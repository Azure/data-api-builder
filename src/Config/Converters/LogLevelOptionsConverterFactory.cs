// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

/// <summary>
/// Defines how DAB reads and writes log level options
/// </summary>
internal class LogLevelOptionsConverterFactory : JsonConverterFactory
{
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(LogLevelOptions));
    }

    /// <inheritdoc/>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return new LogLevelOptionsConverter();
    }

    private class LogLevelOptionsConverter : JsonConverter<LogLevelOptions>
    {
        /// <summary>
        /// Defines how DAB reads loglevel options and defines which values are
        /// used to instantiate LogLevelOptions.
        /// Uses default deserialize.
        /// </summary>
        public override LogLevelOptions? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            JsonSerializerOptions jsonSerializerOptions = new(options);
            jsonSerializerOptions.Converters.Remove(jsonSerializerOptions.Converters.First(c => c is LogLevelOptionsConverterFactory));
            return JsonSerializer.Deserialize<LogLevelOptions>(ref reader, jsonSerializerOptions);
        }

        public override void Write(Utf8JsonWriter writer, LogLevelOptions value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("level");
            JsonSerializer.Serialize(writer, value.Value, options);
            writer.WriteEndObject();
        }
    }
}
