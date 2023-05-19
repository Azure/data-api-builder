// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.Converters;

class RuntimeEntitiesConverter : JsonConverter<RuntimeEntities>
{
    /// <inheritdoc/>
    public override RuntimeEntities? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        IReadOnlyDictionary<string, Entity> entities =
            JsonSerializer.Deserialize<ReadOnlyDictionary<string, Entity>>(ref reader, options) ??
            throw new JsonException("Failed to read entities");

        return new RuntimeEntities(entities);
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, RuntimeEntities value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach ((string key, Entity entity) in value.Entities)
        {
            string json = JsonSerializer.Serialize(entity, options);
            writer.WritePropertyName(key);
            writer.WriteRawValue(json);
        }

        writer.WriteEndObject();
    }
}
