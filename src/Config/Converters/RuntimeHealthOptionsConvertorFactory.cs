// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

internal class RuntimeHealthOptionsConvertorFactory : JsonConverterFactory
{
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(RuntimeHealthCheckConfig));
    }

    /// <inheritdoc/>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return new HealthCheckOptionsConverter();
    }

    private class HealthCheckOptionsConverter : JsonConverter<RuntimeHealthCheckConfig>
    {
        /// <summary>
        /// Defines how DAB reads the data-source's health options and defines which values are
        /// used to instantiate DatasourceHealthCheckConfig.
        /// </summary>
        /// <exception cref="JsonException">Thrown when improperly formatted health check options are provided.</exception>
        public override RuntimeHealthCheckConfig? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return new RuntimeHealthCheckConfig();
            }

            if (reader.TokenType is JsonTokenType.StartObject)
            {
                bool? enabled = null;
                int? cacheTtlSeconds = null;
                List<string>? roles = null;

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
                                    throw new JsonException($"Invalid value for ttl-seconds: {parseTtlSeconds}. Value must be greater than 0.");
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
                                    List<string> stringList = new();

                                    // Read the array elements one by one
                                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                                    {
                                        if (reader.TokenType == JsonTokenType.String)
                                        {
                                            string? currentRole = reader.GetString();
                                            if (!string.IsNullOrEmpty(currentRole))
                                            {
                                                stringList.Add(currentRole);

                                            }
                                            /*
                                            else
                                            {
                                                Handle case where the string is empty (e.g., throw an exception or handle differently)
                                                throw new JsonException("Empty string found in array of roles while deserialization.");
                                            }
                                            */
                                        }
                                        else
                                        {
                                            // If the token is not a string, throw an exception
                                            throw new JsonException($"Invalid token type in array. Expected a string, but found {reader.TokenType}.");
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
                    }
                }
            }

            throw new JsonException();
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
