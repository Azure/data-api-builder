// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record RuntimeConfig
{
    [JsonPropertyName("$schema")]
    public string Schema { get; init; }

    public DataSource DataSource { get; init; }

    public RuntimeOptions Runtime { get; init; }

    public RuntimeEntities Entities { get; init; }

    public string DefaultDataSourceName;

    public Dictionary<string, DataSource> DataSourceNameToDataSource { get; set; }

    public Dictionary<string, string> EntityNameToDataSourceName { get; set; }

    /// <summary>
    /// Constructor for runtimeConfig.
    /// </summary>
    /// <param name="Schema">schema.</param>
    /// <param name="DataSource">Default datasource.</param>
    /// <param name="Runtime">Runtime settings.</param>
    /// <param name="Entities">Entities</param>
    [JsonConstructor]
    public RuntimeConfig(string Schema, DataSource DataSource, RuntimeOptions Runtime, RuntimeEntities Entities)
    {
        this.Schema = Schema;
        this.DataSource = DataSource;
        this.Runtime = Runtime;
        this.Entities = Entities;
        this.DataSourceNameToDataSource = new Dictionary<string, DataSource>();
        this.DefaultDataSourceName = Guid.NewGuid().ToString();
        this.DataSourceNameToDataSource.Add(this.DefaultDataSourceName, this.DataSource);

        this.EntityNameToDataSourceName = new Dictionary<string, string>();
        foreach (KeyValuePair<string, Entity> entity in Entities)
        {
            EntityNameToDataSourceName.TryAdd(entity.Key, this.DefaultDataSourceName);
        }

    }

    /// <summary>
    /// Serializes the RuntimeConfig object to JSON for writing to file.
    /// </summary>
    /// <returns></returns>
    public string ToJson(JsonSerializerOptions? jsonSerializerOptions = null)
    {
        // get default serializer options if none provided.
        jsonSerializerOptions = jsonSerializerOptions ?? RuntimeConfigLoader.GetSerializationOptions();
        return JsonSerializer.Serialize(this, jsonSerializerOptions);
    }
}
