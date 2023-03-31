namespace Azure.DataApiBuilder.Config;

public record RuntimeConfig(
    DataSource DataSource,
    RuntimeOptions Runtime,
    Dictionary<string, Entity> Entities
    )
{
    public bool TryGetEntity(string name, out Entity? entity)
    {
        return Entities.TryGetValue(name, out entity);
    }
};
