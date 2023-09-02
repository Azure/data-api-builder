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

    [JsonConstructor]
    public RuntimeConfig(string Schema, DataSource DataSource, RuntimeEntities Entities, RuntimeOptions? Runtime = null)
    {
        this.Schema = Schema;

        this.DataSource = DataSource;
        this.Runtime = Runtime ??
            new RuntimeOptions(
                new RestRuntimeOptions(Enabled: DataSource.DatabaseType != DatabaseType.CosmosDB_NoSQL),
                GraphQL: null, // even though we pass null here, the constructor will take care of initializing with defaults.
                Host: null); 

        this.Entities = Entities;
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
