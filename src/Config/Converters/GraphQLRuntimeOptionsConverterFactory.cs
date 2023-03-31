// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.Converters;

internal class GraphQLRuntimeOptionsConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(GraphQLRuntimeOptions));
    }

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return new GraphQLRuntimeOptionsConverter();
    }

    private class GraphQLRuntimeOptionsConverter : JsonConverter<GraphQLRuntimeOptions>
    {
        public override GraphQLRuntimeOptions? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.True)
            {
                return new GraphQLRuntimeOptions();
            }

            if (reader.TokenType == JsonTokenType.Null || reader.TokenType == JsonTokenType.False)
            {
                return new GraphQLRuntimeOptions(false, null);
            }

            JsonSerializerOptions innerOptions = new(options);
            _ = innerOptions.Converters.Remove(innerOptions.Converters.First(c => c is GraphQLRuntimeOptionsConverterFactory));

            return JsonSerializer.Deserialize<GraphQLRuntimeOptions>(ref reader, innerOptions);
        }

        public override void Write(Utf8JsonWriter writer, GraphQLRuntimeOptions value, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }
    }
}
