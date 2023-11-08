// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO.Abstractions;
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

    public DataSourceFiles? DataSourceFiles { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    public bool CosmosDataSourceUsed { get; private set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    public bool SqlDataSourceUsed { get; private set; }

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
    [JsonIgnore]
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
    [JsonIgnore]
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
    [JsonIgnore]
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
    /// To be used when setting up from cli json scenario.
    /// </summary>
    /// <param name="Schema">schema for config.</param>
    /// <param name="DataSource">Default datasource.</param>
    /// <param name="Entities">Entities</param>
    /// <param name="Runtime">Runtime settings.</param>
    /// <param name="DataSourceFiles">List of datasource files for multiple db scenario. Null for single db scenario.</param>
    [JsonConstructor]
    public RuntimeConfig(string? Schema, DataSource DataSource, RuntimeEntities Entities, RuntimeOptions? Runtime = null, DataSourceFiles? DataSourceFiles = null)
    {
        this.Schema = Schema ?? DEFAULT_CONFIG_SCHEMA_LINK;
        this.DataSource = DataSource;
        this.Runtime = Runtime;
        this.Entities = Entities;
        _defaultDataSourceName = Guid.NewGuid().ToString();

        // we will set them up with default values
        _dataSourceNameToDataSource = new Dictionary<string, DataSource>
        {
            { _defaultDataSourceName, this.DataSource }
        };

        _entityNameToDataSourceName = new Dictionary<string, string>();
        foreach (KeyValuePair<string, Entity> entity in Entities)
        {
            _entityNameToDataSourceName.TryAdd(entity.Key, _defaultDataSourceName);
        }

        // Process data source and entities information for each database in multiple database scenario.
        this.DataSourceFiles = DataSourceFiles;

        if (DataSourceFiles is not null && DataSourceFiles.SourceFiles is not null)
        {
            IEnumerable<KeyValuePair<string, Entity>> allEntities = Entities.AsEnumerable();
            // Iterate through all the datasource files and load the config.
            IFileSystem fileSystem = new FileSystem();
            FileSystemRuntimeConfigLoader loader = new(fileSystem);

            foreach (string dataSourceFile in DataSourceFiles.SourceFiles)
            {
                if (loader.TryLoadConfig(dataSourceFile, out RuntimeConfig? config))
                {
                    try
                    {
                        _dataSourceNameToDataSource = _dataSourceNameToDataSource.Concat(config._dataSourceNameToDataSource).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                        _entityNameToDataSourceName = _entityNameToDataSourceName.Concat(config._entityNameToDataSourceName).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                        allEntities = allEntities.Concat(config.Entities.AsEnumerable());
                    }
                    catch (Exception e)
                    {
                        // Errors could include duplicate datasource names, duplicate entity names, etc.
                        throw new DataApiBuilderException(
                            $"Error while loading datasource file {dataSourceFile} with exception {e.Message}",
                            HttpStatusCode.ServiceUnavailable,
                            DataApiBuilderException.SubStatusCodes.ConfigValidationError,
                            e.InnerException);
                    }
                }
            }

            this.Entities = new RuntimeEntities(allEntities.ToDictionary(x => x.Key, x => x.Value));
        }

        SetupDataSourcesUsed();

    }

    /// <summary>
    /// Constructor for runtimeConfig.
    /// This constructor is to be used when dynamically setting up the config as opposed to using a cli json file.
    /// </summary>
    /// <param name="Schema">schema for config.</param>
    /// <param name="DataSource">Default datasource.</param>
    /// <param name="Runtime">Runtime settings.</param>
    /// <param name="Entities">Entities</param>
    /// <param name="DataSourceFiles">List of datasource files for multiple db scenario.Null for single db scenario.
    /// <param name="DefaultDataSourceName">DefaultDataSourceName to maintain backward compatibility.</param>
    /// <param name="DataSourceNameToDataSource">Dictionary mapping datasourceName to datasource object.</param>
    /// <param name="EntityNameToDataSourceName">Dictionary mapping entityName to datasourceName.</param>
    /// <param name="DataSourceFiles">Datasource files which represent list of child runtimeconfigs for multi-db scenario.</param>
    public RuntimeConfig(string Schema, DataSource DataSource, RuntimeOptions Runtime, RuntimeEntities Entities, string DefaultDataSourceName, Dictionary<string, DataSource> DataSourceNameToDataSource, Dictionary<string, string> EntityNameToDataSourceName, DataSourceFiles? DataSourceFiles = null)
    {
        this.Schema = Schema;
        this.DataSource = DataSource;
        this.Runtime = Runtime;
        this.Entities = Entities;
        _defaultDataSourceName = DefaultDataSourceName;
        _dataSourceNameToDataSource = DataSourceNameToDataSource;
        _entityNameToDataSourceName = EntityNameToDataSourceName;
        this.DataSourceFiles = DataSourceFiles;

        SetupDataSourcesUsed();
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
    /// Validates if datasource is present in runtimeConfig.
    /// </summary>
    public bool CheckDataSourceExists(string dataSourceName)
    {
        return _dataSourceNameToDataSource.ContainsKey(dataSourceName);
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

    public bool IsDevelopmentMode() =>
        Runtime is not null && Runtime.Host is not null
        && Runtime.Host.Mode is HostMode.Development;

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
            throw new DataApiBuilderException(
                message: $"{entityName} is not a valid entity.",
                statusCode: HttpStatusCode.NotFound,
                subStatusCode: DataApiBuilderException.SubStatusCodes.EntityNotFound);
        }
    }

    private void SetupDataSourcesUsed()
    {
        SqlDataSourceUsed = _dataSourceNameToDataSource.Values.Any
            (x => x.DatabaseType is DatabaseType.MSSQL || x.DatabaseType is DatabaseType.PostgreSQL || x.DatabaseType is DatabaseType.MySQL);

        CosmosDataSourceUsed = _dataSourceNameToDataSource.Values.Any
            (x => x.DatabaseType is DatabaseType.CosmosDB_NoSQL);
    }
}
