// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

/// <summary>
/// Defines how DAB reads and writes the Azure Managed Redis options (JSON).
/// </summary>
internal class AzureManagedRedisOptionsConverterFactory : JsonConverterFactory
{
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(AzureManagedRedisOptions));
    }

    /// <inheritdoc/>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return new AzureManagedRedisOptionsConverter();
    }

    private class AzureManagedRedisOptionsConverter : JsonConverter<AzureManagedRedisOptions>
    {
        public override AzureManagedRedisOptions? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // Remove the converter so we don't recurse.
            JsonSerializerOptions jsonSerializerOptions = new(options);
            jsonSerializerOptions.Converters.Remove(jsonSerializerOptions.Converters.First(c => c is AzureManagedRedisOptionsConverterFactory));

            return JsonSerializer.Deserialize<AzureManagedRedisOptions>(ref reader, jsonSerializerOptions);
        }

        public override void Write(Utf8JsonWriter writer, AzureManagedRedisOptions value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            // Only write properties that were user-provided
            if (value.UserProvidedConnectionString)
            {
                writer.WritePropertyName("connection-string");
                JsonSerializer.Serialize(writer, value.ConnectionString, options);
            }

            if (value.UserProvidedVectorIndex)
            {
                writer.WritePropertyName("vector-index");
                JsonSerializer.Serialize(writer, value.VectorIndex, options);
            }

            if (value.UserProvidedKeyPrefix)
            {
                writer.WritePropertyName("key-prefix");
                JsonSerializer.Serialize(writer, value.KeyPrefix, options);
            }

            writer.WriteEndObject();
        }
    }
}
