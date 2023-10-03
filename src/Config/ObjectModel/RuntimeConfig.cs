// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Service.Exceptions;

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record RuntimeConfig
{
    [JsonPropertyName("$schema")]
    public string Schema { get; init; }

    public const string DEFAULT_CONFIG_SCHEMA_LINK = "https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch/dab.draft.schema.json";

    public DataSource DataSource { get; init; }

    public RuntimeOptions? Runtime { get; init; }

    public RuntimeEntities Entities { get; init; }

    /// <summary>
    /// Retrieves the value of runtime.rest.request-body-strict property if present, default is true.
    /// </summary>
    [JsonIgnore]
    public bool IsRequestBodyStrict =>
        Runtime is null ||
        Runtime.Rest is null ||
        Runtime.Rest.RequestBodyStrict;

    /// <summary>
    /// Retrieves the value of runtime.graphql.enabled property if present, default is true.
    /// </summary>
    [JsonIgnore]
    public bool IsGraphQLEnabled => Runtime is null ||
        Runtime.GraphQL is null ||
        Runtime.GraphQL.Enabled;

    /// <summary>
    /// Retrieves the value of runtime.rest.enabled property if present, default is true if its not cosmosdb.
    /// </summary>
    [JsonIgnore]
    public bool IsRestEnabled =>
        (Runtime is null ||
         Runtime.Rest is null ||
         Runtime.Rest.Enabled) &&
         DataSource.DatabaseType != DatabaseType.CosmosDB_NoSQL;

    /// <summary>
    /// A shorthand method to determine whether Static Web Apps is configured for the current authentication provider.
    /// </summary>
    /// <returns>True if the authentication provider is enabled for Static Web Apps, otherwise false.</returns>
    [JsonIgnore]
    public bool IsStaticWebAppsIdentityProvider =>
        Runtime is null ||
        Runtime.Host is null ||
        Runtime.Host.Authentication is null ||
        EasyAuthType.StaticWebApps.ToString().Equals(Runtime.Host.Authentication.Provider, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The path at which Rest APIs are available
    /// </summary>
    public string RestPath
    {
        get
        {
            if (Runtime is null || Runtime.Rest is null)
            {
                return RestRuntimeOptions.DEFAULT_PATH;
            }
            else
            {
                return Runtime.Rest.Path;
            }
        }
    }

    /// <summary>
    /// The path at which GraphQL API is available
    /// </summary>
    public string GraphQLPath
    {
        get
        {
            if (Runtime is null || Runtime.GraphQL is null)
            {
                return GraphQLRuntimeOptions.DEFAULT_PATH;
            }
            else
            {
                return Runtime.GraphQL.Path;
            }
        }
    }

    /// <summary>
    /// Indicates whether introspection is allowed or not.
    /// </summary>
    public bool AllowIntrospection
    {
        get
        {
            return Runtime is null ||
                Runtime.GraphQL is null ||
                Runtime.GraphQL.AllowIntrospection;
        }
    }

    private string _defaultDataSourceName;

    private Dictionary<string, DataSource> _dataSourceNameToDataSource;

    private Dictionary<string, string> _entityNameToDataSourceName;

    /// <summary>
    /// List of all datasources.
    /// </summary>
    /// <returns>List of datasources</returns>
    public IEnumerable<DataSource> ListAllDataSources()
    {
        return _dataSourceNameToDataSource.Values;
    }

    /// <summary>
    /// Get Iterator to iterate over dictionary.
    /// </summary>
    public IEnumerable<KeyValuePair<string, DataSource>> GetDataSourceNamesToDataSourcesIterator()
    {
        return _dataSourceNameToDataSource.AsEnumerable();
    }

    /// <summary>
    /// Constructor for runtimeConfig.
    /// </summary>
    /// <param name="Schema">schema.</param>
    /// <param name="DataSource">Default datasource.</param>
    /// <param name="Runtime">Runtime settings.</param>
    /// <param name="Entities">Entities</param>
    [JsonConstructor]
    public RuntimeConfig(string? Schema, DataSource DataSource, RuntimeEntities Entities, RuntimeOptions? Runtime = null)
    {
        this.Schema = Schema ?? DEFAULT_CONFIG_SCHEMA_LINK;
        this.DataSource = DataSource;
        this.Runtime = Runtime;
        this.Entities = Entities;
        this._dataSourceNameToDataSource = new Dictionary<string, DataSource>();
        this._defaultDataSourceName = Guid.NewGuid().ToString();
        this._dataSourceNameToDataSource.Add(this._defaultDataSourceName, this.DataSource);

        this._entityNameToDataSourceName = new Dictionary<string, string>();
        foreach (KeyValuePair<string, Entity> entity in Entities)
        {
            _entityNameToDataSourceName.TryAdd(entity.Key, this._defaultDataSourceName);
        }

    }

    /// <summary>
    /// Gets the DataSource corresponding to the datasourceName.
    /// </summary>
    /// <param name="dataSourceName">Name of datasource.</param>
    /// <returns>DataSource object.</returns>
    /// <exception cref="DataApiBuilderException">Not found exception if key is not found.</exception>
    public DataSource GetDataSourceFromDataSourceName(string dataSourceName)
    {
        CheckDataSourceNamePresent(dataSourceName);
        return _dataSourceNameToDataSource[dataSourceName];
    }

    /// <summary>
    /// Tries to add the datasource to the DataSourceNameToDataSource dictionary.
    /// </summary>
    /// <param name="dataSourceName">dataSourceName.</param>
    /// <param name="dataSource">dataSource.</param>
    /// <returns>True indicating success, False indicating failure.</returns>
    public bool AddDataSource(string dataSourceName, DataSource dataSource)
    {
        return _dataSourceNameToDataSource.TryAdd(dataSourceName, dataSource);
    }

    /// <summary>
    /// Updates the DataSourceNameToDataSource dictionary with the new datasource.
    /// </summary>
    /// <param name="dataSourceName">Name of datasource</param>
    /// <param name="dataSource">Updated datasource value.</param>
    /// <exception cref="DataApiBuilderException">Not found exception if key is not found.</exception>
    public void UpdateDataSourceNameToDataSource(string dataSourceName, DataSource dataSource)
    {
        CheckDataSourceNamePresent(dataSourceName);
        _dataSourceNameToDataSource[dataSourceName] = dataSource;
    }

    /// <summary>
    /// Removes the datasource from the DataSourceNameToDataSource dictionary.
    /// </summary>
    /// <param name="dataSourceName">DataSourceName.</param>
    /// <returns>True indicating success, False indicating failure.</returns>
    /// <exception cref="DataApiBuilderException">Not found exception if key is not found.</exception>
    public bool RemoveDataSource(string dataSourceName)
    {
        CheckDataSourceNamePresent(dataSourceName);
        return _dataSourceNameToDataSource.Remove(dataSourceName);
    }

    /// <summary>
    /// Gets datasourceName from EntityNameToDatasourceName dictionary.
    /// </summary>
    /// <param name="entityName">entityName</param>
    /// <returns>DataSourceName</returns>
    public string GetDataSourceNameFromEntityName(string entityName)
    {
        CheckEntityNamePresent(entityName);
        return _entityNameToDataSourceName[entityName];
    }

    /// <summary>
    /// Gets datasource using entityName.
    /// </summary>
    /// <param name="entityName">entityName.</param>
    /// <returns>DataSource using EntityName.</returns>
    public DataSource GetDataSourceFromEntityName(string entityName)
    {
        CheckEntityNamePresent(entityName);
        return _dataSourceNameToDataSource[_entityNameToDataSourceName[entityName]];
    }

    /// <summary>
    /// Add entity to the EntityNameToDataSourceName dictionary.
    /// </summary>
    /// <param name="entityName">EntityName</param>
    /// <param name="dataSourceName">DatasourceName.</param>
    /// <returns>True indicating success, False indicating failure.</returns>
    /// <exception cref="DataApiBuilderException">Not found exception if key is not found.</exception>
    public bool AddEntity(string entityName, string dataSourceName)
    {
        CheckDataSourceNamePresent(dataSourceName);
        return _entityNameToDataSourceName.TryAdd(entityName, dataSourceName);
    }

    /// <summary>
    /// Updates the EntityNameToDataSourceName dictionary with the new Entity to datasource mapping.
    /// </summary>
    /// <param name="entityName">EntityName.</param>
    /// <param name="dataSourceName">dataSourceName.</param>
    /// <exception cref="DataApiBuilderException"></exception>
    public void UpdateEntityNameToDataSourceName(string entityName, string dataSourceName)
    {
        CheckDataSourceNamePresent(dataSourceName);
        CheckEntityNamePresent(entityName);
        _entityNameToDataSourceName[entityName] = dataSourceName;
    }

    /// <summary>
    /// Removes the entity from the EntityNameToDataSourceName dictionary.
    /// </summary>
    /// <param name="entityName">Name of Entity</param>
    /// <exception cref="DataApiBuilderException">Not found exception if key is not found.</exception>
    public bool RemoveEntity(string entityName)
    {
        CheckEntityNamePresent(entityName);
        return _entityNameToDataSourceName.Remove(entityName);
    }

    /// <summary>
    /// Get the default datasource name.
    /// </summary>
    /// <returns>default datasourceName.</returns>
#pragma warning disable CA1024 // Use properties where appropriate. Reason: Do not want datasource serialized and want to keep it private to restrict set;
    public string GetDefaultDataSourceName()
#pragma warning restore CA1024 // Use properties where appropriate
    {
        return _defaultDataSourceName;
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

    public bool IsDevelopmentMode() => Runtime is null || Runtime.Host is null || Runtime.Host.Mode is HostMode.Development;

    private void CheckDataSourceNamePresent(string dataSourceName)
    {
        if (!_dataSourceNameToDataSource.ContainsKey(dataSourceName))
        {
            throw new DataApiBuilderException($"{nameof(dataSourceName)}:{dataSourceName} could not be found within the config", HttpStatusCode.BadRequest, DataApiBuilderException.SubStatusCodes.DataSourceNotFound);
        }
    }

    private void CheckEntityNamePresent(string entityName)
    {
        if (!_entityNameToDataSourceName.ContainsKey(entityName))
        {
            throw new DataApiBuilderException($"{nameof(entityName)}:{entityName} could not be found within the config", HttpStatusCode.BadRequest, DataApiBuilderException.SubStatusCodes.EntityNotFound);
        }
    }
}
