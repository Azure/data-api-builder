// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

internal class RuntimeHealthOptionsConvertorFactory : JsonConverterFactory
{
    // Determines whether to replace environment variable with its
    // value or not while deserializing.
    private bool _replaceEnvVar;

    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(RuntimeHealthCheckConfig));
    }

    /// <inheritdoc/>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return new HealthCheckOptionsConverter(_replaceEnvVar);
    }

    internal RuntimeHealthOptionsConvertorFactory(bool replaceEnvVar)
    {
        _replaceEnvVar = replaceEnvVar;
    }

    private class HealthCheckOptionsConverter : JsonConverter<RuntimeHealthCheckConfig>
    {
        // Determines whether to replace environment variable with its
        // value or not while deserializing.
        private bool _replaceEnvVar;

        /// <param name="replaceEnvVar">Whether to replace environment variable with its
        /// value or not while deserializing.</param>
        internal HealthCheckOptionsConverter(bool replaceEnvVar)
        {
            _replaceEnvVar = replaceEnvVar;
        }

        /// <summary>
        /// Defines how DAB reads the runtime's health options and defines which values are
        /// used to instantiate RuntimeHealthCheckConfig.
        /// </summary>
        /// <exception cref="JsonException">Thrown when improperly formatted health check options are provided.</exception>
        public override RuntimeHealthCheckConfig? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType is JsonTokenType.StartObject)
            {
                bool? enabled = null;
                int? cacheTtlSeconds = null;
                HashSet<string>? roles = null;

                while (reader.Read())
                {
                    if (reader.TokenType is JsonTokenType.EndObject)
                    {
                        return new RuntimeHealthCheckConfig(enabled, roles, cacheTtlSeconds);
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
                        case "cache-ttl-seconds":
                            if (reader.TokenType is not JsonTokenType.Null)
                            {
                                int parseTtlSeconds = reader.GetInt32();
                                if (parseTtlSeconds < 0)
                                {
                                    throw new JsonException($"Invalid value for health cache ttl-seconds: {parseTtlSeconds}. Value must be greater than or equal to 0.");
                                }

                                cacheTtlSeconds = parseTtlSeconds;
                            }

                            break;
                        case "roles":
                            if (reader.TokenType is not JsonTokenType.Null)
                            {
                                // Check if the token type is an array
                                if (reader.TokenType == JsonTokenType.StartArray)
                                {
                                    HashSet<string> stringList = new();

                                    // Read the array elements one by one
                                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                                    {
                                        if (reader.TokenType == JsonTokenType.String)
                                        {
                                            string? currentRole = reader.DeserializeString(_replaceEnvVar);
                                            if (!string.IsNullOrEmpty(currentRole))
                                            {
                                                stringList.Add(currentRole);
                                            }
                                        }
                                    }

                                    // After reading the array, assign it to the string[] variable
                                    roles = stringList;
                                }
                                else
                                {
                                    // Handle case where the token is not an array (e.g., throw an exception or handle differently)
                                    throw new JsonException("Expected an array of strings, but the token type is not an array.");
                                }
                            }

                            break;

                        default:
                            throw new JsonException($"Unexpected property {property}");
                    }
                }
            }

            throw new JsonException("Runtime Health Options has a missing }.");
        }

        public override void Write(Utf8JsonWriter writer, RuntimeHealthCheckConfig value, JsonSerializerOptions options)
        {
            if (value?.UserProvidedEnabled is true)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("enabled");
                JsonSerializer.Serialize(writer, value.Enabled, options);
                if (value?.UserProvidedTtlOptions is true)
                {
                    writer.WritePropertyName("cache-ttl-seconds");
                    JsonSerializer.Serialize(writer, value.CacheTtlSeconds, options);
                }

                if (value?.Roles is not null)
                {
                    writer.WritePropertyName("roles");
                    JsonSerializer.Serialize(writer, value.Roles, options);
                }

                writer.WriteEndObject();
            }
            else
            {
                writer.WriteNullValue();
            }
        }
    }
}
