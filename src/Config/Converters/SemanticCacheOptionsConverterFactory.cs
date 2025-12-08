// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

/// <summary>
/// Defines how DAB reads and writes the semantic cache options (JSON).
/// </summary>
internal class SemanticCacheOptionsConverterFactory : JsonConverterFactory
{
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(SemanticCacheOptions));
    }

    /// <inheritdoc/>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return new SemanticCacheOptionsConverter();
    }

    private class SemanticCacheOptionsConverter : JsonConverter<SemanticCacheOptions>
    {
        /// <summary>
        /// Defines how DAB reads the semantic cache options and defines which values are
        /// used to instantiate SemanticCacheOptions.
        /// </summary>
        /// <exception cref="JsonException">Thrown when improperly formatted semantic cache options are provided.</exception>
        public override SemanticCacheOptions? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // Remove the converter so we don't recurse.
            JsonSerializerOptions jsonSerializerOptions = new(options);
            jsonSerializerOptions.Converters.Remove(jsonSerializerOptions.Converters.First(c => c is SemanticCacheOptionsConverterFactory));

            SemanticCacheOptions? res = JsonSerializer.Deserialize<SemanticCacheOptions>(ref reader, jsonSerializerOptions);

            if (res is not null)
            {
                // Validate similarity threshold
                if (res.SimilarityThreshold.HasValue && (res.SimilarityThreshold < 0.0 || res.SimilarityThreshold > 1.0))
                {
                    throw new JsonException($"Invalid value for similarity-threshold: {res.SimilarityThreshold}. Value must be between 0.0 and 1.0.");
                }

                // Validate max results
                if (res.MaxResults.HasValue && res.MaxResults <= 0)
                {
                    throw new JsonException($"Invalid value for max-results: {res.MaxResults}. Value must be greater than 0.");
                }

                // Validate expire seconds
                if (res.ExpireSeconds.HasValue && res.ExpireSeconds <= 0)
                {
                    throw new JsonException($"Invalid value for expire-seconds: {res.ExpireSeconds}. Value must be greater than 0.");
                }
            }

            return res;
        }

        /// <summary>
        /// When writing the SemanticCacheOptions back to a JSON file, only write properties
        /// that were explicitly provided by the user. This avoids polluting the written JSON
        /// file with default values.
        /// This Write operation is only used when a RuntimeConfig object is serialized to JSON.
        /// </summary>
        public override void Write(Utf8JsonWriter writer, SemanticCacheOptions value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            
            // Always write enabled
            writer.WriteBoolean("enabled", value?.Enabled ?? false);

            if (value is not null)
            {
                // Only write similarity-threshold if user provided it
                if (value.UserProvidedSimilarityThreshold)
                {
                    writer.WritePropertyName("similarity-threshold");
                    JsonSerializer.Serialize(writer, value.SimilarityThreshold, options);
                }

                // Only write max-results if user provided it
                if (value.UserProvidedMaxResults)
                {
                    writer.WritePropertyName("max-results");
                    JsonSerializer.Serialize(writer, value.MaxResults, options);
                }

                // Only write expire-seconds if user provided it
                if (value.UserProvidedExpireSeconds)
                {
                    writer.WritePropertyName("expire-seconds");
                    JsonSerializer.Serialize(writer, value.ExpireSeconds, options);
                }

                // Write nested objects if present
                if (value.AzureManagedRedis is not null)
                {
                    writer.WritePropertyName("azure-managed-redis");
                    JsonSerializer.Serialize(writer, value.AzureManagedRedis, options);
                }

                if (value.EmbeddingProvider is not null)
                {
                    writer.WritePropertyName("embedding-provider");
                    JsonSerializer.Serialize(writer, value.EmbeddingProvider, options);
                }
            }

            writer.WriteEndObject();
        }
    }
}
