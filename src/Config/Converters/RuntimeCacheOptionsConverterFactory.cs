// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

/// <summary>
/// Defines how DAB reads and writes the runtime cache options (JSON).
/// </summary>
internal class RuntimeCacheOptionsConverterFactory : JsonConverterFactory
{
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(RuntimeCacheOptions));
    }

    /// <inheritdoc/>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return new RuntimeCacheOptionsConverter();
    }

    private class RuntimeCacheOptionsConverter : JsonConverter<RuntimeCacheOptions>
    {
        /// <summary>
        /// Defines how DAB reads the runtime cache options and defines which values are
        /// used to instantiate RuntimeCacheOptions.
        /// </summary>
        /// <exception cref="JsonException">Thrown when improperly formatted cache options are provided.</exception>
        public override RuntimeCacheOptions? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // Remove the converter so we don't recurse.
            JsonSerializerOptions jsonSerializerOptions = new(options);
            jsonSerializerOptions.Converters.Remove(jsonSerializerOptions.Converters.First(c => c is RuntimeCacheOptionsConverterFactory));

            RuntimeCacheOptions? res = JsonSerializer.Deserialize<RuntimeCacheOptions>(ref reader, jsonSerializerOptions);

            if (res is not null)
            {
                if (res.TtlSeconds <= 0)
                {
                    throw new JsonException($"Invalid value for ttl-seconds: {res.TtlSeconds}. Value must be greater than 0.");
                }
            }

            return res;
        }

        /// <summary>
        /// When writing the RuntimeCacheOptions back to a JSON file, only write the ttl-seconds
        /// property and value when RuntimeCacheOptions.Enabled is true. This avoids polluting
        /// the written JSON file with a property the user most likely omitted when writing the
        /// original DAB runtime config file.
        /// This Write operation is only used when a RuntimeConfig object is serialized to JSON.
        /// </summary>
        public override void Write(Utf8JsonWriter writer, RuntimeCacheOptions value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteBoolean("enabled", value?.Enabled ?? false);

            if (value is not null)
            {
                if (value.UserProvidedTtlOptions is true)
                {
                    writer.WritePropertyName("ttl-seconds");
                    JsonSerializer.Serialize(writer, value.TtlSeconds, options);
                }

                if (value.Level2 is not null)
                {
                    writer.WritePropertyName("level-2");
                    JsonSerializer.Serialize(writer, value.Level2, options);
                }
            }

            writer.WriteEndObject();
        }
    }
}
