// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Globalization;
using System.Text;
using System.Text.Json;
using Azure.DataApiBuilder.Config.ObjectModel;
using Humanizer;
using static System.Text.Json.JsonElement;

namespace Azure.DataApiBuilder.Core.Generator
{
    /// <summary>
    /// This Class takes a JArray of JSON objects and generates a GraphQL schema.
    /// </summary>
    internal class SchemaGenerator
    {
        // Cosmos DB reserved properties, these properties will be ignored in the schema generation as they are not user-defined properties.
        private readonly List<string> _cosmosDbReservedProperties = new() { "_ts", "_etag", "_rid", "_self", "_attachments" };

        // Contains the mapping of GQL Entities and their corresponding attributes.
        private Dictionary<string, HashSet<AttributeObject>> _attrMapping = new();

        private List<JsonDocument> _data;
        private string _containerName;
        private RuntimeConfig? _config;

        private SchemaGenerator(List<JsonDocument> data, string containerName, RuntimeConfig? config)
        {
            this._data = data;
            this._containerName = containerName;
            this._config = config;
        }

        /// <summary>
        /// Processes a JArray of JSON objects and generates a GraphQL schema.
        /// </summary>
        /// <param name="jsonData">Sampled JSON Data</param>
        /// <param name="containerName">Cosmos DB Container Name</param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException">If JsonArray or Container Name is Empty or null</exception>
        public static string Generate(List<JsonDocument> jsonData, string containerName, RuntimeConfig? config = null)
        {
            // Validating if passed inputs are not null or empty
            if (jsonData == null || jsonData.Count == 0 || string.IsNullOrEmpty(containerName))
            {
                throw new InvalidOperationException("JArray must contain at least one JSON object and Container Name can not be blank");
            }

            return new SchemaGenerator(jsonData, containerName, config)
                        .ConvertJsonToGQLSchema();
        }

        /// <summary>
        /// This method takes data and container name, from which GQL Schema needs to be fetched.
        /// </summary>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException">If JArray doesn't contains JObject</exception>
        private string ConvertJsonToGQLSchema()
        {
            // Process each JSON object in the JArray to collect GQL Entities and their attributes.
            foreach (JsonDocument token in _data)
            {
                if (token is JsonDocument jsonObject)
                {
                    TraverseJsonObject(jsonObject, _containerName);
                }
                else
                {
                    throw new InvalidOperationException("JArray must contain JSON objects only.");
                }
            }

            // Generate string out of the traversed information
            return GenerateGQLSchema();
        }

        /// <summary>
        /// Take Traversed information and convert that to GQL string
        /// </summary>
        /// <param name="containerName"></param>
        /// <returns></returns>
        private string GenerateGQLSchema()
        {
            StringBuilder sb = new();
            foreach (KeyValuePair<string, HashSet<AttributeObject>> entity in _attrMapping)
            {
                bool isRoot = entity.Key == _containerName.Pascalize();
                sb.AppendLine($"type {entity.Key} {(isRoot ? "@model " : "")}{{");

                int counter = 0;
                foreach (AttributeObject field in entity.Value)
                {
                    sb.Append($"  {field.GetString(_data.Count)}");

                    if (counter != entity.Value.Count - 1)
                    {
                        sb.AppendLine(",");
                        counter++;

                        continue;
                    }

                    sb.AppendLine();
                }

                sb.AppendLine("}");
            }

            return sb.ToString().Trim();
        }

        /// <summary>
        /// This function takes the JSON Object and Traverse through it to collect all the GQL Entities which corresponding schema
        /// </summary>
        /// <param name="jsonObject"></param>
        /// <param name="parentType"></param>
        private void TraverseJsonObject(JsonDocument jsonObject, string parentType)
        {
            foreach (JsonProperty property in jsonObject.RootElement.EnumerateObject())
            {
                // Skipping if the property is reserved Cosmos DB property
                if (_cosmosDbReservedProperties.Contains(property.Name))
                {
                    continue;
                }

                if (_config != null && !_config.Entities.Entities.ContainsKey(property.Name))
                {
                    continue;
                }

                ProcessJsonToken(property.Value, property.Name, parentType, false);
            }
        }

        /// <summary>
        /// Traverse through the JSON Token and generate the GQL Field, it runs the same logic recursively also, for nested objects.
        /// </summary>
        /// <param name="token"></param>
        /// <param name="fieldName"></param>
        /// <param name="parentType"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        private string ProcessJsonToken(JsonElement token, string fieldName, string parentType, bool isArray, int parentArrayLength = 1)
        {
            parentType = parentType.Pascalize();

            string gqlFieldType = "String";
            // If field name is "id" then it will be represented as ID in GQL
            if (fieldName == "id")
            {
                gqlFieldType = "ID";
            }
            else
            {
                // Rest of the fields will be processed based on their type
                switch (token.ValueKind)
                {
                    case JsonValueKind.Object:
                    {
                        string objectTypeName = fieldName.Pascalize();

                        if (isArray)
                        {
                            objectTypeName = objectTypeName.Singularize();
                        }

                        TraverseJsonObject(JsonDocument.Parse(token.GetRawText()), objectTypeName);

                        gqlFieldType = objectTypeName;
                        break;
                    }
                    case JsonValueKind.Array:
                        gqlFieldType = ProcessJsonArray(token, fieldName, parentType.Singularize());
                        break;

                    case JsonValueKind.Number:
                        if (token.TryGetInt32(out int _))
                        {
                            gqlFieldType = "Int";
                        }
                        else if (token.TryGetDouble(out double _))
                        {
                            gqlFieldType = "Float";
                        }

                        break;
                    case JsonValueKind.True:
                    case JsonValueKind.False:
                        gqlFieldType = "Boolean";
                        break;
                    case JsonValueKind.Null or JsonValueKind.String:
                        if (DateTime.TryParse(token.GetString(), DateTimeFormatInfo.InvariantInfo, DateTimeStyles.None, out DateTime _))
                        {
                            gqlFieldType = "Date";
                        }
                        else
                        {
                            gqlFieldType = "String";
                        }

                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported token.ValueKind: {token.ValueKind}");
                }
            }

            AddOrUpdateAttributeInfo(token, fieldName, parentType, isArray, gqlFieldType, parentArrayLength);

            return gqlFieldType;
        }

        /// <summary>
        /// Process first element of the JSON Array and generate the GQL Field, if array is empty consider it array of string
        /// </summary>
        /// <param name="jsonArray"></param>
        /// <param name="fieldName"></param>
        /// <param name="parentType"></param>
        /// <returns></returns>
        private string ProcessJsonArray(JsonElement jsonArray, string fieldName, string parentType)
        {
            HashSet<string> gqlFieldType = new();
            ArrayEnumerator arrayEnumerator = jsonArray.EnumerateArray();

            // Process each element of the array
            foreach (JsonElement obj in arrayEnumerator)
            {
                gqlFieldType.Add(ProcessJsonToken(obj, fieldName, parentType, true, arrayEnumerator.Count()));
            }

            if (gqlFieldType.Count is not 1)
            {
                throw new InvalidOperationException($"Same attributes {parentType} contains multiple types of elements.");
            }

            return gqlFieldType.First<string>();
        }

        /// <summary>
        /// Add or Update the Attribute Information in the Entity Map
        /// </summary>
        /// <param name="token"></param>
        /// <param name="fieldName"></param>
        /// <param name="parentType"></param>
        /// <param name="isArray"></param>
        /// <param name="gqlFieldType"></param>
        private void AddOrUpdateAttributeInfo(JsonElement token, string fieldName, string parentType, bool isArray, string gqlFieldType, int parentArrayLength)
        {
            // Check if this attribute is already recorded for this entity
            if (!_attrMapping.ContainsKey(parentType))
            {
                AttributeObject attributeObject = new(name: fieldName,
                        type: gqlFieldType,
                        parent: parentType,
                        isArray: isArray,
                        value: token.ValueKind,
                        arrayLength: parentArrayLength);

                _attrMapping.Add(parentType, new HashSet<AttributeObject>() { attributeObject });
            }
            else
            {
                AttributeObject? attributeObject = _attrMapping[parentType].FirstOrDefault(a => a.Name == fieldName);
                if (attributeObject is null)
                {
                    attributeObject = new(name: fieldName,
                        type: gqlFieldType,
                        parent: parentType,
                        isArray: isArray,
                        value: token.ValueKind,
                        arrayLength: parentArrayLength);

                    _attrMapping[parentType].Add(attributeObject);
                }
                else if (token.ValueKind is not JsonValueKind.Null)
                {
                    attributeObject.Count++;
                }

                attributeObject.ParentArrayLength += parentArrayLength;
            }
        }
    }
}
