// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

/// <summary>
/// Defines how DAB reads and writes Azure Key Vault Retry Policies (JSON).
/// </summary>
internal class AKVRetryPolicyOptionsConverterFactory : JsonConverterFactory
{
    // Determines whether to replace environment variable with its
    // value or not while deserializing.
    private bool _replaceEnvVar;

    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(AKVRetryPolicyOptions));
    }

    /// <inheritdoc/>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return new AKVRetryPolicyOptionsConverter(_replaceEnvVar);
    }

    /// <param name="replaceEnvVar">Whether to replace environment variable with its
    /// value or not while deserializing.</param>
    internal AKVRetryPolicyOptionsConverterFactory(bool replaceEnvVar)
    {
        _replaceEnvVar = replaceEnvVar;
    }

    private class AKVRetryPolicyOptionsConverter : JsonConverter<AKVRetryPolicyOptions>
    {
        // Determines whether to replace environment variable with its
        // value or not while deserializing.
        private bool _replaceEnvVar;

        /// <param name="replaceEnvVar">Whether to replace environment variable with its
        /// value or not while deserializing.</param>
        public AKVRetryPolicyOptionsConverter(bool replaceEnvVar)
        {
            _replaceEnvVar = replaceEnvVar;
        }

        /// <summary>
        /// Defines how DAB reads AKV Retry Policy options and defines which values are
        /// used to instantiate those options.
        /// </summary>
        /// <exception cref="JsonException">Thrown when improperly formatted cache options are provided.</exception>
        public override AKVRetryPolicyOptions? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType is JsonTokenType.StartObject)
            {
                AKVRetryPolicyMode? mode = null;
                int? maxCount = null;
                int? delaySeconds = null;
                int? maxDelaySeconds = null;
                int? networkTimeoutSeconds = null;

                while (reader.Read())
                {
                    if (reader.TokenType is JsonTokenType.EndObject)
                    {
                        return new AKVRetryPolicyOptions(mode, maxCount, delaySeconds, maxDelaySeconds, networkTimeoutSeconds);
                    }

                    string? property = reader.GetString();
                    reader.Read();

                    switch (property)
                    {
                        case "mode":
                            if (reader.TokenType is JsonTokenType.Null)
                            {
                                mode = null;
                            }
                            else
                            {
                                mode = EnumExtensions.Deserialize<AKVRetryPolicyMode>(reader.DeserializeString(_replaceEnvVar)!);
                            }

                            break;
                        case "max-count":
                            if (reader.TokenType is JsonTokenType.Null)
                            {
                                maxCount = null;
                            }
                            else
                            {
                                int parseMaxCount = reader.GetInt32();
                                if (parseMaxCount < 0)
                                {
                                    throw new JsonException($"Invalid value for max-count: {parseMaxCount}. Value must not be negative.");
                                }

                                maxCount = parseMaxCount;
                            }

                            break;
                        case "delay-seconds":
                            if (reader.TokenType is JsonTokenType.Null)
                            {
                                delaySeconds = null;
                            }
                            else
                            {
                                int parseDelaySeconds = reader.GetInt32();
                                if (parseDelaySeconds <= 0)
                                {
                                    throw new JsonException($"Invalid value for delay-seconds: {parseDelaySeconds}. Value must be greater than 0.");
                                }

                                delaySeconds = parseDelaySeconds;
                            }

                            break;
                        case "max-delay-seconds":
                            if (reader.TokenType is JsonTokenType.Null)
                            {
                                maxDelaySeconds = null;
                            }
                            else
                            {
                                int parseMaxDelaySeconds = reader.GetInt32();
                                if (parseMaxDelaySeconds <= 0)
                                {
                                    throw new JsonException($"Invalid value for max-delay-seconds: {parseMaxDelaySeconds}. Value must be greater than 0.");
                                }

                                maxDelaySeconds = parseMaxDelaySeconds;
                            }

                            break;
                        case "network-timeout-seconds":
                            if (reader.TokenType is JsonTokenType.Null)
                            {
                                networkTimeoutSeconds = null;
                            }
                            else
                            {
                                int parseNetworkTimeoutSeconds = reader.GetInt32();
                                if (parseNetworkTimeoutSeconds <= 0)
                                {
                                    throw new JsonException($"Invalid value for network-timeout-seconds: {parseNetworkTimeoutSeconds}. Value must be greater than 0.");
                                }

                                networkTimeoutSeconds = parseNetworkTimeoutSeconds;
                            }

                            break;
                    }
                }
            }

            throw new JsonException("Failed to read the Azure Key Vault Retry Policy Options");
        }

        /// <summary>
        /// When writing the AKVRetryPolicyOptions back to a JSON file, only write the properties and values
        /// when those AKVRetryPolicyOptions are user provided.
        /// This avoids polluting the written JSON file with a property the user most likely
        /// omitted when writing the original DAB runtime config file.
        /// This Write operation is only used when a RuntimeConfig object is serialized to JSON.
        /// </summary>
        public override void Write(Utf8JsonWriter writer, AKVRetryPolicyOptions value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            if (value?.UserProvidedMode is true)
            {
                writer.WritePropertyName("mode");
                JsonSerializer.Serialize(writer, value.Mode, options);
            }

            if (value?.UserProvidedMaxCount is true)
            {
                writer.WritePropertyName("max-count");
                JsonSerializer.Serialize(writer, value.MaxCount, options);
            }

            if (value?.UserProvidedDelaySeconds is true)
            {
                writer.WritePropertyName("delay-seconds");
                JsonSerializer.Serialize(writer, value.DelaySeconds, options);
            }

            if (value?.UserProvidedMaxDelaySeconds is true)
            {
                writer.WritePropertyName("max-delay-seconds");
                JsonSerializer.Serialize(writer, value.MaxDelaySeconds, options);
            }

            if (value?.UserProvidedNetworkTimeoutSeconds is true)
            {
                writer.WritePropertyName("network-timeout-seconds");
                JsonSerializer.Serialize(writer, value.NetworkTimeoutSeconds, options);
            }

            writer.WriteEndObject();
        }
    }
}
