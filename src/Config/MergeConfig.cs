// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Azure.DataApiBuilder.Config
{
    /// <summary>
    /// This Class contains methods to merge config files based on DAB_ENVIRONMENT value
    /// to give a similar experience to that of dotnet development.
    /// </summary>
    public class MergeConfig
    {
        /// <summary>
        /// Parse the string and call the helper methods to merge based on their value kind, i.e. Objects or Array.
        /// </summary>
        public static string Merge(string originalJson, string overridingJson)
        {
            ArrayBufferWriter<byte> outputBuffer = new();

            using (JsonDocument originalJsonDoc = JsonDocument.Parse(originalJson))
            using (JsonDocument overridingJsonDoc = JsonDocument.Parse(overridingJson))
            using (Utf8JsonWriter jsonWriter = new(outputBuffer, new JsonWriterOptions { Indented = true }))
            {
                JsonElement root1 = originalJsonDoc.RootElement;
                JsonElement root2 = overridingJsonDoc.RootElement;

                if (root1.ValueKind != JsonValueKind.Array && root1.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidOperationException($"The original JSON document to merge new content into must be a container type. Instead it is {root1.ValueKind}.");
                }

                if (root1.ValueKind != root2.ValueKind)
                {
                    return originalJson;
                }

                if (root1.ValueKind == JsonValueKind.Array)
                {
                    MergeArrays(jsonWriter, root2);
                }
                else
                {
                    MergeObjects(jsonWriter, root1, root2);
                }
            }

            return Encoding.UTF8.GetString(outputBuffer.WrittenSpan);
        }

        /// <summary>
        /// This helper methods tries to merge two Json Object recursively.
        /// </summary>
        private static void MergeObjects(Utf8JsonWriter jsonWriter, JsonElement root1, JsonElement root2)
        {
            jsonWriter.WriteStartObject();

            // If a property exists in both documents, either:
            // * Merge them, if the value kinds match (e.g. both are objects or arrays) recursively,
            // * Completely override the value of the first with the one from the second, if the value kind mismatches (e.g. one is object, while the other is an array or string),
            // * Or favor the value of the first, if the second one is null.
            foreach (JsonProperty property in root1.EnumerateObject())
            {
                string propertyName = property.Name;

                JsonValueKind newValueKind;

                if (root2.TryGetProperty(propertyName, out JsonElement newValue) && (newValueKind = newValue.ValueKind) != JsonValueKind.Null)
                {
                    jsonWriter.WritePropertyName(propertyName);

                    JsonElement originalValue = property.Value;
                    JsonValueKind originalValueKind = originalValue.ValueKind;

                    if (newValueKind == JsonValueKind.Object && originalValueKind == JsonValueKind.Object)
                    {
                        MergeObjects(jsonWriter, originalValue, newValue); // Recursive call
                    }
                    else if (newValueKind == JsonValueKind.Array && originalValueKind == JsonValueKind.Array)
                    {
                        MergeArrays(jsonWriter, newValue);
                    }
                    else
                    {
                        newValue.WriteTo(jsonWriter);
                    }
                }
                else
                {
                    property.WriteTo(jsonWriter);
                }
            }

            // Write all the properties of the second document that are unique to it.
            foreach (JsonProperty property in root2.EnumerateObject())
            {
                if (!root1.TryGetProperty(property.Name, out _))
                {
                    property.WriteTo(jsonWriter);
                }
            }

            jsonWriter.WriteEndObject();
        }

        /// <summary>
        /// This helper methods tries to merge two Json Array such that the values of second array
        /// completely overrides that of the first.
        /// </summary>
        private static void MergeArrays(Utf8JsonWriter jsonWriter, JsonElement root2)
        {
            jsonWriter.WriteStartArray();

            // elements of second array completely overrides the first one.
            // the merged config will only contain elements only from second array when merging Json Arrays.
            foreach (JsonElement element in root2.EnumerateArray())
            {
                element.WriteTo(jsonWriter);
            }

            jsonWriter.WriteEndArray();
        }
    }
}
