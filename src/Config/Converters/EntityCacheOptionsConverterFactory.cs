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
    // Determines whether to replace environment variable with its
    // value or not while deserializing.
    private bool _replaceEnvVar;

    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(EntityCacheOptions));
    }

    /// <inheritdoc/>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return new EntityCacheOptionsConverter(_replaceEnvVar);
    }

    /// <param name="replaceEnvVar">Whether to replace environment variable with its
    /// value or not while deserializing.</param>
    internal EntityCacheOptionsConverterFactory(bool replaceEnvVar)
    {
        _replaceEnvVar = replaceEnvVar;
    }

    private class EntityCacheOptionsConverter : JsonConverter<EntityCacheOptions>
    {
        // Determines whether to replace environment variable with its
        // value or not while deserializing.
        private bool _replaceEnvVar;

        /// <param name="replaceEnvVar">Whether to replace environment variable with its
        /// value or not while deserializing.</param>
        public EntityCacheOptionsConverter(bool replaceEnvVar)
        {
            _replaceEnvVar = replaceEnvVar;
        }

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

                EntityCacheLevel? level = null;

                while (reader.Read())
                {
                    if (reader.TokenType is JsonTokenType.EndObject)
                    {
                        return new EntityCacheOptions(enabled, ttlSeconds, level);
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
                        case "level":
                            if (reader.TokenType is JsonTokenType.Null)
                            {
                                throw new JsonException("level property cannot be null.");
                            }

                            level = EnumExtensions.Deserialize<EntityCacheLevel>(reader.DeserializeString(_replaceEnvVar)!);

                            break;
                    }
                }
            }

            throw new JsonException();
        }

        /// <summary>
        /// When writing the EntityCacheOptions back to a JSON file, only write the ttl-seconds
        /// and level properties and values when EntityCacheOptions.Enabled is true.
        /// This avoids polluting the written JSON file with a property the user most likely
        /// omitted when writing the original DAB runtime config file.
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

            if (value?.UserProvidedLevelOptions is true)
            {
                writer.WritePropertyName("level");
                JsonSerializer.Serialize(writer, value.Level, options);
            }

            writer.WriteEndObject();
        }
    }
}
