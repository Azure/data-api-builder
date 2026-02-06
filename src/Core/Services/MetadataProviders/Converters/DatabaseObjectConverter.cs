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
                if (IsSourceDefinitionOrDerivedClassProperty(prop) && propVal is SourceDefinition sourceDef)
                {
                    // Check if we need to escape any column names
                    propVal = GetSourceDefinitionWithEscapedColumns(sourceDef);
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
        /// Returns the SourceDefinition as-is if no columns start with '$', 
        /// otherwise returns a copy with escaped column names.
        /// </summary>
        private static SourceDefinition GetSourceDefinitionWithEscapedColumns(SourceDefinition sourceDef)
        {
            if (sourceDef == null)
            {
                throw new ArgumentNullException(nameof(sourceDef));
            }

            // If no columns or no columns starting with '$', return original
            if (sourceDef.Columns is null || sourceDef.Columns.Count == 0 ||
                !sourceDef.Columns.Keys.Any(k => k.StartsWith(DOLLAR_CHAR, StringComparison.Ordinal)))
            {
                return sourceDef;
            }

            // Create escaped columns dictionary
            Dictionary<string, ColumnDefinition> escapedColumns = CreateEscapedColumnsDictionary(sourceDef.Columns);
            
            // Create new instance based on the actual type
            return sourceDef switch
            {
                StoredProcedureDefinition spDef => CreateStoredProcedureDefinition(spDef, escapedColumns),
                _ => CreateSourceDefinition(sourceDef, escapedColumns)
            };
        }

        /// <summary>
        /// Creates a dictionary with escaped column keys for serialization.
        /// </summary>
        private static Dictionary<string, ColumnDefinition> CreateEscapedColumnsDictionary(IDictionary<string, ColumnDefinition> originalColumns)
        {
            Dictionary<string, ColumnDefinition> escapedColumns = new();
            foreach (KeyValuePair<string, ColumnDefinition> kvp in originalColumns)
            {
                if (kvp.Key.StartsWith(DOLLAR_CHAR, StringComparison.Ordinal))
                {
                    string escapedKey = ESCAPED_DOLLARCHAR + kvp.Key[1..];
                    escapedColumns[escapedKey] = kvp.Value;
                }
                else
                {
                    escapedColumns[kvp.Key] = kvp.Value;
                }
            }

            return escapedColumns;
        }

        /// <summary>
        /// Creates a new StoredProcedureDefinition with escaped columns and copied properties.
        /// </summary>
        private static StoredProcedureDefinition CreateStoredProcedureDefinition(StoredProcedureDefinition original, Dictionary<string, ColumnDefinition> escapedColumns)
        {
            StoredProcedureDefinition newSpDef = new()
            {
                IsInsertDMLTriggerEnabled = original.IsInsertDMLTriggerEnabled,
                IsUpdateDMLTriggerEnabled = original.IsUpdateDMLTriggerEnabled,
                PrimaryKey = original.PrimaryKey
            };
                
            // Add escaped columns
            AddColumnsToDictionary(newSpDef.Columns, escapedColumns);
                
            // Copy parameters
            AddParametersToDictionary(newSpDef.Parameters, original.Parameters);

            // Copy relationship map if it exists
            CopyRelationshipMap(newSpDef.SourceEntityRelationshipMap, original.SourceEntityRelationshipMap);
                
            return newSpDef;
        }

        /// <summary>
        /// Creates a new SourceDefinition with escaped columns and copied properties.
        /// </summary>
        private static SourceDefinition CreateSourceDefinition(SourceDefinition original, Dictionary<string, ColumnDefinition> escapedColumns)
        {
            SourceDefinition newSourceDef = new()
            {
                IsInsertDMLTriggerEnabled = original.IsInsertDMLTriggerEnabled,
                IsUpdateDMLTriggerEnabled = original.IsUpdateDMLTriggerEnabled,
                PrimaryKey = original.PrimaryKey
            };
                
            // Add escaped columns
            AddColumnsToDictionary(newSourceDef.Columns, escapedColumns);

            // Copy relationship map if it exists
            CopyRelationshipMap(newSourceDef.SourceEntityRelationshipMap, original.SourceEntityRelationshipMap);
                
            return newSourceDef;
        }

        /// <summary>
        /// Helper method to add columns to a dictionary.
        /// </summary>
        private static void AddColumnsToDictionary(IDictionary<string, ColumnDefinition> target, Dictionary<string, ColumnDefinition> source)
        {
            foreach (KeyValuePair<string, ColumnDefinition> kvp in source)
            {
                target.Add(kvp.Key, kvp.Value);
            }
        }

        /// <summary>
        /// Helper method to add parameters to a dictionary.
        /// </summary>
        private static void AddParametersToDictionary(IDictionary<string, ParameterDefinition> target, IDictionary<string, ParameterDefinition> source)
        {
            foreach (KeyValuePair<string, ParameterDefinition> kvp in source)
            {
                target.Add(kvp.Key, kvp.Value);
            }
        }

        /// <summary>
        /// Helper method to copy relationship map between SourceDefinition instances.
        /// </summary>
        private static void CopyRelationshipMap(IDictionary<string, RelationshipMetadata> target, IDictionary<string, RelationshipMetadata> source)
        {
            foreach (KeyValuePair<string, RelationshipMetadata> kvp in source)
            {
                target.Add(kvp.Key, kvp.Value);
            }
        }

        /// <summary>
        /// Unescapes column keys that start with 'DAB_ESCAPE$' by removing the prefix and restoring the original '$' for deserialization.
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
                string newKey = DOLLAR_CHAR + key[ESCAPED_DOLLARCHAR.Length..];
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
