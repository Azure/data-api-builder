// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

/// <summary>
/// This converter is used to convert all the autoentities defined in the configuration file
/// each into a <see cref="Autoentity"/> object. The resulting collection is then wrapped in the
/// <see cref="RuntimeAutoentities"/> object.
/// </summary>
class RuntimeAutoentitiesConverter : JsonConverter<RuntimeAutoentities>
{
    /// <inheritdoc/>
    public override RuntimeAutoentities? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        Dictionary<string, Autoentity> autoEntities =
            JsonSerializer.Deserialize<Dictionary<string, Autoentity>>(ref reader, options) ??
            throw new JsonException("Failed to read autoentities");

        return new RuntimeAutoentities(new ReadOnlyDictionary<string, Autoentity>(autoEntities));
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, RuntimeAutoentities value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach ((string key, Autoentity autoEntity) in value.Autoentities)
        {
            writer.WritePropertyName(key);
            JsonSerializer.Serialize(writer, autoEntity, options);
        }

        writer.WriteEndObject();
    }
}
