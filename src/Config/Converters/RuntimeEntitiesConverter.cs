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
            string json = JsonSerializer.Serialize(entity, options);
            writer.WritePropertyName(key);
            writer.WriteRawValue(json);
        }

        writer.WriteEndObject();
    }
}

public static class KeyValuePairExtensions
{
    public static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> keyValuePair, out TKey key, out TValue value)
    {
        key = keyValuePair.Key;
        value = keyValuePair.Value;
    }

    public static bool TryAdd<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key, TValue value)
    {
        if (dictionary == null)
        {
            throw new ArgumentNullException(nameof(dictionary));
        }

        if (!dictionary.ContainsKey(key))
        {
            dictionary.Add(key, value);
            return true;
        }

        return false;
    }
}
