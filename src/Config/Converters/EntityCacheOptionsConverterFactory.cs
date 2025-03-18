// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

/// <summary>
/// Defines how DAB reads and writes an entity's cache options (JSON).
/// </summary>
internal class EntityCacheOptionsConverterFactory : JsonConverterFactory
{
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(EntityCacheOptions));
    }

    /// <inheritdoc/>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return new EntityCacheOptionsConverter();
    }

    private class EntityCacheOptionsConverter : JsonConverter<EntityCacheOptions>
    {
        /// <summary>
        /// Defines how DAB reads an entity's cache options and defines which values are
        /// used to instantiate EntityCacheOptions.
        /// </summary>
        /// <exception cref="JsonException">Thrown when improperly formatted cache options are provided.</exception>
        public override EntityCacheOptions? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType is JsonTokenType.StartObject)
            {
                bool? enabled = false;

                // Defer to EntityCacheOptions record definition to define default ttl value.
                int? ttlSeconds = null;

                while (reader.Read())
                {
                    if (reader.TokenType is JsonTokenType.EndObject)
                    {
                        return new EntityCacheOptions(enabled, ttlSeconds);
                    }

                    string? property = reader.GetString();
                    reader.Read();

                    switch (property)
                    {
                        case "enabled":
                            if (reader.TokenType is JsonTokenType.Null)
                            {
                                enabled = null;
                            }
                            else
                            {
                                enabled = reader.GetBoolean();
                            }

                            break;
                        case "ttl-seconds":
                            if (reader.TokenType is JsonTokenType.Null)
                            {
                                ttlSeconds = null;
                            }
                            else
                            {
                                int parseTtlSeconds = reader.GetInt32();
                                if (parseTtlSeconds <= 0)
                                {
                                    throw new JsonException($"Invalid value for ttl-seconds: {parseTtlSeconds}. Value must be greater than 0.");
                                }

                                ttlSeconds = parseTtlSeconds;
                            }

                            break;
                    }
                }
            }

            throw new JsonException();
        }

        /// <summary>
        /// When writing the EntityCacheOptions back to a JSON file, only write the ttl-seconds
        /// property and value when EntityCacheOptions.Enabled is true. This avoids polluting
        /// the written JSON file with a property the user most likely omitted when writing the
        /// original DAB runtime config file.
        /// This Write operation is only used when a RuntimeConfig object is serialized to JSON.
        /// </summary>
        public override void Write(Utf8JsonWriter writer, EntityCacheOptions value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteBoolean("enabled", value?.Enabled ?? false);

            if (value?.UserProvidedTtlOptions is true)
            {
                writer.WritePropertyName("ttl-seconds");
                JsonSerializer.Serialize(writer, value.TtlSeconds, options);
            }

            writer.WriteEndObject();
        }
    }
}
