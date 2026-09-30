// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel.Embeddings;

namespace Azure.DataApiBuilder.Config.Converters;

/// <summary>
/// Custom JSON converter factory for EmbeddingsCacheOptions.
/// </summary>
internal class EmbeddingsCacheOptionsConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert == typeof(EmbeddingsCacheOptions);
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return new EmbeddingsCacheOptionsConverter();
    }

    private class EmbeddingsCacheOptionsConverter : JsonConverter<EmbeddingsCacheOptions>
    {
        public override EmbeddingsCacheOptions Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            bool? enabled = null;
            int? ttlHours = null;
            EmbeddingsCacheLevel2Options? level2 = null;

            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("Expected StartObject token");
            }

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return new EmbeddingsCacheOptions(
                        Enabled: enabled,
                        TtlHours: ttlHours,
                        Level2: level2);
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException("Expected PropertyName token");
                }

                string? propertyName = reader.GetString();
                reader.Read();

                switch (propertyName)
                {
                    case "enabled":
                        enabled = reader.GetBoolean();
                        break;
                    case "ttl-hours":
                        ttlHours = reader.GetInt32();
                        break;
                    case "level-2":
                        level2 = JsonSerializer.Deserialize<EmbeddingsCacheLevel2Options>(ref reader, options);
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            throw new JsonException("Expected EndObject token");
        }

        public override void Write(Utf8JsonWriter writer, EmbeddingsCacheOptions value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            if (value.Enabled is not null)
            {
                writer.WriteBoolean("enabled", value.Enabled.Value);
            }

            if (value.UserProvidedTtlHours && value.TtlHours is not null)
            {
                writer.WriteNumber("ttl-hours", value.TtlHours.Value);
            }

            if (value.Level2 is not null)
            {
                writer.WritePropertyName("level-2");
                JsonSerializer.Serialize(writer, value.Level2, options);
            }

            writer.WriteEndObject();
        }
    }
}
