// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Azure.DataApiBuilder.Core.Services.MetadataProviders.Generator
{
    public class GraphQLSchemaGenerate
    {
        public static string GenerateGraphQLSchema(JToken item, string containerName)
        {
            Dictionary<string, JObject> types = new();

            // Traverse the JSON object to generate types
            Traverse(item, types, parentName: containerName);

            // Assemble GraphQL schema
            StringBuilder schemaBuilder = new ();
            foreach (KeyValuePair<string, JObject> type in types)
            {
                if (type.Key == containerName)
                {
                    schemaBuilder.AppendLine($"type @model {type.Key} {{");
                }
                else
                {
                    schemaBuilder.AppendLine($"type {type.Key} {{");
                }

                foreach (JProperty property in type.Value.Properties())
                {
                    schemaBuilder.AppendLine($"    {property.Name}: {property.Value}");
                }

                schemaBuilder.AppendLine("}");
            }

            return schemaBuilder.ToString();
        }

        public static void Traverse(JToken token, Dictionary<string, JObject>? types, string parentName)
        {
            if (types == null)
            {
                types = new Dictionary<string, JObject>();
            }

            parentName = ToUpperFirstLetter(ToCamelCase(parentName));

            if (token.Type == JTokenType.Object)
            {
                JObject obj = (JObject)token;
                if (!types.ContainsKey(parentName))
                {
                    types[parentName] = new JObject();
                }

                foreach (JProperty? property in obj.Properties())
                {
                    string fieldType = GetGraphQLType(property.Value);
                    types[parentName][property.Name] = fieldType;

                    if (property.Value.Type == JTokenType.Object)
                    {
                        Traverse(property.Value, types, property.Name);
                    }
                    else if (property.Value.Type == JTokenType.Array && property.Value.HasValues)
                    {
                        JTokenType? firstElementType = property?.Value?.First?.Type;
                        if (firstElementType == JTokenType.Object)
                        {
                            if(property == null || property.Value == null || property.Value.First == null)
                            {
                                throw new ArgumentNullException("Property value is null");
                            }

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

        public static string GetGraphQLType(JToken token)
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
                    return ToUpperFirstLetter(ToCamelCase(Regex.Replace(key, @"\[\d+\]$", ""))); // Extract object name from path
                case JTokenType.Array:
                    string arrayType = GetGraphQLType(((JArray)token).First());
                    string sanitizedValue = arrayType.EndsWith("s") ? arrayType.Remove(arrayType.Length - 1) : arrayType;
                    return $"[{sanitizedValue}]";
                default:
                    return "String"; // Default to string
            }
        }

        public static string ToUpperFirstLetter(string source)
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

        public static string ToCamelCase(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                return string.Empty;
            }

            char newFirstLetter = char.ToLowerInvariant(s[0]);
            if(newFirstLetter == s[0])
            {
                return s;
            }

            return s.Length <= 256
                ? FastChangeFirstLetter(newFirstLetter, s)
                : newFirstLetter + s.Substring(1);
        }

        private static string FastChangeFirstLetter(char newFirstLetter, string s)
        {
            Span<char> buffer = stackalloc char[s.Length];
            buffer[0] = newFirstLetter;
            s.AsSpan().Slice(1).CopyTo(buffer.Slice(1));
            return buffer.ToString();
        }
    }
}
