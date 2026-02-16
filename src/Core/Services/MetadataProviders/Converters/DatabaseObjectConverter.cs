// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.DatabasePrimitives;

namespace Azure.DataApiBuilder.Core.Services.MetadataProviders.Converters
{
    /// <summary>
    /// This is a converter to serialize and deserialize the DatabaseObject
    /// this adds a typename field at the object level, this is used when you deserialize the different child objects : DatabaseTable,
    /// DatabaseView as DatabaseObject is an abstract class
    /// This is also required as there is no TypeNameHandling support in System.text.Json and we need to explicitly add a TypeName field to distinguish
    /// </summary>
    public class DatabaseObjectConverter : JsonConverter<DatabaseObject>
    {
        private const string TYPE_NAME = "TypeName";
        private const string DOLLAR_CHAR = "$";

        // ``DAB_ESCAPE$`` is used to escape column names that start with `$` during serialization.
        // It is chosen to be unique enough to avoid collisions with actual column names.
        private const string ESCAPED_DOLLARCHAR = "DAB_ESCAPE$";

        public override DatabaseObject Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using (JsonDocument document = JsonDocument.ParseValue(ref reader))
            {
                JsonElement root = document.RootElement;
                string typeName = root.GetProperty(TYPE_NAME).GetString() ?? throw new JsonException("TypeName is missing");

                Type concreteType = GetTypeFromName(typeName);

                DatabaseObject objA = (DatabaseObject)JsonSerializer.Deserialize(document, concreteType, options)!;

                // Track SourceDefinition objects we've already unescaped to avoid double-unescaping
                // (SourceDefinition and TableDefinition/ViewDefinition/StoredProcedureDefinition can reference the same object)
                HashSet<SourceDefinition> unescapedSourceDefs = new();
                foreach (PropertyInfo prop in objA.GetType().GetProperties().Where(IsSourceDefinitionOrDerivedClassProperty))
                {
                    SourceDefinition? sourceDef = (SourceDefinition?)prop.GetValue(objA);
                    if (sourceDef is not null && unescapedSourceDefs.Add(sourceDef))
                    {
                        UnescapeDollaredColumns(sourceDef);
                    }
                }

                return objA;
            }
        }

        public override void Write(Utf8JsonWriter writer, DatabaseObject value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                throw new ArgumentNullException("Database Object being serialised cannot be null");
            }

            writer.WriteStartObject();

            // Add TypeName property in DatabaseObject object that we are serializing based on its type. (DatabaseTable, DatabaseView)
            // We add this property to differentiate between them in the dictionary. This extra property gets used in deserialization above.
            // for example if object is DatabaseTable then we need to add
            // "TypeName": "Azure.DataApiBuilder.Config.DatabasePrimitives.DatabaseTable, Azure.DataApiBuilder.Config",
            writer.WriteString(TYPE_NAME, GetTypeNameFromType(value.GetType()));

            // Track SourceDefinition objects we've already escaped to avoid double-escaping
            // (SourceDefinition and TableDefinition/ViewDefinition/StoredProcedureDefinition can reference the same object)
            HashSet<SourceDefinition> escapedSourceDefs = new();

            // Add other properties of DatabaseObject
            foreach (PropertyInfo prop in value.GetType().GetProperties())
            {
                // Skip the TypeName property, as it has been handled above
                if (prop.Name == TYPE_NAME)
                {
                    continue;
                }

                writer.WritePropertyName(prop.Name);
                object? propVal = prop.GetValue(value);

                // Only escape columns for properties whose type(derived type) is SourceDefinition.
                // Use HashSet to avoid double-escaping when multiple properties reference the same SourceDefinition object.
                if (IsSourceDefinitionOrDerivedClassProperty(prop) && propVal is SourceDefinition sourceDef)
                {
                    if (escapedSourceDefs.Add(sourceDef))
                    {
                        EscapeDollaredColumns(sourceDef);
                    }
                }

                JsonSerializer.Serialize(writer, propVal, options);
            }

            writer.WriteEndObject();
        }

        private static bool IsSourceDefinitionOrDerivedClassProperty(PropertyInfo prop)
        {
            // Return true for properties whose type is SourceDefinition or any class derived from SourceDefinition
            return typeof(SourceDefinition).IsAssignableFrom(prop.PropertyType);
        }

        /// <summary>
        /// Escapes column keys that start with '$' or 'DAB_ESCAPE$' for serialization.
        /// Uses a double-encoding approach to handle edge cases:
        /// 1. First escapes columns starting with 'DAB_ESCAPE$' to 'DAB_ESCAPE$DAB_ESCAPE$...'
        /// 2. Then escapes columns starting with '$' to 'DAB_ESCAPE$...'
        /// This ensures that even if a column is named 'DAB_ESCAPE$xyz', it will be properly handled.
        /// </summary>
        private static void EscapeDollaredColumns(SourceDefinition sourceDef)
        {
            if (sourceDef.Columns is null || sourceDef.Columns.Count == 0)
            {
                return;
            }

            // Build a list of all renames to apply atomically to avoid key collisions during in-place mutation.
            List<(string oldKey, string newKey)> renames = new();

            // Step 1: Escape columns that start with the escape sequence itself ('DAB_ESCAPE$...' → 'DAB_ESCAPE$DAB_ESCAPE$...')
            foreach (string key in sourceDef.Columns.Keys)
            {
                if (key.StartsWith(ESCAPED_DOLLARCHAR, StringComparison.Ordinal))
                {
                    renames.Add((key, ESCAPED_DOLLARCHAR + key));
                }
            }

            // Step 2: Escape columns that start with '$' ('$...' → 'DAB_ESCAPE$...')
            foreach (string key in sourceDef.Columns.Keys)
            {
                if (key.StartsWith(DOLLAR_CHAR, StringComparison.Ordinal) &&
                    !key.StartsWith(ESCAPED_DOLLARCHAR, StringComparison.Ordinal))
                {
                    renames.Add((key, ESCAPED_DOLLARCHAR + key[1..]));
                }
            }

            // Apply all renames
            foreach ((string oldKey, string newKey) in renames)
            {
                ColumnDefinition col = sourceDef.Columns[oldKey];
                sourceDef.Columns.Remove(oldKey);
                sourceDef.Columns[newKey] = col;
            }
        }

        /// <summary>
        /// Unescapes column keys for deserialization using reverse double-encoding:
        /// 1. First unescapes columns starting with 'DAB_ESCAPE$' (that are NOT double-escaped) to '$...'
        /// 2. Then unescapes columns starting with 'DAB_ESCAPE$DAB_ESCAPE$' to 'DAB_ESCAPE$...'
        /// All renames are computed upfront and applied atomically to avoid key collisions.
        /// </summary>
        private static void UnescapeDollaredColumns(SourceDefinition sourceDef)
        {
            if (sourceDef.Columns is null || sourceDef.Columns.Count == 0)
            {
                return;
            }

            string doubleEscapeSequence = ESCAPED_DOLLARCHAR + ESCAPED_DOLLARCHAR;

            // Build a list of all renames to apply atomically to avoid key collisions during in-place mutation.
            List<(string oldKey, string newKey)> renames = new();

            foreach (string key in sourceDef.Columns.Keys)
            {
                if (key.StartsWith(doubleEscapeSequence, StringComparison.Ordinal))
                {
                    // Double-escaped: 'DAB_ESCAPE$DAB_ESCAPE$...' → 'DAB_ESCAPE$...'
                    renames.Add((key, key[ESCAPED_DOLLARCHAR.Length..]));
                }
                else if (key.StartsWith(ESCAPED_DOLLARCHAR, StringComparison.Ordinal))
                {
                    // Single-escaped: 'DAB_ESCAPE$...' → '$...'
                    renames.Add((key, DOLLAR_CHAR + key[ESCAPED_DOLLARCHAR.Length..]));
                }
            }

            // Apply all renames
            foreach ((string oldKey, string newKey) in renames)
            {
                ColumnDefinition col = sourceDef.Columns[oldKey];
                sourceDef.Columns.Remove(oldKey);
                sourceDef.Columns[newKey] = col;
            }
        }

        private static Type GetTypeFromName(string typeName)
        {
            Type? type = Type.GetType(typeName);

            if (type == null)
            {
                throw new JsonException($"Could not find type: {typeName}");
            }

            return type;
        }

        /// <summary>
        /// Changes the Type.AssemblyQualifiedName to desired format
        /// we cannot use the FullName as during deserialization it throws exception as object not found.
        /// </summary>
        private static string GetTypeNameFromType(Type type)
        {
            // AssemblyQualifiedName for the type looks like :
            // "Azure.DataApiBuilder.Config.DatabasePrimitives.DatabaseTable, Azure.DataApiBuilder.Config, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
            // we donot need version or culture or publickeytoken for serialization or deserialization, we need the first two parts.

            string assemblyQualifiedName = type.AssemblyQualifiedName!;
            string[] parts = assemblyQualifiedName.Split(',');

            // typename would be : "Azure.DataApiBuilder.Config.DatabasePrimitives.DatabaseTable, Azure.DataApiBuilder.Config"
            string typeName = $"{parts[0]},{parts[1]}";
            return typeName;
        }
    }
}
