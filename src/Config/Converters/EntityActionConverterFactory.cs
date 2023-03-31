// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.Converters;

internal class EntityActionConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(EntityAction));
    }

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return new EntityActionConverter();
    }

    private class EntityActionConverter : JsonConverter<EntityAction>
    {
        public override EntityAction? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                string? action = reader.GetString();

                return new EntityAction(action!, new EntityActionFields(Array.Empty<string>(), Array.Empty<string>()), new EntityActionPolicy(""));
            }

            JsonSerializerOptions innerOptions = new(options);
            innerOptions.Converters.Remove(innerOptions.Converters.First(c => c is EntityActionConverterFactory));

            return JsonSerializer.Deserialize<EntityAction>(ref reader, innerOptions);
        }

        public override void Write(Utf8JsonWriter writer, EntityAction value, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }
    }
}
