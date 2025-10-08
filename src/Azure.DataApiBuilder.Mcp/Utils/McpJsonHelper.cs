// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;

namespace Azure.DataApiBuilder.Mcp.Utils
{
    /// <summary>
    /// Helper methods for JSON operations in MCP tools.
    /// </summary>
    public static class McpJsonHelper
    {
        /// <summary>
        /// Converts JsonElement to .NET object dynamically.
        /// </summary>
        public static object? GetJsonValue(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.TryGetInt64(out long l) ? l : element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => element.GetRawText() // fallback for arrays/objects
            };
        }

        /// <summary>
        /// Extracts values from a JSON value array typically returned by DAB engine.
        /// </summary>
        public static Dictionary<string, object?> ExtractValuesFromEngineResult(JsonElement engineRootElement)
        {
            Dictionary<string, object?> resultData = new();

            // Navigate to "value" array in the engine result
            if (engineRootElement.TryGetProperty("value", out JsonElement valueArray) &&
                valueArray.ValueKind == JsonValueKind.Array &&
                valueArray.GetArrayLength() > 0)
            {
                JsonElement firstItem = valueArray[0];

                // Include all properties from the result
                foreach (JsonProperty prop in firstItem.EnumerateObject())
                {
                    resultData[prop.Name] = GetJsonValue(prop.Value);
                }
            }

            return resultData;
        }

        /// <summary>
        /// Creates a formatted key details string from a dictionary of key-value pairs.
        /// </summary>
        public static string FormatKeyDetails(Dictionary<string, object?> keys)
        {
            return string.Join(", ", keys.Select(k => $"{k.Key}={k.Value}"));
        }
    }
}
