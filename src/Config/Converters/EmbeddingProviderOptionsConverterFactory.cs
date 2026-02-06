// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

/// <summary>
/// Defines how DAB reads and writes the embedding provider options (JSON).
/// </summary>
internal class EmbeddingProviderOptionsConverterFactory : JsonConverterFactory
{
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(EmbeddingProviderOptions));
    }

    /// <inheritdoc/>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return new EmbeddingProviderOptionsConverter();
    }

    private class EmbeddingProviderOptionsConverter : JsonConverter<EmbeddingProviderOptions>
    {
        public override EmbeddingProviderOptions? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // Remove the converter so we don't recurse.
            JsonSerializerOptions jsonSerializerOptions = new(options);
            jsonSerializerOptions.Converters.Remove(jsonSerializerOptions.Converters.First(c => c is EmbeddingProviderOptionsConverterFactory));

            return JsonSerializer.Deserialize<EmbeddingProviderOptions>(ref reader, jsonSerializerOptions);
        }

        public override void Write(Utf8JsonWriter writer, EmbeddingProviderOptions value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            // Only write properties that were user-provided
            if (value.UserProvidedType)
            {
                writer.WritePropertyName("type");
                JsonSerializer.Serialize(writer, value.Type, options);
            }

            if (value.UserProvidedEndpoint)
            {
                writer.WritePropertyName("endpoint");
                JsonSerializer.Serialize(writer, value.Endpoint, options);
            }

            if (value.UserProvidedApiKey)
            {
                writer.WritePropertyName("api-key");
                JsonSerializer.Serialize(writer, value.ApiKey, options);
            }

            if (value.UserProvidedModel)
            {
                writer.WritePropertyName("model");
                JsonSerializer.Serialize(writer, value.Model, options);
            }

            writer.WriteEndObject();
        }
    }
}
