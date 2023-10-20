// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Azure.DataApiBuilder.Service.Exceptions;

namespace Azure.DataApiBuilder.Config.Converters;

/// <summary>
/// Custom string json converter factory to replace environment variables of the pattern
/// @env('ENV_NAME') with their value during deserialization.
/// </summary>
public class StringJsonConverterFactory : JsonConverterFactory
{
    private EnvironmentVariableReplacementFailureMode _replacementFailureMode;

    public StringJsonConverterFactory(EnvironmentVariableReplacementFailureMode replacementFailureMode)
    {
        _replacementFailureMode = replacementFailureMode;
    }

    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(string));
    }

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return new StringJsonConverter(_replacementFailureMode);
    }

    class StringJsonConverter : JsonConverter<string>
    {
        // @env\('  : match @env('
        // .*?      : lazy match any character except newline 0 or more times
        // (?='\))  : look ahead for ') which will combine with our lazy match
        //            ie: in @env('hello')goodbye') we match @env('hello')
        // '\)      : consume the ') into the match (look ahead doesn't capture)
        // This pattern lazy matches any string that starts with @env(' and ends with ')
        // ie: fooBAR@env('hello-world')bash)FOO')  match: @env('hello-world')
        // This matching pattern allows for the @env('<match>') to be safely nested
        // within strings that contain ') after our match.
        // ie: if the environment variable "Baz" has the value of "Bar"
        // fooBarBaz: "('foo@env('Baz')Baz')" would parse into
        // fooBarBaz: "('fooBarBaz')"
        // Note that there is no escape character currently for ') to exist
        // within the name of the environment variable, but that ') is not
        // a valid environment variable name in certain shells.
        const string ENV_PATTERN = @"@env\('.*?(?='\))'\)";
        private EnvironmentVariableReplacementFailureMode _replacementFailureMode;

        public StringJsonConverter(EnvironmentVariableReplacementFailureMode replacementFailureMode)
        {
            _replacementFailureMode = replacementFailureMode;
        }

        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                string? value = reader.GetString();
                return Regex.Replace(value!, ENV_PATTERN, new MatchEvaluator(ReplaceMatchWithEnvVariable));
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

        private string ReplaceMatchWithEnvVariable(Match match)
        {
            // [^@env\(]   :  any substring that is not @env(
            // .*          :  any char except newline any number of times
            // (?=\))      :  look ahead for end char of )
            // This pattern greedy matches all characters that are not a part of @env()
            // ie: @env('hello@env('goodbye')world') match: 'hello@env('goodbye')world'
            string innerPattern = @"[^@env\(].*(?=\))";

            // strips first and last characters, ie: '''hello'' --> ''hello'
            string envName = Regex.Match(match.Value, innerPattern).Value[1..^1];
            string? envValue = Environment.GetEnvironmentVariable(envName);
            if (_replacementFailureMode == EnvironmentVariableReplacementFailureMode.Throw)
            {
                return envValue is not null ? envValue :
                    throw new DataApiBuilderException(message: $"Environmental Variable, {envName}, not found.",
                                                   statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                                                   subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }
            else
            {
                return envValue ?? match.Value;
            }
        }
    }
}
