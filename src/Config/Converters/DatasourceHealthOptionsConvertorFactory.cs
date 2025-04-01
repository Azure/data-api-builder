// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

internal class DataSourceHealthOptionsConvertorFactory : JsonConverterFactory
{
    // Determines whether to replace environment variable with its
    // value or not while deserializing.
    private bool _replaceEnvVar;

    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(DatasourceHealthCheckConfig));
    }

    /// <inheritdoc/>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return new HealthCheckOptionsConverter(_replaceEnvVar);
    }

    /// <param name="replaceEnvVar">Whether to replace environment variable with its
    /// value or not while deserializing.</param>
    internal DataSourceHealthOptionsConvertorFactory(bool replaceEnvVar)
    {
        _replaceEnvVar = replaceEnvVar;
    }

    private class HealthCheckOptionsConverter : JsonConverter<DatasourceHealthCheckConfig>
    {
        // Determines whether to replace environment variable with its
        // value or not while deserializing.
        private bool _replaceEnvVar;

        /// <param name="replaceEnvVar">Whether to replace environment variable with its
        /// value or not while deserializing.</param>
        public HealthCheckOptionsConverter(bool replaceEnvVar)
        {
            _replaceEnvVar = replaceEnvVar;
        }

        /// <summary>
        /// Defines how DAB reads the data-source's health options and defines which values are
        /// used to instantiate DatasourceHealthCheckConfig.
        /// </summary>
        /// <exception cref="JsonException">Thrown when improperly formatted health check options are provided.</exception>
        public override DatasourceHealthCheckConfig? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType is JsonTokenType.Null)
            {
                return new DatasourceHealthCheckConfig();
            }

            if (reader.TokenType is JsonTokenType.StartObject)
            {
                bool? enabled = null;
                string? name = null;
                int? threshold_ms = null;

                while (reader.Read())
                {
                    if (reader.TokenType is JsonTokenType.EndObject)
                    {
                        return new DatasourceHealthCheckConfig(enabled, name, threshold_ms);
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
                        case "name":
                            if (reader.TokenType is not JsonTokenType.Null)
                            {
                                name = reader.DeserializeString(_replaceEnvVar);
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

            throw new JsonException("Datasource Health Options has a missing }.");
        }

        public override void Write(Utf8JsonWriter writer, DatasourceHealthCheckConfig value, JsonSerializerOptions options)
        {
            if (value?.UserProvidedEnabled is true)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("enabled");
                JsonSerializer.Serialize(writer, value.Enabled, options);
                if (value?.Name is not null)
                {
                    writer.WritePropertyName("name");
                    JsonSerializer.Serialize(writer, value.Name, options);
                }

                if (value?.UserProvidedThresholdMs is true)
                {
                    writer.WritePropertyName("threshold-ms");
                    JsonSerializer.Serialize(writer, value.ThresholdMs, options);
                }

                writer.WriteEndObject();
            }
        }
    }
}
