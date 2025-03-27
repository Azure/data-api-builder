// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

internal class EntityHealthOptionsConvertorFactory : JsonConverterFactory
{
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(EntityHealthCheckConfig));
    }

    /// <inheritdoc/>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return new HealthCheckOptionsConverter();
    }

    private class HealthCheckOptionsConverter : JsonConverter<EntityHealthCheckConfig>
    {
        /// <summary>
        /// Defines how DAB reads an entity's health options and defines which values are
        /// used to instantiate EntityHealthCheckConfig.
        /// </summary>
        /// <exception cref="JsonException">Thrown when improperly formatted health check options are provided.</exception>
        public override EntityHealthCheckConfig? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType is JsonTokenType.Null)
            {
                return new EntityHealthCheckConfig();
            }

            if (reader.TokenType is JsonTokenType.StartObject)
            {
                bool? enabled = null;
                int? first = null;
                int? threshold_ms = null;

                while (reader.Read())
                {
                    if (reader.TokenType is JsonTokenType.EndObject)
                    {
                        return new EntityHealthCheckConfig(enabled, first, threshold_ms);
                    }

                    string? property = reader.GetString();
                    reader.Read();

                    switch (property)
                    {
                        case "enabled":
                            if (reader.TokenType is not JsonTokenType.Null)
                            {
                                enabled = reader.GetBoolean();
                            }

                            break;
                        case "first":
                            if (reader.TokenType is not JsonTokenType.Null)
                            {
                                int parseFirstValue = reader.GetInt32();
                                if (parseFirstValue <= 0)
                                {
                                    throw new JsonException($"Invalid value for first: {parseFirstValue}. Value must be greater than 0.");
                                }

                                first = parseFirstValue;
                            }

                            break;
                        case "threshold-ms":
                            if (reader.TokenType is not JsonTokenType.Null)
                            {
                                int parseThresholdMs = reader.GetInt32();
                                if (parseThresholdMs <= 0)
                                {
                                    throw new JsonException($"Invalid value for ttl-seconds: {parseThresholdMs}. Value must be greater than 0.");
                                }

                                threshold_ms = parseThresholdMs;
                            }

                            break;

                        default:
                            throw new JsonException($"Unexpected property {property}");
                    }
                }
            }

            throw new JsonException("Entity Health Options has a missing }.");
        }

        public override void Write(Utf8JsonWriter writer, EntityHealthCheckConfig value, JsonSerializerOptions options)
        {
            if (value?.UserProvidedEnabled is true)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("enabled");
                JsonSerializer.Serialize(writer, value.Enabled, options);
                if (value.UserProvidedFirst is true)
                {
                    writer.WritePropertyName("first");
                    JsonSerializer.Serialize(writer, value.First, options);
                }

                if (value.UserProvidedThresholdMs is true)
                {
                    writer.WritePropertyName("threshold-ms");
                    JsonSerializer.Serialize(writer, value.ThresholdMs, options);
                }

                writer.WriteEndObject();
            }
        }
    }
}
