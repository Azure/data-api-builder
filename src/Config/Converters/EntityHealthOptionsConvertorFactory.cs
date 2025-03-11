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
            if (reader.TokenType == JsonTokenType.Null)
            {
                return new EntityHealthCheckConfig() { Enabled = true, First = 100, ThresholdMs = 1000 };
            }
            
            if (reader.TokenType is JsonTokenType.StartObject)
            {
                bool enabled = true;
                // Defer to EntityHealthCheckConfig record definition to define default first value.
                int? first = null;

                // Defer to EntityHealthCheckConfig record definition to define default threshold-ms value.
                int? threshold_ms = null;

                while (reader.Read())
                {
                    if (reader.TokenType is JsonTokenType.EndObject)
                    {
                        return new EntityHealthCheckConfig() { Enabled = enabled, First = first ?? 100, ThresholdMs = threshold_ms ?? 1000 };
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
                                first = 100;
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
                                threshold_ms = 1000;
                            }
                            else
                            {
                                int parseTtlSeconds = reader.GetInt32();
                                if (parseTtlSeconds <= 0)
                                {
                                    throw new JsonException($"Invalid value for ttl-seconds: {parseTtlSeconds}. Value must be greater than 0.");
                                }

                                threshold_ms = parseTtlSeconds;
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
