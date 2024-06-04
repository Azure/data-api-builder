// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

/// <summary>
/// Defines how DAB reads and writes host options.
/// </summary>
internal class HostOptionsConvertorFactory : JsonConverterFactory
{
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(HostOptions));
    }

    /// <inheritdoc/>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return new HostOptionsConverter();
    }

    private class HostOptionsConverter : JsonConverter<HostOptions>
    {
        /// <summary>
        /// Defines how DAB reads host options and defines which values are
        /// used to instantiate HostOptions.
        /// Uses default deserialize.
        /// </summary>
        /// <exception cref="JsonException">Thrown when improperly formatted host options are provided.</exception>
        public override HostOptions? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // Remove the converter so we don't recurse.
            JsonSerializerOptions jsonSerializerOptions = new(options);
            jsonSerializerOptions.Converters.Remove(jsonSerializerOptions.Converters.First(c => c is HostOptionsConvertorFactory));
            return JsonSerializer.Deserialize<HostOptions>(ref reader, jsonSerializerOptions);
        }

        /// <summary>
        /// When writing the HostOptions back to a JSON file, only write the MaxResponseSizeMB property
        /// if the property is user provided. This avoids polluting the written JSON file with a property
        /// the user most likely ommitted when writing the original DAB runtime config file.
        /// This Write operation is only used when a RuntimeConfig object is serialized to JSON.
        /// </summary>
        public override void Write(Utf8JsonWriter writer, HostOptions value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("cors");
            JsonSerializer.Serialize(writer, value.Cors, options);
            writer.WritePropertyName("authentication");
            JsonSerializer.Serialize(writer, value.Authentication, options);
            writer.WritePropertyName("mode");
            JsonSerializer.Serialize(writer, value.Mode, options);

            if (value?.UserProvidedMaxResponseSizeMB is true)
            {
                writer.WritePropertyName("max-response-size-mb");
                JsonSerializer.Serialize(writer, value.MaxResponseSizeMB, options);
            }

            writer.WriteEndObject();
        }
    }
}
