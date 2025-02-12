// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Globalization;
using System.Text;
using System.Text.Json;
using Azure.DataApiBuilder.Config.ObjectModel;
using Humanizer;
using Microsoft.Extensions.Logging;
using static System.Text.Json.JsonElement;

namespace Azure.DataApiBuilder.Core.Generator
{
    /// <summary>
    /// The <see cref="SchemaGenerator"/> class generates a GraphQL schema from a collection of JSON objects. 
    /// It processes the JSON data, maps attributes to GraphQL types, and creates a schema definition.
    /// </summary>
    internal class SchemaGenerator
    {
        // Azure Cosmos DB reserved properties, these properties will be ignored in the schema generation as they are not user-defined properties.
        private readonly List<string> _cosmosDbReservedProperties = new() { "_ts", "_etag", "_rid", "_self", "_attachments" };

        // Logger instance for logging messages.
        private readonly ILogger? _logger;

        // Maps GraphQL entities to their corresponding attributes.
        private Dictionary<string, HashSet<AttributeObject>> _attrMapping = new();

        // List of JSON documents to process.
        private List<JsonDocument> _data;
        // Name of the Azure Cosmos DB container from which the JSON data is obtained.
        private string _containerName;
        // Dictionary mapping plural entity names to singular names based on the provided configuration.
        private Dictionary<string, string> _entityAndSingularNameMapping = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="SchemaGenerator"/> class.
        /// </summary>
        /// <param name="data">A list of JSON documents to be used to generate the schema.</param>
        /// <param name="containerName">The name of the Azure Cosmos DB container which is used to generate the GraphQL schema.</param>
        /// <param name="config">Optional configuration that maps GraphQL entity names to their singular forms.</param>
        /// <param name="logger">Optional Logger</param>
        private SchemaGenerator(List<JsonDocument> data, string containerName, RuntimeConfig? config, ILogger? logger)
        {
            this._logger = logger;

            this._data = data;
            this._containerName = containerName;
            if (config != null)
            {
                if (config.Entities == null || config.Entities.Count() == 0)
                {
                    throw new Exception("Define one or more entities in the config file to generate the GraphQL schema.");
                }
                // Populate entity and singular name mapping if configuration is provided.
                foreach (KeyValuePair<string, Entity> item in config.Entities)
                {
                    _entityAndSingularNameMapping.Add(item.Value.GraphQL.Singular.Pascalize(), item.Key);
                }
            }
        }

        /// <summary>
        /// Generates a GraphQL schema from the provided JSON data and container name.
        /// </summary>
        /// <param name="jsonData">A list of JSON documents to generate the schema from.</param>
        /// <param name="containerName">The name of the Azure Cosmos DB container.</param>
        /// <param name="config">Optional configuration that maps GraphQL entity names to their singular forms.</param>
        /// <param name="logger">Optional Logger</param>
        /// <returns>A string representing the generated GraphQL schema.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the JSON data is empty or the container name is null or empty.</exception>
        public static string Generate(List<JsonDocument> jsonData, string containerName, RuntimeConfig? config = null, ILogger? logger = null)
        {
            // Validate input parameters.
            if (string.IsNullOrEmpty(containerName))
            {
                throw new InvalidOperationException("Container name cannot be blank");
            }

            if (jsonData == null || jsonData.Count == 0)
            {
                logger?.LogWarning($"No JSON data found to generate schema, from Container: {containerName}");
                return string.Empty;
            }

            // Create an instance of SchemaGenerator and generate the schema.
            return new SchemaGenerator(jsonData,
                        containerName.Singularize(),
                        config,
                        logger)
                  .ConvertJsonToGQLSchema();
        }

        /// <summary>
        /// Converts the JSON data into a GraphQL schema string.
        /// </summary>
        /// <returns>A string representing the GraphQL schema.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the JSON data contains non-object elements.</exception>
        private string ConvertJsonToGQLSchema()
        {
            // Process each JSON document to build the GraphQL schema.
            foreach (JsonDocument token in _data)
            {
                if (token is JsonDocument jsonToken)
                {
                    this.TraverseJsonObject(jsonToken, _containerName);
                }
                else
                {
                    throw new InvalidOperationException("Invalid JsonDocument found.");
                }
            }

            // Generate the GraphQL schema string from the collected entity information.
            return this.GenerateGQLSchema();
        }

        /// <summary>
        /// Generates a GraphQL schema string from the collected attributes and entities.
        /// </summary>
        /// <returns>A string representing the GraphQL schema.</returns>
        private string GenerateGQLSchema()
        {
            StringBuilder sb = new();

            // Iterate through the collected entities and their attributes to build the schema.
            foreach (KeyValuePair<string, HashSet<AttributeObject>> entity in _attrMapping)
            {
                // Determine if the entity is the root entity.
                bool isRoot = entity.Key == _containerName.Pascalize();

                sb.Append($"type {entity.Key} ");

                // Append model directive if applicable.
                if (_entityAndSingularNameMapping.ContainsKey(entity.Key) && _entityAndSingularNameMapping[entity.Key] != entity.Key)
                {
                    sb.Append($"@model(name:\"{_entityAndSingularNameMapping[entity.Key]}\") ");
                }
                else if (isRoot)
                {
                    sb.Append($"@model(name: \"{entity.Key}\") ");
                }

                sb.AppendLine($"{{");

                // Append fields and their types.
                int counter = 0;
                foreach (AttributeObject field in entity.Value)
                {
                    sb.Append($"  {field.GetString(_data.Count)}");

                    // Add comma if it's not the last field.
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

            if (sb.Length == 0)
            {
                _logger?.LogWarning("Generated GraphQL schema is empty.");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Traverses a JSON object and collects GraphQL entity and attribute information.
        /// </summary>
        /// <param name="jsonObject">The JSON object to traverse.</param>
        /// <param name="parentType">The name of the parent type or entity.</param>
        private void TraverseJsonObject(JsonDocument jsonObject, string parentType)
        {
            foreach (JsonProperty property in jsonObject.RootElement.EnumerateObject())
            {
                // Skip reserved Azure Cosmos DB properties.
                if (_cosmosDbReservedProperties.Contains(property.Name))
                {
                    continue;
                }

                // Check if the parent type is not in the entity mapping.
                if (_entityAndSingularNameMapping.Count != 0 && !_entityAndSingularNameMapping.ContainsKey(parentType.Pascalize()))
                {
                    _logger?.LogWarning($"{parentType.Pascalize()} is not available, If it is unexpected, add an entity of it, in the config file.");
                    continue;
                }

                // Process each property token to determine its GraphQL type.
                this.ProcessJsonToken(property.Value, property.Name, parentType, false);
            }
        }

        /// <summary>
        /// Processes a JSON token and determines its GraphQL field type. Handles nested objects and arrays.
        /// </summary>
        /// <param name="token">The JSON token to process.</param>
        /// <param name="fieldName">The name of the field.</param>
        /// <param name="parentType">The name of the parent type.</param>
        /// <param name="isArray">Indicates whether the field is part of an array.</param>
        /// <param name="parentArrayLength">The length of the parent array if applicable.</param>
        /// <returns>The GraphQL type of the field.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the JSON token type is unsupported or if an array contains mixed types.</exception>
        private string? ProcessJsonToken(JsonElement token, string fieldName, string parentType, bool isArray, int parentArrayLength = 1)
        {
            parentType = parentType.Pascalize();

            string? gqlFieldType = "String";
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

                        if (_entityAndSingularNameMapping.Count == 0 ||
                            (_entityAndSingularNameMapping.Count != 0 && _entityAndSingularNameMapping.ContainsKey(objectTypeName)))
                        {
                            // Recursively traverse nested objects.
                            this.TraverseJsonObject(JsonDocument.Parse(token.GetRawText()), objectTypeName);
                            gqlFieldType = objectTypeName;
                        }
                        else
                        {
                            gqlFieldType = null;
                        }

                        break;
                    }
                    case JsonValueKind.Array:
                        gqlFieldType = this.ProcessJsonArray(token, fieldName, parentType.Singularize());
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
                            // Assuming string if attribute is NULL or not a date
                            gqlFieldType = "String";
                        }

                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported token.ValueKind: {token.ValueKind}");
                }
            }

            // Add or update attribute information in the entity mapping.
            if (gqlFieldType != null)
            {
                this.AddOrUpdateAttributeInfo(token, fieldName, parentType, isArray, gqlFieldType, parentArrayLength);
            }

            return gqlFieldType;
        }

        /// <summary>
        /// Processes a JSON array to determine the GraphQL field type for array elements.
        /// </summary>
        /// <param name="jsonArray">The JSON array to process.</param>
        /// <param name="fieldName">The name of the field representing the array.</param>
        /// <param name="parentType">The name of the parent type.</param>
        /// <returns>The GraphQL type of the array elements.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the array contains elements of multiple types.</exception>
        private string? ProcessJsonArray(JsonElement jsonArray, string fieldName, string parentType)
        {
            if (jsonArray.GetArrayLength() == 0)
            {
                return null;
            }

            HashSet<string?> gqlFieldType = new();
            ArrayEnumerator arrayEnumerator = jsonArray.EnumerateArray();

            // Process each element of the array to determine its GraphQL type.
            foreach (JsonElement obj in arrayEnumerator)
            {
                gqlFieldType.Add(this.ProcessJsonToken(obj, fieldName, parentType, true, arrayEnumerator.Count()));
            }

            // Check if all elements in the array are of the same type.
            if (gqlFieldType.Count is not 1)
            {
                throw new InvalidOperationException($"Same attributes {parentType} contains multiple types of elements.");
            }

            return gqlFieldType.First<string?>();
        }

        /// <summary>
        /// Adds or updates the attribute information in the entity map.
        /// </summary>
        /// <param name="token">The JSON token representing the attribute.</param>
        /// <param name="fieldName">The name of the field.</param>
        /// <param name="parentType">The name of the parent type.</param>
        /// <param name="isArray">Indicates whether the attribute is part of an array.</param>
        /// <param name="gqlFieldType">The GraphQL type of the attribute.</param>
        /// <param name="parentArrayLength">The length of the parent array if applicable.</param>
        private void AddOrUpdateAttributeInfo(JsonElement token, string fieldName, string parentType, bool isArray, string gqlFieldType, int parentArrayLength)
        {
            // Add a new attribute if it does not already exist for the entity.
            if (!_attrMapping.ContainsKey(parentType))
            {
                AttributeObject attributeObject = new(name: fieldName,
                        type: gqlFieldType,
                        isArray: isArray,
                        value: token.ValueKind,
                        arrayLength: parentArrayLength);

                _attrMapping.Add(parentType, new HashSet<AttributeObject>() { attributeObject });
            }
            else
            {
                // Update existing attribute information if the attribute already exists.
                AttributeObject? attributeObject = _attrMapping[parentType].FirstOrDefault(a => a.Name == fieldName);
                if (attributeObject is null)
                {
                    attributeObject = new(name: fieldName,
                        type: gqlFieldType,
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
