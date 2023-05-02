// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

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
                EntityActionOperation op = JsonSerializer.Deserialize<EntityActionOperation>(ref reader, options);

                return new EntityAction(op, new EntityActionFields(Exclude: new()), new EntityActionPolicy(null, null));
            }

            JsonSerializerOptions innerOptions = new(options);
            innerOptions.Converters.Remove(innerOptions.Converters.First(c => c is EntityActionConverterFactory));

            EntityAction? action = JsonSerializer.Deserialize<EntityAction>(ref reader, innerOptions);

            if (action is null)
            {
                return null;
            }

            if (action.Policy is null)
            {
                return action with { Policy = new EntityActionPolicy(null, null) };
            }

            // While Fields.Exclude is non-nullable, if the property was not in the JSON
            // it will be set to `null` by the deserializer, so we'll do a cleanup here.
            if (action.Fields is not null && action.Fields.Exclude is null)
            {
                action = action with { Fields = action.Fields with { Exclude = new() } };
            }

            return action;
        }

        public override void Write(Utf8JsonWriter writer, EntityAction value, JsonSerializerOptions options)
        {
            JsonSerializerOptions innerOptions = new(options);
            innerOptions.Converters.Remove(innerOptions.Converters.First(c => c is EntityActionConverterFactory));
            JsonSerializer.Serialize(writer, value, innerOptions);
        }
    }
}
