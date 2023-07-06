// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

/// <summary>
/// Used to convert an <see cref="EntityAction"/> to and from JSON by creating a <see cref="EntityActionConverter"/> if needed.
/// </summary>
/// <remarks>
/// This is needed so we can remove the converter from the options before we deserialize the object to avoid infinite recursion.
/// </remarks>
internal class EntityActionConverterFactory : JsonConverterFactory
{
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(EntityAction));
    }

    /// <inheritdoc/>
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

                return new EntityAction(Action: op, Fields: null, Policy: null);
            }

            // Remove the converter so we don't recurse.
            JsonSerializerOptions innerOptions = new(options);
            innerOptions.Converters.Remove(innerOptions.Converters.First(c => c is EntityActionConverterFactory));

            EntityAction? action = JsonSerializer.Deserialize<EntityAction>(ref reader, innerOptions);

            if (action is null)
            {
                return null;
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
            // Remove the converter so we don't recurse.
            JsonSerializerOptions innerOptions = new(options);
            innerOptions.Converters.Remove(innerOptions.Converters.First(c => c is EntityActionConverterFactory));
            JsonSerializer.Serialize(writer, value, innerOptions);
        }
    }
}
