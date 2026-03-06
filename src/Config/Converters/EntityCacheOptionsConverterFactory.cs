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
    private readonly DeserializationVariableReplacementSettings? _replacementSettings;

    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(EntityCacheOptions));
    }

    /// <inheritdoc/>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return new EntityCacheOptionsConverter(_replacementSettings);
    }

    /// <param name="replacementSettings">The replacement settings to use while deserializing.</param>
    internal EntityCacheOptionsConverterFactory(DeserializationVariableReplacementSettings? replacementSettings)
    {
        _replacementSettings = replacementSettings;
    }

    private class EntityCacheOptionsConverter : JsonConverter<EntityCacheOptions>
    {
        // Determines whether to replace environment variable with its
        // value or not while deserializing.
        private readonly DeserializationVariableReplacementSettings? _replacementSettings;

        /// <param name="replacementSettings">The replacement settings to use while deserializing.</param>
        public EntityCacheOptionsConverter(DeserializationVariableReplacementSettings? replacementSettings)
        {
            _replacementSettings = replacementSettings;
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
                // Default to null (unset) so that an empty cache object ("cache": {})
                // is treated as "not explicitly configured" and inherits from the runtime setting.
                bool? enabled = null;

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

                            level = EnumExtensions.Deserialize<EntityCacheLevel>(reader.DeserializeString(_replacementSettings)!);

                            break;
                    }
                }
            }

            throw new JsonException();
        }

        /// <summary>
        /// When writing the EntityCacheOptions back to a JSON file, only write each sub-property
        /// when its corresponding UserProvided* flag is true. This avoids polluting the written
        /// JSON file with properties the user omitted (defaults or inherited values).
        /// If the user provided a cache object (Entity.Cache is non-null), we always write the
        /// object — even if it ends up empty ("cache": {}) — because the user explicitly included it.
        /// Entity.Cache being null means the user never wrote a cache property, and the serializer's
        /// DefaultIgnoreCondition.WhenWritingNull suppresses the "cache" key entirely.
        /// </summary>
        public override void Write(Utf8JsonWriter writer, EntityCacheOptions value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            if (value?.UserProvidedEnabledOptions is true)
            {
                writer.WriteBoolean("enabled", value.Enabled!.Value);
            }

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
