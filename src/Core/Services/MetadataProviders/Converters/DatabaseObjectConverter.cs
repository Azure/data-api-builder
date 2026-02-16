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

                foreach (PropertyInfo prop in objA.GetType().GetProperties().Where(IsSourceDefinitionOrDerivedClassProperty))
                {
                    SourceDefinition? sourceDef = (SourceDefinition?)prop.GetValue(objA);
                    if (sourceDef is not null)
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

            // Track SourceDefinition objects and their original Columns dictionaries
            // to restore them after serialization (avoiding permanent mutation)
            Dictionary<SourceDefinition, Dictionary<string, ColumnDefinition>> originalColumnsDictionaries = new();

            try
            {
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
                    // Temporarily replace dictionary contents with escaped version, then restore after serialization.
                    if (IsSourceDefinitionOrDerivedClassProperty(prop) && propVal is SourceDefinition sourceDef)
                    {
                        if (!originalColumnsDictionaries.ContainsKey(sourceDef))
                        {
                            // Store original dictionary contents
                            originalColumnsDictionaries[sourceDef] = new Dictionary<string, ColumnDefinition>(sourceDef.Columns, sourceDef.Columns.Comparer);
                            
                            // Replace dictionary contents with escaped version
                            Dictionary<string, ColumnDefinition> escapedColumns = CreateEscapedColumnsDictionary(sourceDef.Columns);
                            sourceDef.Columns.Clear();
                            foreach (var kvp in escapedColumns)
                            {
                                sourceDef.Columns[kvp.Key] = kvp.Value;
                            }
                        }
                    }

                    JsonSerializer.Serialize(writer, propVal, options);
                }

                writer.WriteEndObject();
            }
            finally
            {
                // Always restore original Columns dictionary contents to avoid mutating the input object
                foreach (var kvp in originalColumnsDictionaries)
                {
                    kvp.Key.Columns.Clear();
                    foreach (var colKvp in kvp.Value)
                    {
                        kvp.Key.Columns[colKvp.Key] = colKvp.Value;
                    }
                }
            }
        }

        private static bool IsSourceDefinitionOrDerivedClassProperty(PropertyInfo prop)
        {
            // Return true for properties whose type is SourceDefinition or any class derived from SourceDefinition
            return typeof(SourceDefinition).IsAssignableFrom(prop.PropertyType);
        }

        /// <summary>
        /// Creates a new dictionary with escaped column keys for serialization.
        /// Uses a double-encoding approach to handle edge cases:
        /// 1. First escapes columns starting with 'DAB_ESCAPE$' to 'DAB_ESCAPE$DAB_ESCAPE$...'
        /// 2. Then escapes columns starting with '$' to 'DAB_ESCAPE$...'
        /// This ensures that even if a column is named 'DAB_ESCAPE$xyz', it will be properly handled.
        /// Returns a new dictionary without modifying the original.
        /// </summary>
        private static Dictionary<string, ColumnDefinition> CreateEscapedColumnsDictionary(Dictionary<string, ColumnDefinition> originalColumns)
        {
            if (originalColumns is null || originalColumns.Count == 0)
            {
                return new Dictionary<string, ColumnDefinition>(StringComparer.InvariantCultureIgnoreCase);
            }

            // Create a new dictionary with the same comparer as the original
            Dictionary<string, ColumnDefinition> escapedColumns = new(originalColumns.Comparer);

            // Step 1: Escape columns that start with the escape sequence itself
            // This prevents collision when a column name already contains 'DAB_ESCAPE$'
            foreach (var kvp in originalColumns)
            {
                string key = kvp.Key;
                ColumnDefinition value = kvp.Value;

                if (key.StartsWith(ESCAPED_DOLLARCHAR, StringComparison.Ordinal))
                {
                    // Double-escape: DAB_ESCAPE$FirstName -> DAB_ESCAPE$DAB_ESCAPE$FirstName
                    escapedColumns[ESCAPED_DOLLARCHAR + key] = value;
                }
                else if (key.StartsWith(DOLLAR_CHAR, StringComparison.Ordinal))
                {
                    // Escape dollar: $FirstName -> DAB_ESCAPE$FirstName
                    escapedColumns[ESCAPED_DOLLARCHAR + key.Substring(1)] = value;
                }
                else
                {
                    // No escaping needed
                    escapedColumns[key] = value;
                }
            }

            return escapedColumns;
        }

        /// <summary>
        /// Unescapes column keys for deserialization using reverse double-encoding:
        /// 1. First unescapes columns starting with 'DAB_ESCAPE$DAB_ESCAPE$' to 'DAB_ESCAPE$...'
        /// 2. Then unescapes columns starting with 'DAB_ESCAPE$' to '$...'
        /// This ensures proper reconstruction of original column names even in edge cases.
        /// Modifies the dictionary in-place during deserialization.
        /// </summary>
        private static void UnescapeDollaredColumns(SourceDefinition sourceDef)
        {
            if (sourceDef.Columns is null || sourceDef.Columns.Count == 0)
            {
                return;
            }

            // Create a new dictionary with unescaped keys
            Dictionary<string, ColumnDefinition> unescapedColumns = new(sourceDef.Columns.Comparer);

            foreach (var kvp in sourceDef.Columns)
            {
                string key = kvp.Key;
                ColumnDefinition value = kvp.Value;

                // Step 1: Unescape columns that were double-escaped (originally started with 'DAB_ESCAPE$')
                string doubleEscapeSequence = ESCAPED_DOLLARCHAR + ESCAPED_DOLLARCHAR;
                if (key.StartsWith(doubleEscapeSequence, StringComparison.Ordinal))
                {
                    // Remove the first 'DAB_ESCAPE$' prefix: DAB_ESCAPE$DAB_ESCAPE$FirstName -> DAB_ESCAPE$FirstName
                    unescapedColumns[key.Substring(ESCAPED_DOLLARCHAR.Length)] = value;
                }
                // Step 2: Unescape columns that start with 'DAB_ESCAPE$' (originally started with '$')
                else if (key.StartsWith(ESCAPED_DOLLARCHAR, StringComparison.Ordinal))
                {
                    // Add back the '$' prefix: DAB_ESCAPE$FirstName -> $FirstName
                    unescapedColumns[DOLLAR_CHAR + key.Substring(ESCAPED_DOLLARCHAR.Length)] = value;
                }
                else
                {
                    // No unescaping needed
                    unescapedColumns[key] = value;
                }
            }

            // Replace the dictionary contents with the unescaped version
            sourceDef.Columns.Clear();
            foreach (var kvp in unescapedColumns)
            {
                sourceDef.Columns[kvp.Key] = kvp.Value;
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
