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
    /// <param name="DataSourceDict">DataSourceDictionary mapping.</param>
    /// <param name="EntityNameToDataSourceDict">EntityNameToDataSourceDictionary mapping.</param>
    public RuntimeConfig(string Schema, DataSource DataSource, RuntimeOptions Runtime, RuntimeEntities Entities, Dictionary<string, DataSource>? DataSourceDict = null, Dictionary<string, string>? EntityNameToDataSourceDict = null)
    {
        this.Schema = Schema;
        this.DataSource = DataSource;
        this.Runtime = Runtime;
        this.Entities = Entities;

        if (DataSourceDict is not null)
        {
            // already supplied with datasource mapping - multiple db scenario.
            this.DataSourceNameToDataSource = DataSourceDict;
            // set the first db to default - not relevant for multiple db scenario.
            this.DefaultDataSourceName = DataSourceDict.Keys.First();
        }
        else
        {
            this.DataSourceNameToDataSource = new Dictionary<string, DataSource>();
            this.DefaultDataSourceName = Guid.NewGuid().ToString();
            this.DataSourceNameToDataSource.Add(this.DefaultDataSourceName, this.DataSource);
        }

        if (EntityNameToDataSourceDict is not null)
        {
            this.EntityNameToDataSourceName = EntityNameToDataSourceDict;
        }
        else
        {
            // if no entity name mapping provided - all entities will map to the default datasource.
            this.EntityNameToDataSourceName = new Dictionary<string, string>();
            foreach (KeyValuePair<string, Entity> entity in Entities)
            {
                EntityNameToDataSourceName.TryAdd(entity.Key, this.DefaultDataSourceName);
            }
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
