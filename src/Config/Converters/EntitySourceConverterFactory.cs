// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;

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
                return new EntitySource(reader.GetString() ?? "", EntityType.Table, new(), Enumerable.Empty<string>().ToArray());
            }

            JsonSerializerOptions innerOptions = new(options);
            innerOptions.Converters.Remove(innerOptions.Converters.First(c => c is EntitySourceConverterFactory));

            return JsonSerializer.Deserialize<EntitySource>(ref reader, innerOptions);
        }

        public override void Write(Utf8JsonWriter writer, EntitySource value, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }
    }
}
