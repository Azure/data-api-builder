// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

/// <summary>
/// Defines how DAB reads and writes a runtime cache options L2 (JSON).
/// </summary>
internal class RuntimeCacheLevel2OptionsConverterFactory : JsonConverterFactory
{
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(RuntimeCacheLevel2Options));
    }

    /// <inheritdoc/>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return new RuntimeCacheLevel2OptionsConverter();
    }

    private class RuntimeCacheLevel2OptionsConverter : JsonConverter<RuntimeCacheLevel2Options>
    {
        /// <summary>
        /// Defines how DAB reads the runtime cache level2 options and defines which values are
        /// used to instantiate RuntimeCacheLevel2Options.
        /// </summary>
        /// <exception cref="JsonException">Thrown when improperly formatted cache options are provided.</exception>
        public override RuntimeCacheLevel2Options? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // Remove the converter so we don't recurse.
            JsonSerializerOptions jsonSerializerOptions = new(options);
            jsonSerializerOptions.Converters.Remove(jsonSerializerOptions.Converters.First(c => c is RuntimeCacheLevel2OptionsConverterFactory));

            RuntimeCacheLevel2Options? res = JsonSerializer.Deserialize<RuntimeCacheLevel2Options>(ref reader, jsonSerializerOptions);

            // TODO: maybe add a check to ensure that the provider is valid?

            return res;
        }

        /// <summary>
        /// This Write operation is only used when a RuntimeConfig object is serialized to JSON.
        /// </summary>
        public override void Write(Utf8JsonWriter writer, RuntimeCacheLevel2Options value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteBoolean("enabled", value?.Enabled ?? false);

            if (value is not null)
            {
                if (value.Provider is not null)
                {
                    writer.WritePropertyName("provider");
                    JsonSerializer.Serialize(writer, value.Provider, options);
                }

                if (value.Partition is not null)
                {
                    writer.WritePropertyName("partition");
                    JsonSerializer.Serialize(writer, value.Partition, options);
                }

                if (value.ConnectionString is not null)
                {
                    writer.WritePropertyName("connection-string");
                    JsonSerializer.Serialize(writer, value.ConnectionString, options);
                }
            }

            writer.WriteEndObject();
        }
    }
}
