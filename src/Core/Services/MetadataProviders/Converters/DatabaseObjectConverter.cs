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
        private const string ESCAPED_DOLLARCHAR = "_$";

        public override DatabaseObject Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using (JsonDocument document = JsonDocument.ParseValue(ref reader))
            {
                JsonElement root = document.RootElement;
                string typeName = root.GetProperty(TYPE_NAME).GetString() ?? throw new JsonException("TypeName is missing");

                Type concreteType = GetTypeFromName(typeName);

                DatabaseObject objA = (DatabaseObject)JsonSerializer.Deserialize(document, concreteType, options)!;

                foreach (PropertyInfo prop in objA.GetType().GetProperties())
                {
                    if (IsSourceDefinitionProperty(prop))
                    {
                        SourceDefinition? sourceDef = (SourceDefinition?)prop.GetValue(objA);
                        if (sourceDef is not null)
                        {
                            UnescapeDollaredColumns(sourceDef);
                        }
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
                Type propType = prop.PropertyType;

                // enforcing that we only escape columns for properties whose type is exactly SourceDefinition(DatabaseTables)
                if (IsSourceDefinitionProperty(prop) && propVal is SourceDefinition sourceDef && propVal.GetType() == typeof(SourceDefinition))
                {
                    EscapeDollaredColumns(sourceDef);
                }

                JsonSerializer.Serialize(writer, propVal, propType, options);
            }

            writer.WriteEndObject();
        }

        private static bool IsSourceDefinitionProperty(PropertyInfo prop)
        {
            // Only return true for properties whose type is exactly SourceDefinition (not subclasses)
            return prop.PropertyType == typeof(SourceDefinition);
        }

        /// <summary>
        /// Escapes column keys that start with '$' to '_$' for serialization.
        /// </summary>
        private static void EscapeDollaredColumns(SourceDefinition sourceDef)
        {
            if (sourceDef.Columns is null || sourceDef.Columns.Count == 0)
            {
                return;
            }

            List<string> keysToEscape = sourceDef.Columns.Keys
                .Where(k => k.StartsWith(DOLLAR_CHAR, StringComparison.Ordinal))
                .ToList();

            foreach (string key in keysToEscape)
            {
                ColumnDefinition col = sourceDef.Columns[key];
                sourceDef.Columns.Remove(key);
                string newKey = ESCAPED_DOLLARCHAR + key[1..];
                sourceDef.Columns[newKey] = col;
            }
        }

        /// <summary>
        /// Unescapes column keys that start with '_$' to '$' for deserialization.
        /// </summary>
        private static void UnescapeDollaredColumns(SourceDefinition sourceDef)
        {
            if (sourceDef.Columns is null || sourceDef.Columns.Count == 0)
            {
                return;
            }

            List<string> keysToUnescape = sourceDef.Columns.Keys
                .Where(k => k.StartsWith(ESCAPED_DOLLARCHAR, StringComparison.Ordinal))
                .ToList();

            foreach (string key in keysToUnescape)
            {
                ColumnDefinition col = sourceDef.Columns[key];
                sourceDef.Columns.Remove(key);
                string newKey = DOLLAR_CHAR + key[2..];
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
