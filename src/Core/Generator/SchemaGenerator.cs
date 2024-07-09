// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Azure.DataApiBuilder.Core.Generator
{
    internal class SchemaGenerator
    {
        private Dictionary<string, string> _gqlTypes = new ();
        private HashSet<string> _processedTypes = new ();

        public static string Run(JArray jsonArray, string containerName)
        {
            if (jsonArray == null || jsonArray.Count == 0)
            {
                throw new InvalidOperationException("JArray must contain at least one JSON object.");
            }

            return new SchemaGenerator().ConvertJsonToGQLSchema(jsonArray: jsonArray,
                containerName: containerName);
        }

        private string ConvertJsonToGQLSchema(JArray jsonArray, string containerName)
        {
            var gqlFields = new Dictionary<string, string>();

            foreach (var token in jsonArray)
            {
                if (token is JObject jsonObject)
                {
                    MergeJsonObject(jsonObject, gqlFields, ToPascalCase(containerName));
                }
                else
                {
                    throw new InvalidOperationException("JArray must contain JSON objects only.");
                }
            }

            GenerateSchema(gqlFields, ToPascalCase(containerName), true);

            var sb = new StringBuilder();
            foreach (var gqlType in _gqlTypes)
            {
                sb.AppendLine(gqlType.Value);
                sb.AppendLine();
            }

            return sb.ToString().Trim();
        }

        private void MergeJsonObject(JObject jsonObject, Dictionary<string, string> gqlFields, string parentType)
        {
            foreach (var property in jsonObject.Properties())
            {
                var gqlField = ProcessJsonToken(property.Value, property.Name, parentType);
                gqlFields[property.Name] = gqlField;
            }
        }

        private string ProcessJsonToken(JToken token, string fieldName, string parentType)
        {
            if (fieldName == "id")
            {
                return $"{fieldName}: ID";
            }
               
            switch (token.Type)
            {
                case JTokenType.Object:
                    var objectTypeName = ToPascalCase(fieldName);
                    if (!_processedTypes.Contains(objectTypeName))
                    {
                        _processedTypes.Add(objectTypeName);
                        var objectFields = new Dictionary<string, string>();
                        MergeJsonObject((JObject)token, objectFields, objectTypeName);
                        GenerateSchema(objectFields, objectTypeName);
                    }
                    return $"{fieldName}: {objectTypeName}";

                case JTokenType.Array:
                    return ProcessJsonArray((JArray)token, fieldName, parentType);

                case JTokenType.String:
                    return $"{fieldName}: String";

                case JTokenType.Integer:
                    return $"{fieldName}: Int";

                case JTokenType.Float:
                    return $"{fieldName}: Float";

                case JTokenType.Boolean:
                    return $"{fieldName}: Boolean";

                case JTokenType.Date:
                    // Example: Representing date as ISO 8601 string
                    return $"{fieldName}: String";

                case JTokenType.Null:
                    return $"{fieldName}: String"; // Could be any type, using String as default

                default:
                    throw new InvalidOperationException($"Unsupported JTokenType: {token.Type}");
            }
        }

        private string ProcessJsonArray(JArray jsonArray, string fieldName, string parentType)
        {
            if (jsonArray.Count == 0)
            {
                return $"{fieldName}: [String]"; // Default to array of Strings if empty
            }

            var itemType = DetermineArrayItemType(jsonArray, fieldName, parentType);
            return $"{fieldName}: [{itemType}]";
        }

        private string DetermineArrayItemType(JArray jsonArray, string fieldName, string parentType)
        {
            var firstElement = jsonArray[0];

            switch (firstElement.Type)
            {
                case JTokenType.Object:
                    var objectTypeName = ToPascalCase(fieldName);
                    if (!_processedTypes.Contains(objectTypeName))
                    {
                        _processedTypes.Add(objectTypeName);
                        var objectFields = new Dictionary<string, string>();
                        MergeJsonObject((JObject)firstElement, objectFields, objectTypeName);
                        GenerateSchema(objectFields, objectTypeName);
                    }
                    return objectTypeName;

                case JTokenType.Array:
                    return $"[{DetermineArrayItemType((JArray)firstElement, fieldName, parentType)}]";

                case JTokenType.String:
                    return "String";

                case JTokenType.Integer:
                    return "Int";

                case JTokenType.Float:
                    return "Float";

                case JTokenType.Boolean:
                    return "Boolean";

                case JTokenType.Date:
                    // Example: Representing date as ISO 8601 string
                    return "String";

                case JTokenType.Null:
                    return "String"; // Default to String if null

                default:
                    throw new InvalidOperationException($"Unsupported JTokenType: {firstElement.Type}");
            }
        }

        private void GenerateSchema(Dictionary<string, string> gqlFields, string typeName, bool isRoot = false)
        {
            var sb = new StringBuilder();
            if (isRoot)
            {
                sb.AppendLine($"type {typeName} @model {{"); // Add @model annotation
            }
            else
            {
                sb.AppendLine($"type {typeName} {{"); // Add @model annotation
            }
           
            foreach (var field in gqlFields.Values)
            {
                sb.AppendLine($"  {field}");
            }
            sb.AppendLine("}");

            _gqlTypes[typeName] = sb.ToString();
        }

        private static string ToPascalCase(string str)
        {
            return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(str).Replace("_", string.Empty).Replace("-", string.Empty);
        }


    }
}
