// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.HealthCheck;
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
            if (reader.TokenType == JsonTokenType.Null)
            {
                return new EntityHealthCheckConfig()
                {
                    Enabled = true,
                    First = HealthCheckConstants.DEFAULT_FIRST_VALUE,
                    ThresholdMs = HealthCheckConstants.DEFAULT_THRESHOLD_RESPONSE_TIME_MS
                };
            }

            if (reader.TokenType is JsonTokenType.StartObject)
            {
                bool enabled = true;

                // Refer to EntityHealthCheckConfig record definition to define default first value.
                int? first = null;

                // Refer to EntityHealthCheckConfig record definition to define default threshold-ms value.
                int? threshold_ms = null;

                while (reader.Read())
                {
                    if (reader.TokenType is JsonTokenType.EndObject)
                    {
                        return new EntityHealthCheckConfig()
                        {
                            Enabled = enabled,
                            First = first ?? HealthCheckConstants.DEFAULT_FIRST_VALUE,
                            ThresholdMs = threshold_ms ?? HealthCheckConstants.DEFAULT_THRESHOLD_RESPONSE_TIME_MS
                        };
                    }

                    string? property = reader.GetString();
                    reader.Read();

                    switch (property)
                    {
                        case "enabled":
                            if (reader.TokenType is JsonTokenType.Null)
                            {
                                enabled = true; // This is true because the default value for entity check in Comprehensive Health End point is true.
                            }
                            else
                            {
                                enabled = reader.GetBoolean();
                            }

                            break;
                        case "first":
                            if (reader.TokenType is JsonTokenType.Null)
                            {
                                first = HealthCheckConstants.DEFAULT_FIRST_VALUE; // This is the default value for first.
                            }
                            else
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
                            if (reader.TokenType is JsonTokenType.Null)
                            {
                                threshold_ms = HealthCheckConstants.DEFAULT_THRESHOLD_RESPONSE_TIME_MS; // This is the default value for threshold-ms.
                            }
                            else
                            {
                                int parseThresholdMs = reader.GetInt32();
                                if (parseThresholdMs <= 0)
                                {
                                    throw new JsonException($"Invalid value for ttl-seconds: {parseThresholdMs}. Value must be greater than 0.");
                                }

                                threshold_ms = parseThresholdMs;
                            }

                            break;
                    }
                }
            }

            throw new JsonException();
        }

        public override void Write(Utf8JsonWriter writer, EntityHealthCheckConfig value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteBoolean("enabled", value.Enabled);
            writer.WriteNumber("first", value.First);
            writer.WriteNumber("threshold-ms", value.ThresholdMs);
            writer.WriteEndObject();
        }
    }
}
