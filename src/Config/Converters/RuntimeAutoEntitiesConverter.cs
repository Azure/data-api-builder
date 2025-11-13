// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

/// <summary>
/// Custom JSON converter for RuntimeAutoEntities.
/// </summary>
class RuntimeAutoEntitiesConverter : JsonConverter<RuntimeAutoEntities>
{
    /// <inheritdoc/>
    public override RuntimeAutoEntities? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        Dictionary<string, AutoEntity>? autoEntities =
            JsonSerializer.Deserialize<Dictionary<string, AutoEntity>>(ref reader, options);

        return new RuntimeAutoEntities(autoEntities);
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, RuntimeAutoEntities value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach ((string key, AutoEntity autoEntity) in value)
        {
            writer.WritePropertyName(key);
            JsonSerializer.Serialize(writer, autoEntity, options);
        }

        writer.WriteEndObject();
    }
}
