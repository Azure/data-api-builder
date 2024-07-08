// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Newtonsoft.Json.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Azure.DataApiBuilder.Core.Generator
{
    internal class SchemaGenerator
    {
        public static string Run(JArray jsonArray)
        {
            return GenerateGraphQLSchema(item: jsonArray,
                containerName: "Planet");
        }

        static string GenerateGraphQLSchema(JToken item, string containerName)
        {
            var types = new Dictionary<string, JObject>();
            if (item.Type == JTokenType.Array)
            {
                int i = 0;
                do
                {
                    JToken? element = item[i];
                    if (element is null)
                    {
                        break;
                    }
                    // Traverse the JSON object to generate types
                    Traverse(element, types, parentName: containerName);
                    i++;
                } while (i < item.Count());

            }
            else if (item.Type == JTokenType.Object)
            {
                // Traverse the JSON object to generate types
                Traverse(item, types, parentName: containerName);
            }

            // Assemble GraphQL schema
            var schemaBuilder = new StringBuilder();
            foreach (var type in types)
            {
                if (type.Key == containerName)
                {
                    schemaBuilder.AppendLine($"type @model {type.Key} {{");
                }
                else
                {
                    schemaBuilder.AppendLine($"type {type.Key} {{");
                }

                foreach (var property in type.Value.Properties())
                {
                    schemaBuilder.AppendLine($"    {property.Name}: {property.Value}");
                }

                schemaBuilder.AppendLine("}");
            }

            return schemaBuilder.ToString();
        }

        static void Traverse(JToken token, Dictionary<string, JObject> types, string parentName)
        {
            parentName = ToUpperFirstLetter(parentName/*.ToCamelCase()*/);

            if (token.Type == JTokenType.Object)
            {
                var obj = (JObject)token;
                if (!types.ContainsKey(parentName))
                {
                    types[parentName] = new JObject();
                }

                foreach (var property in obj.Properties())
                {
                    if (property == null || property.Value == null)
                    {
                        continue;
                    }

                    string fieldType = GetGraphQLType(property.Value);
                    types[parentName][property.Name] = fieldType;

                    if (property.Value.Type == JTokenType.Object)
                    {
                        Traverse(property.Value, types, property.Name);
                    }
                    else if (property.Value.First != null && property.Value.Type == JTokenType.Array && property.Value.HasValues)
                    {
                        var firstElementType = property.Value.First.Type;
                        if (firstElementType == JTokenType.Object)
                        {
                            Traverse(property.Value.First, types, property.Name);
                        }
                        else if (firstElementType == JTokenType.Array)
                        {
                            throw new NotSupportedException("Nested arrays are not supported in GraphQL schema generation.");
                        }
                    }
                }
            }
        }

        static string GetGraphQLType(JToken token)
        {
            string key = token.Path.Split('.').Last();
            switch (token.Type)
            {
                case JTokenType.String:
                    if (key == "id")
                    {
                        return "ID";
                    }
                    return "String";
                case JTokenType.Integer:
                    return "Int";
                case JTokenType.Float:
                    return "Float";
                case JTokenType.Boolean:
                    return "Boolean";
                case JTokenType.Null:
                    return "String"; // Treat null as string for simplicity
                case JTokenType.Object:
                    return ToUpperFirstLetter(Regex.Replace(key, @"\[\d+\]$", "")/*.ToCamelCase()*/); // Extract object name from path
                case JTokenType.Array:
                    var arrayType = GetGraphQLType(((JArray)token).First());
                    var sanitizedValue = arrayType.EndsWith("s") ? arrayType.Remove(arrayType.Length - 1) : arrayType;
                    return $"[{sanitizedValue}]";
                default:
                    return "String"; // Default to string
            }
        }

        static string ToUpperFirstLetter(string source)
        {
            if (string.IsNullOrEmpty(source))
            {
                return string.Empty;
            }
            // convert to char array of the string
            char[] letters = source.ToCharArray();
            // upper case the first char
            letters[0] = char.ToUpper(letters[0]);
            // return the array made of the new char array
            return new string(letters);
        }

    }
}
