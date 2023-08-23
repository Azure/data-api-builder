// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record RuntimeConfig
{
    [JsonPropertyName("$schema")]
    public string Schema;

    public DataSource DataSource;

    public RuntimeOptions Runtime;

    public RuntimeEntities Entities;

    [JsonIgnore]
    public string DefaultDataSourceName;

    [JsonIgnore]
    public Dictionary<string, DataSource> DataSourceNameToDataSource { get; set; }

    [JsonIgnore]
    public Dictionary<string, string> EntityNameToDataSourceName { get; set; }

    [JsonConstructor]
    public RuntimeConfig(string Schema, DataSource DataSource, RuntimeOptions Runtime, RuntimeEntities Entities)
    {
        this.Schema = Schema;
        this.DataSource = DataSource;
        this.Runtime = Runtime;
        this.Entities = Entities;
        this.DataSourceNameToDataSource = new Dictionary<string, DataSource>();
        this.EntityNameToDataSourceName = new Dictionary<string, string>();

        this.DefaultDataSourceName = Guid.NewGuid().ToString();
        this.DataSourceNameToDataSource.Add(this.DefaultDataSourceName, this.DataSource);

        foreach (KeyValuePair<string, Entity> entity in Entities)
        {
            EntityNameToDataSourceName.Add(entity.Key, DefaultDataSourceName);
        }

    }

    public RuntimeConfig(string schema, Dictionary<string, DataSource> dataSourceDict, Dictionary<string, string> entityNameToDataSourceDict, RuntimeOptions runtime, RuntimeEntities entities)
    {
        this.Schema = schema;
        this.DataSourceNameToDataSource = dataSourceDict;
        this.EntityNameToDataSourceName = entityNameToDataSourceDict;
        this.Runtime = runtime;
        this.Entities = entities;
        KeyValuePair<string, DataSource> keyValuePair = dataSourceDict.First();
        this.DefaultDataSourceName = keyValuePair.Key;
        this.DataSource = keyValuePair.Value;
    }

    /// <summary>
    /// Serializes the RuntimeConfig object to JSON for writing to file.
    /// </summary>
    /// <returns></returns>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this, RuntimeConfigLoader.GetSerializationOptions());
    }
}
