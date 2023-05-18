using System.Text.Json;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config;

public record RuntimeConfig(
    [property: JsonPropertyName("$schema")] string Schema,
    DataSource DataSource,
    RuntimeOptions Runtime,
    RuntimeEntities Entities)
{
    /// <summary>
    /// Serializes the RuntimeConfig object to JSON for writing to file.
    /// </summary>
    /// <returns></returns>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this, RuntimeConfigLoader.GetSerializationOptions());
    }
}
