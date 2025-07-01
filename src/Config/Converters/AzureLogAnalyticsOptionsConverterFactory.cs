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
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(AzureLogAnalyticsOptions));
    }

    /// <inheritdoc/>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return new AzureLogAnalyticsOptionsConverter();
    }

    private class AzureLogAnalyticsOptionsConverter : JsonConverter<AzureLogAnalyticsOptions>
    {
        /// <summary>
        /// Defines how DAB reads Azure Log Analytics options and defines which values are
        /// used to instantiate AzureLogAnalyticsOptions.
        /// Uses default deserialize.
        /// </summary>
        /// <exception cref="JsonException">Thrown when improperly formatted Azure Log Analytics options are provided.</exception>
        public override AzureLogAnalyticsOptions? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // Remove the converter so we don't recurse.
            JsonSerializerOptions jsonSerializerOptions = new(options);
            jsonSerializerOptions.Converters.Remove(jsonSerializerOptions.Converters.First(c => c is AzureLogAnalyticsOptionsConverterFactory));
            return JsonSerializer.Deserialize<AzureLogAnalyticsOptions>(ref reader, jsonSerializerOptions);
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
                writer.WritePropertyName("auth");
                JsonSerializer.Serialize(writer, value.Auth, options);
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
