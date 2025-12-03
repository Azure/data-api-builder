// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

/// <summary>
/// Defines how DAB reads and writes the compression options (JSON).
/// </summary>
internal class CompressionOptionsConverterFactory : JsonConverterFactory
{
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(CompressionOptions));
    }

    /// <inheritdoc/>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return new CompressionOptionsConverter();
    }

    private class CompressionOptionsConverter : JsonConverter<CompressionOptions>
    {
        /// <summary>
        /// Defines how DAB reads the compression options and defines which values are
        /// used to instantiate CompressionOptions.
        /// </summary>
        public override CompressionOptions? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("Expected start of object.");
            }

            CompressionLevel level = CompressionOptions.DEFAULT_LEVEL;
            bool userProvidedLevel = false;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    break;
                }

                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    string? propertyName = reader.GetString();
                    reader.Read();

                    if (string.Equals(propertyName, "level", StringComparison.OrdinalIgnoreCase))
                    {
                        string? levelStr = reader.GetString();
                        if (levelStr is not null && Enum.TryParse<CompressionLevel>(levelStr, ignoreCase: true, out CompressionLevel parsedLevel))
                        {
                            level = parsedLevel;
                            userProvidedLevel = true;
                        }
                    }
                }
            }

            return new CompressionOptions(level) with { UserProvidedLevel = userProvidedLevel };
        }

        /// <summary>
        /// When writing the CompressionOptions back to a JSON file, only write the level
        /// property and value when it was provided by the user.
        /// </summary>
        public override void Write(Utf8JsonWriter writer, CompressionOptions value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            if (value is not null && value.UserProvidedLevel)
            {
                writer.WritePropertyName("level");
                writer.WriteStringValue(value.Level.ToString().ToLowerInvariant());
            }

            writer.WriteEndObject();
        }
    }
}
