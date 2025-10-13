// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

internal class EntitySourceConverterFactory : JsonConverterFactory
{
    // Determines whether to replace environment variable with its
    // value or not while deserializing.
    private bool _replaceEnvVar;

    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(EntitySource));
    }

    /// <inheritdoc/>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return new EntitySourceConverter(_replaceEnvVar);
    }

    /// <param name="replaceEnvVar">Whether to replace environment variable with its
    /// value or not while deserializing.</param>
    internal EntitySourceConverterFactory(bool replaceEnvVar)
    {
        _replaceEnvVar = replaceEnvVar;
    }

    private class EntitySourceConverter : JsonConverter<EntitySource>
    {
        // Determines whether to replace environment variable with its
        // value or not while deserializing.
        private bool _replaceEnvVar;

        /// <param name="replaceEnvVar">Whether to replace environment variable with its
        /// value or not while deserializing.</param>
        public EntitySourceConverter(bool replaceEnvVar)
        {
            _replaceEnvVar = replaceEnvVar;
        }

        public override EntitySource? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                string? obj = reader.DeserializeString(_replaceEnvVar);
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
                    object? value = prop.Value.Deserialize<object>(innerOptions);
                    string? defaultValue = value?.ToString();
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

        public override void Write(Utf8JsonWriter writer, EntitySource value, JsonSerializerOptions options)
        {
            // Remove the converter so we don't recurse.
            JsonSerializerOptions innerOptions = new(options);
            innerOptions.Converters.Remove(innerOptions.Converters.First(c => c is EntitySourceConverterFactory));

            JsonSerializer.Serialize(writer, value, innerOptions);
        }
    }
}
