// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.Converters;

internal class RestRuntimeOptionsConverterFactory : JsonConverterFactory
{
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(RestRuntimeOptions));
    }

    /// <inheritdoc/>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return new RestRuntimeOptionsConverter();
    }

    private class RestRuntimeOptionsConverter : JsonConverter<RestRuntimeOptions>
    {
        public override RestRuntimeOptions? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.True)
            {
                return new RestRuntimeOptions();
            }

            if (reader.TokenType == JsonTokenType.Null || reader.TokenType == JsonTokenType.False)
            {
                return new RestRuntimeOptions(false, null);
            }

            JsonSerializerOptions innerOptions = new(options);
            _ = innerOptions.Converters.Remove(innerOptions.Converters.First(c => c is RestRuntimeOptionsConverterFactory));

            return JsonSerializer.Deserialize<RestRuntimeOptions>(ref reader, innerOptions);
        }

        public override void Write(Utf8JsonWriter writer, RestRuntimeOptions value, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }
    }
}
