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
    public string DefaultDBName;

    [JsonIgnore]
    public Dictionary<string, DataSource> DatasourceNameToDataSource { get; set; }

    [JsonIgnore]
    public Dictionary<string, string> EntityNameToDataSourceName { get; set; }

    [JsonConstructor]
    public RuntimeConfig(string Schema, DataSource DataSource, RuntimeOptions Runtime, RuntimeEntities Entities)
    {
        this.Schema = Schema;
        this.DataSource = DataSource;
        this.Runtime = Runtime;
        this.Entities = Entities;
        this.DatasourceNameToDataSource = new Dictionary<string, DataSource>();
        this.EntityNameToDataSourceName = new Dictionary<string, string>();

        this.DefaultDBName = Guid.NewGuid().ToString();
        this.DatasourceNameToDataSource.Add(this.DefaultDBName, this.DataSource);

        if (Entities != null)
        {
            foreach (KeyValuePair<string, Entity> entity in Entities)
            {
                EntityNameToDataSourceName.Add(entity.Key, DefaultDBName);
            }
        }
    }

    public RuntimeConfig(string Schema, Dictionary<string, DataSource> dataSourceDict, Dictionary<string, string> entityNameToDataSourceDict, RuntimeOptions Runtime, RuntimeEntities Entities)
    {
        this.Schema = Schema;
        this.DatasourceNameToDataSource = dataSourceDict;
        this.EntityNameToDataSourceName = entityNameToDataSourceDict;
        this.Runtime = Runtime;
        this.Entities = Entities;
        KeyValuePair<string, DataSource> keyValuePair = dataSourceDict.First();
        this.DefaultDBName = keyValuePair.Key;
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
