// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Azure.DataApiBuilder.Config.Converters;

/// <summary>
/// Custom string json converter factory to replace environment variables and other variable patterns
/// during deserialization using the DeserializationVariableReplacementSettings.
/// </summary>
public class StringJsonConverterFactory : JsonConverterFactory
{
    private readonly DeserializationVariableReplacementSettings _replacementSettings;

    public StringJsonConverterFactory(DeserializationVariableReplacementSettings replacementSettings)
    {
        _replacementSettings = replacementSettings;
    }

    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(string));
    }

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return new StringJsonConverter(_replacementSettings);
    }

    class StringJsonConverter : JsonConverter<string>
    {
        private DeserializationVariableReplacementSettings _replacementSettings;

        public StringJsonConverter(DeserializationVariableReplacementSettings replacementSettings)
        {
            _replacementSettings = replacementSettings;
        }

        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                string? value = reader.GetString();
                if (string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }

                // Apply all replacement strategies configured in the settings
                foreach (KeyValuePair<Regex, Func<Match, string>> strategy in _replacementSettings.ReplacementStrategies)
                {
                    value = strategy.Key.Replace(value, new MatchEvaluator(strategy.Value));
                }

                return value;
            }

            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            throw new JsonException();
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value);
        }
    }
}
