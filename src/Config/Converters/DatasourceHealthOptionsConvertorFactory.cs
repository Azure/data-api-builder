// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.HealthCheck;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

internal class DatasourceHealthOptionsConvertorFactory : JsonConverterFactory
{
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(DatasourceHealthCheckConfig));
    }

    /// <inheritdoc/>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return new HealthCheckOptionsConverter();
    }

    private class HealthCheckOptionsConverter : JsonConverter<DatasourceHealthCheckConfig>
    {
        /// <summary>
        /// Defines how DAB reads an entity's health options and defines which values are
        /// used to instantiate EntityHealthCheckConfig.
        /// </summary>
        /// <exception cref="JsonException">Thrown when improperly formatted health check options are provided.</exception>
        public override DatasourceHealthCheckConfig? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return new DatasourceHealthCheckConfig() { Enabled = true, Name = null, ThresholdMs = HealthCheckConstants.DefaultThresholdResponseTimeMs };
            }

            if (reader.TokenType is JsonTokenType.StartObject)
            {
                bool enabled = true;

                // Defer to DatasourceHealthCheckConfig record definition to define default name value.
                string? name = null;

                // Defer to DatasourceHealthCheckConfig record definition to define default threshold-ms value.
                int? threshold_ms = null;

                while (reader.Read())
                {
                    if (reader.TokenType is JsonTokenType.EndObject)
                    {
                        return new DatasourceHealthCheckConfig()
                        {
                            Enabled = enabled,
                            Name = name,
                            ThresholdMs = threshold_ms ?? HealthCheckConstants.DefaultThresholdResponseTimeMs
                        };
                    }

                    string? property = reader.GetString();
                    reader.Read();

                    switch (property)
                    {
                        case "enabled":
                            if (reader.TokenType is JsonTokenType.Null)
                            {
                                enabled = true; // This is true because the default value for Data source check in Comprehensive Health Endpoint is true.
                            }
                            else
                            {
                                enabled = reader.GetBoolean();
                            }

                            break;
                        case "name":
                            if (reader.TokenType is JsonTokenType.Null)
                            {
                                name = null;
                            }
                            else
                            {
                                name = reader.GetString();
                            }

                            break;
                        case "threshold-ms":
                            if (reader.TokenType is JsonTokenType.Null)
                            {
                                threshold_ms = HealthCheckConstants.DefaultThresholdResponseTimeMs; // This is the default value for threshold-ms.
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

        public override void Write(Utf8JsonWriter writer, DatasourceHealthCheckConfig value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteBoolean("enabled", value.Enabled);
            writer.WriteString("name", value.Name);
            writer.WriteNumber("threshold-ms", value.ThresholdMs);
            writer.WriteEndObject();
        }
    }
}
