// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

internal class EntitySourceConverterFactory : JsonConverterFactory
{
    // Settings for variable replacement during deserialization.
    private readonly DeserializationVariableReplacementSettings? _replacementSettings;

    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(EntitySource));
    }

    /// <inheritdoc/>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return new EntitySourceConverter(_replacementSettings);
    }

    /// <param name="replacementSettings">Settings for variable replacement during deserialization.
    /// If null, no variable replacement will be performed.</param>
    internal EntitySourceConverterFactory(DeserializationVariableReplacementSettings? replacementSettings = null)
    {
        _replacementSettings = replacementSettings;
    }

    private class EntitySourceConverter : JsonConverter<EntitySource>
    {
        // Settings for variable replacement during deserialization.
        private readonly DeserializationVariableReplacementSettings? _replacementSettings;

        /// <param name="replacementSettings">Settings for variable replacement during deserialization.
        /// If null, no variable replacement will be performed.</param>
        public EntitySourceConverter(DeserializationVariableReplacementSettings? replacementSettings)
        {
            _replacementSettings = replacementSettings;
        }

        public override EntitySource? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                string? obj = reader.DeserializeString(_replacementSettings);
                return new EntitySource(obj ?? string.Empty, EntitySourceType.Table, new(), Array.Empty<string>());
            }

            // Remove the converter so we don't recurse.
            JsonSerializerOptions innerOptions = new(options);
            innerOptions.Converters.Remove(innerOptions.Converters.First(c => c is EntitySourceConverterFactory));

            using JsonDocument doc = JsonDocument.ParseValue(ref reader);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("parameters", out JsonElement parametersElement) &&
                parametersElement.ValueKind == JsonValueKind.Object)
            {
                // Old format detected
                List<ParameterMetadata> paramList = [];
                foreach (JsonProperty prop in parametersElement.EnumerateObject())
                {
                    string? defaultValue = GetClrValue(prop.Value)?.ToString();
                    paramList.Add(new ParameterMetadata
                    {
                        Name = prop.Name,
                        Default = defaultValue,
                    });
                }

                // Remove "parameters" from the JSON before deserialization
                Dictionary<string, object> modObj = [];
                foreach (JsonProperty property in root.EnumerateObject())
                {
                    if (!property.NameEquals("parameters"))
                    {
                        modObj[property.Name] = property.Value.Deserialize<object>(innerOptions) ?? new object();
                    }
                }

                modObj["parameters"] = paramList;

                string modJson = JsonSerializer.Serialize(modObj, innerOptions);

                // Deserialize to EntitySource without parameters
                EntitySource? entitySource = JsonSerializer.Deserialize<EntitySource>(modJson, innerOptions)
                    ?? throw new JsonException("Failed to deserialize EntitySource from modified JSON.");

                // Use the with expression to set the correct Parameters
                return entitySource with { Parameters = paramList };
            }
            else
            {
                string rawJson = root.GetRawText();
                // If already in new format, deserialize as usual
                EntitySource? source = JsonSerializer.Deserialize<EntitySource>(rawJson, innerOptions);

                if (source?.Parameters is not null)
                {
                    if (source.Parameters is IEnumerable<ParameterMetadata> paramList)
                    {
                        return source with { Parameters = [.. paramList] };
                    }
                }

                return source;
            }
        }

        private static object GetClrValue(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString() ?? string.Empty,
                JsonValueKind.Number => GetNumberValue(element),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => element.ToString()
            };
        }

        /// <summary>
        /// Attempts to get the correct numeric value from the <see cref="JsonElement"/>.
        /// If all possible numeric values are exhausted, the raw text is returned.
        /// </summary>
        /// <param name="element">JSON element to extract the value from.</param>
        /// <returns>The parsed value as a CLR type.</returns>
        private static object GetNumberValue(JsonElement element)
        {
            if (element.TryGetInt32(out int intValue))
            {
                return intValue;
            }

            if (element.TryGetDecimal(out decimal decimalValue))
            {
                return decimalValue;
            }

            if (element.TryGetDouble(out double doubleValue))
            {
                return doubleValue;
            }

            if (element.TryGetInt64(out long longValue))
            {
                return longValue;
            }

            if (element.TryGetUInt32(out uint uintValue))
            {
                return uintValue;
            }

            if (element.TryGetUInt64(out ulong ulongValue))
            {
                return ulongValue;
            }

            if (element.TryGetSingle(out float floatValue))
            {
                return floatValue;
            }

            return element.GetRawText();
        }

        public override void Write(Utf8JsonWriter writer, EntitySource value, JsonSerializerOptions options)
        {
            // Remove the converter so we don't recurse.
            JsonSerializerOptions innerOptions = new(options);
            innerOptions.Converters.Remove(innerOptions.Converters.First(c => c is EntitySourceConverterFactory));

            JsonSerializer.Serialize(writer, value, innerOptions);
        }
    }
}
