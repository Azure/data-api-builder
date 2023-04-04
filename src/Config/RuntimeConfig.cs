using System.Collections;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.Converters;

namespace Azure.DataApiBuilder.Config;

[JsonConverter(typeof(RuntimeEntitiesConverter))]
public record RuntimeEntities(IDictionary<string, Entity> Entities) : IEnumerable<KeyValuePair<string, Entity>>
{
    public IEnumerator<KeyValuePair<string, Entity>> GetEnumerator()
    {
        return Entities.GetEnumerator();
    }

    public bool TryGetValue(string key, out Entity? entity)
    {
        return Entities.TryGetValue(key, out entity);
    }

    public bool ContainsKey(string key)
    {
        return Entities.ContainsKey(key);
    }

    public Entity this[string key] => Entities[key];

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}

public record RuntimeConfig(
    DataSource DataSource,
    RuntimeOptions Runtime,
    RuntimeEntities Entities
    );
