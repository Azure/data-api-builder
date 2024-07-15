// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;
using DotNetEnv;
using Humanizer;
using Newtonsoft.Json.Linq;

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

        private JArray _data;
        private string _containerName;

        private SchemaGenerator(JArray data, string containerName)
        {
            this._data = data;
            this._containerName = containerName;
        }

        /// <summary>
        /// Processes a JArray of JSON objects and generates a GraphQL schema.
        /// </summary>
        /// <param name="jsonData">Sampled JSON Data</param>
        /// <param name="containerName">Cosmos DB Container Name</param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException">If JsonArray or Container Name is Empty or null</exception>
        public static string Generate(JArray jsonData, string containerName)
        {
            // Validating if passed inputs are not null or empty
            if (jsonData == null || jsonData.Count == 0 || string.IsNullOrEmpty(containerName))
            {
                throw new InvalidOperationException("JArray must contain at least one JSON object and Container Name can not be blank");
            }

            return new SchemaGenerator(jsonData, containerName)
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
            foreach (JToken token in _data)
            {
                if (token is JObject jsonObject)
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

                foreach (AttributeObject field in entity.Value)
                {
                    sb.AppendLine($"  {field.GetString(_data.Count)}");
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
        private void TraverseJsonObject(JObject jsonObject, string parentType)
        {
            foreach (JProperty property in jsonObject.Properties())
            {
                // Skipping if the property is reserved Cosmos DB property
                if (_cosmosDbReservedProperties.Contains(property.Name))
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
        private string ProcessJsonToken(JToken token, string fieldName, string parentType, bool isArray)
        {
            parentType = parentType.Pascalize();

            string gqlFieldType;
            // If field name is "id" then it will be represented as ID in GQL
            if (fieldName == "id")
            {
                gqlFieldType = "ID";
            }
            else
            {
                // Rest of the fields will be processed based on their type
                switch (token.Type)
                {
                    case JTokenType.Object:
                    {
                        string objectTypeName = fieldName.Pascalize();

                        if (isArray)
                        {
                            objectTypeName = objectTypeName.Singularize();
                        }

                        TraverseJsonObject((JObject)token, objectTypeName);

                        gqlFieldType = objectTypeName;
                        break;
                    }
                    case JTokenType.Array:
                        gqlFieldType = ProcessJsonArray((JArray)token, fieldName, parentType.Singularize());
                        break;

                    case JTokenType.Integer:
                        gqlFieldType = "Int";
                        break;

                    case JTokenType.Float:
                        gqlFieldType = "Float";
                        break;

                    case JTokenType.Boolean:
                        gqlFieldType = "Boolean";
                        break;

                    case JTokenType.Date or JTokenType.Null or JTokenType.String:
                        gqlFieldType = "String";
                        break;

                    default:
                        throw new InvalidOperationException($"Unsupported JTokenType: {token.Type}");
                }

            }

            AddOrUpdateAttributeInfo(token, fieldName, parentType, isArray, gqlFieldType);

            return gqlFieldType;
        }

        /// <summary>
        /// Process first element of the JSON Array and generate the GQL Field, if array is empty consider it array of string
        /// </summary>
        /// <param name="jsonArray"></param>
        /// <param name="fieldName"></param>
        /// <param name="parentType"></param>
        /// <returns></returns>
        private string ProcessJsonArray(JArray jsonArray, string fieldName, string parentType)
        {
            if (jsonArray.Count == 0)
            {
                return "String";
            }

            // Take first element out of the array, Assuming array contains similar objects and process it
            return ProcessJsonToken(jsonArray[0], fieldName, parentType, true);
        }

        /// <summary>
        /// Add or Update the Attribute Information in the Entity Map
        /// </summary>
        /// <param name="token"></param>
        /// <param name="fieldName"></param>
        /// <param name="parentType"></param>
        /// <param name="isArray"></param>
        /// <param name="gqlFieldType"></param>
        private void AddOrUpdateAttributeInfo(JToken token, string fieldName, string parentType, bool isArray, string gqlFieldType)
        {
            object? value = (token is JValue jValue) ? jValue.Value : gqlFieldType;
            // Check if this attribute is already recorded for this entity
            if (!_attrMapping.ContainsKey(parentType))
            {
                _attrMapping.Add(parentType, new HashSet<AttributeObject>()
                {
                    new(name: fieldName,
                        type: gqlFieldType,
                        parent: parentType,
                        isArray: isArray,
                        value: value)
                });
            }
            else
            {
                AttributeObject? attributeObject = _attrMapping[parentType].FirstOrDefault(a => a.Name == fieldName);
                if(attributeObject is null)
                {
                    _attrMapping[parentType].Add(new(name: fieldName,
                        type: gqlFieldType,
                        parent: parentType,
                        isArray: isArray,
                        value: value));
                }
                else if(value is not null)
                {
                    attributeObject.Values.Add(value);
                }
            }

        }
    }
}
