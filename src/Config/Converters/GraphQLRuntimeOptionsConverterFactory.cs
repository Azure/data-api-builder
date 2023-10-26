// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

internal class GraphQLRuntimeOptionsConverterFactory : JsonConverterFactory
{
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(GraphQLRuntimeOptions));
    }

    /// <inheritdoc/>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return new GraphQLRuntimeOptionsConverter();
    }

    private class GraphQLRuntimeOptionsConverter : JsonConverter<GraphQLRuntimeOptions>
    {
        public override GraphQLRuntimeOptions? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.True || reader.TokenType == JsonTokenType.Null)
            {
                return new GraphQLRuntimeOptions();
            }

            if (reader.TokenType == JsonTokenType.False)
            {
                return new GraphQLRuntimeOptions(Enabled: false);
            }

            // Remove the converter so we don't recurse.
            JsonSerializerOptions innerOptions = new(options);
            _ = innerOptions.Converters.Remove(innerOptions.Converters.First(c => c is GraphQLRuntimeOptionsConverterFactory));

            return JsonSerializer.Deserialize<GraphQLRuntimeOptions>(ref reader, innerOptions);
        }

        public override void Write(Utf8JsonWriter writer, GraphQLRuntimeOptions value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteBoolean("enabled", value.Enabled);
            writer.WriteString("path", value.Path);
            writer.WriteBoolean("allow-introspection", value.AllowIntrospection);
            writer.WriteEndObject();
        }
    }
}
