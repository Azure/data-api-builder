// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

class RuntimeEntitiesConverter : JsonConverter<RuntimeEntities>
{
    /// <inheritdoc/>
    public override RuntimeEntities? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        IDictionary<string, Entity> entities =
            JsonSerializer.Deserialize<Dictionary<string, Entity>>(ref reader, options) ??
            throw new JsonException("Failed to read entities");

        return new RuntimeEntities(new ReadOnlyDictionary<string, Entity>(entities));
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, RuntimeEntities value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach ((string key, Entity entity) in value.Entities)
        {
            writer.WritePropertyName(key);
            JsonSerializer.Serialize(writer, entity, options);
        }

        writer.WriteEndObject();
    }
}
