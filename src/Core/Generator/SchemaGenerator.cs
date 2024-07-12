// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;
using Azure.DataApiBuilder.Core.Extensions;
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

        private Dictionary<string, string> _gqlTypes = new ();
        private HashSet<string> _processedTypes = new ();

        /// <summary>
        /// Processes a JArray of JSON objects and generates a GraphQL schema.
        /// </summary>
        /// <param name="jsonArray">Sampled JSON Data</param>
        /// <param name="containerName">Cosmos DB Container Name</param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException">If JsonArray or Container Name is Empty or null</exception>
        public static string Run(JArray jsonArray, string containerName)
        {
            // Validating if passed inputs are not null or empty
            if (jsonArray == null || jsonArray.Count == 0 || string.IsNullOrEmpty(containerName))
            {
                throw new InvalidOperationException("JArray must contain at least one JSON object.");
            }

            return new SchemaGenerator().ConvertJsonToGQLSchema(jsonArray, containerName);
        }

        /// <summary>
        /// This method converts a JArray of JSON objects to a GraphQL schema.
        /// </summary>
        /// <param name="jsonArray"></param>
        /// <param name="containerName"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException">If JArray doesn't contains JObject</exception>
        private string ConvertJsonToGQLSchema(JArray jsonArray, string containerName)
        {
            Dictionary<string, string> gqlFields = new ();

            foreach (JToken token in jsonArray)
            {
                if (token is JObject jsonObject)
                {
                    Console.WriteLine($"-----------------------------");
                    TraverseJsonObject(jsonObject, gqlFields, containerName.ToPascalCase());
                }
                else
                {
                    throw new InvalidOperationException("JArray must contain JSON objects only.");
                }
            }

            GenerateSchema(gqlFields, containerName.ToPascalCase(), true);

            StringBuilder sb = new ();
            foreach (KeyValuePair<string, string> gqlType in _gqlTypes)
            {
                sb.AppendLine(gqlType.Value);
                sb.AppendLine();
            }

            return sb.ToString().Trim();
        }

        /// <summary>
        /// This function takes the JSON Object and Traverse through it to collect all the GQL Entities which corresponding schema
        /// </summary>
        /// <param name="jsonObject"></param>
        /// <param name="gqlFields"></param>
        /// <param name="parentType"></param>
        private void TraverseJsonObject(JObject jsonObject, Dictionary<string, string> gqlFields, string parentType)
        {
            Console.WriteLine($"Parent Type: {parentType}");
            foreach (JProperty property in jsonObject.Properties())
            {
                Console.WriteLine($"Property Name: {property.Name} Value: {property.Value}");
                // Skipping if the property is reserved Cosmos DB property
                if (_cosmosDbReservedProperties.Contains(property.Name))
                {
                    continue;
                }

                gqlFields[property.Name] = $"{property.Name}: {ProcessJsonToken(property.Value, property.Name, parentType, false)}";
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
                        string objectTypeName = fieldName.ToPascalCase();
                        if (!_processedTypes.Contains(objectTypeName))
                        {
                            _processedTypes.Add(objectTypeName);

                            Dictionary<string, string> objectFields = new();
                            TraverseJsonObject((JObject)token, objectFields, objectTypeName);

                            GenerateSchema(objectFields, objectTypeName);
                        }

                        gqlFieldType = objectTypeName;
                        break;

                    case JTokenType.Array:
                        gqlFieldType = ProcessJsonArray((JArray)token, fieldName, parentType);
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
                        // Example: Representing date as ISO 8601 string
                        gqlFieldType = "String";
                        break;

                    default:
                        throw new InvalidOperationException($"Unsupported JTokenType: {token.Type}");
                }

            }

            if (isArray)
            {
                return $"[{gqlFieldType}]";
            }

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
            // Take first element out of the array, Assuming array contains similar objects and process it
            return ProcessJsonToken(jsonArray[0], fieldName, parentType, true);
        }

        /// <summary>
        /// Converted collected GQL field information to the GQL Schema
        /// </summary>
        /// <param name="gqlFields"></param>
        /// <param name="typeName"></param>
        /// <param name="isRoot"></param>
        private void GenerateSchema(Dictionary<string, string> gqlFields, string typeName, bool isRoot = false)
        {
            StringBuilder sb = new ();
            sb.AppendLine($"type {typeName} {(isRoot ? "@model " : "")}{{");

            foreach (string field in gqlFields.Values)
            {
                sb.AppendLine($"  {field}");
            }

            sb.AppendLine("}");

            _gqlTypes[typeName] = sb.ToString();
        }
    }
}
