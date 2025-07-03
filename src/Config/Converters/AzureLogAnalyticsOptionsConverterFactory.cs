// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

/// <summary>
/// Defines how DAB reads and writes Azure Log Analytics options.
/// </summary>
internal class AzureLogAnalyticsOptionsConverterFactory : JsonConverterFactory
{
    // Determines whether to replace environment variable with its
    // value or not while deserializing.
    private bool _replaceEnvVar;

    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(AzureLogAnalyticsOptions));
    }

    /// <inheritdoc/>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return new AzureLogAnalyticsOptionsConverter(_replaceEnvVar);
    }

    /// <param name="replaceEnvVar">Whether to replace environment variable with its
    /// value or not while deserializing.</param>
    internal AzureLogAnalyticsOptionsConverterFactory(bool replaceEnvVar)
    {
        _replaceEnvVar = replaceEnvVar;
    }

    private class AzureLogAnalyticsOptionsConverter : JsonConverter<AzureLogAnalyticsOptions>
    {
        // Determines whether to replace environment variable with its
        // value or not while deserializing.
        private bool _replaceEnvVar;

        /// <param name="replaceEnvVar">Whether to replace environment variable with its
        /// value or not while deserializing.</param>
        internal AzureLogAnalyticsOptionsConverter(bool replaceEnvVar)
        {
            _replaceEnvVar = replaceEnvVar;
        }

        /// <summary>
        /// Defines how DAB reads Azure Log Analytics options and defines which values are
        /// used to instantiate AzureLogAnalyticsOptions.
        /// Uses default deserialize.
        /// </summary>
        /// <exception cref="JsonException">Thrown when improperly formatted Azure Log Analytics options are provided.</exception>
        public override AzureLogAnalyticsOptions? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType is JsonTokenType.StartObject)
            {
                AzureLogAnalyticsOptions azureLogAnalyticsOptions = new();
                AzureLogAnalyticsAuthOptionsConverter authOptionsConverter = new(_replaceEnvVar);

                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                    {
                        break;
                    }

                    string? propertyName = reader.GetString();

                    reader.Read();
                    switch (propertyName)
                    {
                        case "enabled":
                            if (reader.TokenType is JsonTokenType.True || reader.TokenType is JsonTokenType.False)
                            {
                                azureLogAnalyticsOptions = azureLogAnalyticsOptions with { Enabled = reader.GetBoolean() };
                            }
                            else
                            {
                                throw new JsonException($"Unsupported value entered for the property 'enabled': {reader.TokenType}");
                            }

                            break;

                        case "auth":
                            azureLogAnalyticsOptions = azureLogAnalyticsOptions with { Auth = authOptionsConverter.Read(ref reader, typeToConvert, options) };
                            break;

                        case "log-type":
                            if (reader.TokenType is JsonTokenType.String)
                            {
                                string? logType = reader.DeserializeString(_replaceEnvVar);
                                if (logType is null)
                                {
                                    logType = "DabLogs";
                                }

                                azureLogAnalyticsOptions = azureLogAnalyticsOptions with { LogType = logType };
                            }
                            else
                            {
                                throw new JsonException($"Unexpected type of value entered for log-type: {reader.TokenType}");
                            }

                            break;

                        case "flush-interval-seconds":
                            if (reader.TokenType is JsonTokenType.Number)
                            {
                                int flushIntSec;
                                try
                                {
                                    flushIntSec = reader.GetInt32();
                                }
                                catch (FormatException)
                                {
                                    throw new JsonException($"The JSON token value is of the incorrect numeric format.");
                                }

                                if (flushIntSec <= 0)
                                {
                                    throw new JsonException($"Invalid flush-interval-seconds: {flushIntSec}. Specify a number > 0.");
                                }

                                azureLogAnalyticsOptions = azureLogAnalyticsOptions with { FlushIntervalSeconds = flushIntSec };
                            }
                            else
                            {
                                throw new JsonException($"Unsupported value entered for flush-interval-seconds: {reader.TokenType}");
                            }

                            break;

                        default:
                            throw new JsonException($"Unexpected property {propertyName}");
                    }
                }

                return azureLogAnalyticsOptions;
            }

            throw new JsonException("Failed to read the Azure Log Analytics Options");
        }

        /// <summary>
        /// When writing the AzureLogAnalyticsOptions back to a JSON file, only write the properties
        /// if they are user provided. This avoids polluting the written JSON file with properties
        /// the user most likely omitted when writing the original DAB runtime config file.
        /// This Write operation is only used when a RuntimeConfig object is serialized to JSON.
        /// </summary>
        public override void Write(Utf8JsonWriter writer, AzureLogAnalyticsOptions value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            if (value?.UserProvidedEnabled is true)
            {
                writer.WritePropertyName("enabled");
                JsonSerializer.Serialize(writer, value.Enabled, options);
            }

            if (value?.Auth is not null)
            {
                AzureLogAnalyticsAuthOptionsConverter authOptionsConverter = options.GetConverter(typeof(AzureLogAnalyticsAuthOptions)) as AzureLogAnalyticsAuthOptionsConverter ??
                                    throw new JsonException("Failed to get azure-log-analytics.auth options converter");

                authOptionsConverter.Write(writer, value.Auth, options);
            }

            if (value?.UserProvidedLogType is true)
            {
                writer.WritePropertyName("log-type");
                JsonSerializer.Serialize(writer, value.LogType, options);
            }

            if (value?.UserProvidedFlushIntervalSeconds is true)
            {
                writer.WritePropertyName("flush-interval-seconds");
                JsonSerializer.Serialize(writer, value.FlushIntervalSeconds, options);
            }

            writer.WriteEndObject();
        }
    }
}
