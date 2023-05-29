// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

internal class EntitySourceConverterFactory : JsonConverterFactory
{
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(EntitySource));
    }

    /// <inheritdoc/>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return new EntitySourceConverter();
    }

    private class EntitySourceConverter : JsonConverter<EntitySource>
    {
        public override EntitySource? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                string? obj = reader.DeserializeString();
                return new EntitySource(obj ?? "", EntitySourceType.Table, new(), Enumerable.Empty<string>().ToArray());
            }

            // Remove the converter so we don't recurse.
            JsonSerializerOptions innerOptions = new(options);
            innerOptions.Converters.Remove(innerOptions.Converters.First(c => c is EntitySourceConverterFactory));

            EntitySource? source = JsonSerializer.Deserialize<EntitySource>(ref reader, innerOptions);

            if (source?.Parameters is not null)
            {
                // If we get parameters back the value field will be JsonElement, since that's what STJ uses for the `object` type.
                // But we want to convert that to a CLR type so we can use it in our code and avoid having to do our own type checking
                // and casting elsewhere.
                return source with { Parameters = source.Parameters.ToDictionary(p => p.Key, p => GetClrValue((JsonElement)p.Value)) };
            }

            return source;
        }

        private static object GetClrValue(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString() ?? "",
                JsonValueKind.Number => element.GetInt32(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => element.ToString()
            };
        }

        public override void Write(Utf8JsonWriter writer, EntitySource value, JsonSerializerOptions options)
        {
            // Remove the converter so we don't recurse.
            JsonSerializerOptions innerOptions = new(options);
            innerOptions.Converters.Remove(innerOptions.Converters.First(c => c is EntitySourceConverterFactory));

            JsonSerializer.Serialize(writer, value, innerOptions);
        }
    }
}
