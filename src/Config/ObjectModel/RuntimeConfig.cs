// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
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
    /// Retrieves the value of runtime.CacheEnabled property if present, default is false.
    /// Caching is enabled only when explicitly set to true.
    /// </summary>
    /// <returns>Whether caching is globally enabled.</returns>
    [JsonIgnore]
    public bool IsCachingEnabled =>
        Runtime is not null &&
        Runtime.IsCachingEnabled;

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

    [JsonIgnore]
    public string DefaultDataSourceName { get; private set; }

    private Dictionary<string, DataSource> _dataSourceNameToDataSource;

    private Dictionary<string, string> _entityNameToDataSourceName = new();

    private Dictionary<string, string> _entityPathNameToEntityName = new();

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

    public bool TryAddEntityPathNameToEntityName(string entityPathName, string entityName)
    {
        return _entityPathNameToEntityName.TryAdd(entityPathName, entityName);
    }

    public bool TryGetEntityNameFromPath(string entityPathName, [NotNullWhen(true)] out string? entityName)
    {
        return _entityPathNameToEntityName.TryGetValue(entityPathName, out entityName);
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
        this.DefaultDataSourceName = Guid.NewGuid().ToString();

        // we will set them up with default values
        _dataSourceNameToDataSource = new Dictionary<string, DataSource>
        {
            { DefaultDataSourceName, this.DataSource }
        };

        _entityNameToDataSourceName = new Dictionary<string, string>();
        foreach (KeyValuePair<string, Entity> entity in Entities)
        {
            _entityNameToDataSourceName.TryAdd(entity.Key, DefaultDataSourceName);
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
                if (loader.TryLoadConfig(dataSourceFile, out RuntimeConfig? config, replaceEnvVar: true))
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
        this.DefaultDataSourceName = DefaultDataSourceName;
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
    /// In a Hot Reload scenario we should maintain the same default data source
    /// name before the hot reload as after the hot reload. This is because we hold
    /// references to the Data Source itself which depend on this data source name
    /// for lookups. To correctly retrieve this information after a hot reload
    /// we need the data source name to stay the same after hot reloading. This method takes
    /// a default data source name, such as the one from before hot reload, and
    /// replaces the current dictionary entries of this RuntimeConfig that were
    /// built using a new, unique guid during the construction of this RuntimeConfig
    /// with entries using the provided default data source name. We then update the DefaultDataSourceName.
    /// </summary>
    /// <param name="initialDefaultDataSourceName">The name used to update the dictionaries.</param>
    public void UpdateDefaultDataSourceName(string initialDefaultDataSourceName)
    {
        _dataSourceNameToDataSource.Remove(DefaultDataSourceName);
        if (!_dataSourceNameToDataSource.TryAdd(initialDefaultDataSourceName, this.DataSource))
        {
            // An exception here means that a default data source name was generated as a GUID that
            // matches the original default data source name. This should never happen but we add this
            // to be extra safe.
            throw new DataApiBuilderException(
                message: $"Duplicate data source name: {initialDefaultDataSourceName}.",
                statusCode: HttpStatusCode.InternalServerError,
                subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError);
        }

        foreach (KeyValuePair<string, Entity> entity in Entities)
        {
            _entityNameToDataSourceName[entity.Key] = initialDefaultDataSourceName;
        }

        DefaultDataSourceName = initialDefaultDataSourceName;
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

    /// <summary>
    /// Returns the ttl-seconds value for a given entity.
    /// If the property is not set, returns the global default value set in the runtime config.
    /// If the global default value is not set, the default value is used (5 seconds).
    /// </summary>
    /// <param name="entityName">Name of the entity to check cache configuration.</param>
    /// <returns>Number of seconds (ttl) that a cache entry should be valid before cache eviction.</returns>
    /// <exception cref="DataApiBuilderException">Raised when an invalid entity name is provided or if the entity has caching disabled.</exception>
    public int GetEntityCacheEntryTtl(string entityName)
    {
        if (!Entities.TryGetValue(entityName, out Entity? entityConfig))
        {
            throw new DataApiBuilderException(
                message: $"{entityName} is not a valid entity.",
                statusCode: HttpStatusCode.BadRequest,
                subStatusCode: DataApiBuilderException.SubStatusCodes.EntityNotFound);
        }

        if (!entityConfig.IsCachingEnabled)
        {
            throw new DataApiBuilderException(
                message: $"{entityName} does not have caching enabled.",
                statusCode: HttpStatusCode.BadRequest,
                subStatusCode: DataApiBuilderException.SubStatusCodes.NotSupported);
        }

        if (entityConfig.Cache.UserProvidedTtlOptions)
        {
            return entityConfig.Cache.TtlSeconds.Value;
        }
        else
        {
            return GlobalCacheEntryTtl();
        }
    }

    /// <summary>
    /// Whether the caching service should be used for a given operation. This is determined by
    /// - whether caching is enabled globally
    /// - whether the datasource is SQL and session context is disabled.
    /// </summary>
    /// <returns>Whether cache operations should proceed.</returns>
    public bool CanUseCache()
    {
        bool setSessionContextEnabled = DataSource.GetTypedOptions<MsSqlOptions>()?.SetSessionContext ?? true;
        return IsCachingEnabled && !setSessionContextEnabled;
    }

    /// <summary>
    /// Returns the ttl-seconds value for the global cache entry.
    /// If no value is explicitly set, returns the global default value.
    /// </summary>
    /// <returns>Number of seconds a cache entry should be valid before cache eviction.</returns>
    public int GlobalCacheEntryTtl()
    {
        return Runtime is not null && Runtime.IsCachingEnabled && Runtime.Cache.UserProvidedTtlOptions
            ? Runtime.Cache.TtlSeconds.Value
            : EntityCacheOptions.DEFAULT_TTL_SECONDS;
    }

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
            (x => x.DatabaseType is DatabaseType.MSSQL || x.DatabaseType is DatabaseType.PostgreSQL || x.DatabaseType is DatabaseType.MySQL || x.DatabaseType is DatabaseType.DWSQL);

        CosmosDataSourceUsed = _dataSourceNameToDataSource.Values.Any
            (x => x.DatabaseType is DatabaseType.CosmosDB_NoSQL);
    }

    /// <summary>
    /// Handles the logic for determining if we are in a scenario where hot reload is possible.
    /// Hot reload is currently not available, and so this will always return false. When hot reload
    /// becomes an available feature this logic will change to reflect the correct state based on
    /// the state of the runtime config and any other relevant factors.
    /// </summary>
    /// <returns>True in a scenario that support hot reload, false otherwise.</returns>
    public static bool IsHotReloadable()
    {
        // always return false while hot reload is not an available feature.
        return false;
    }

    /// <summary>
    /// Helper method to check if multiple create option is supported and enabled.
    /// 
    /// Returns true when
    /// 1. Multiple create operation is supported by the database type and
    /// 2. Multiple create operation is enabled in the runtime config.
    /// 
    /// </summary>
    public bool IsMultipleCreateOperationEnabled()
    {
        return Enum.GetNames(typeof(MultipleCreateSupportingDatabaseType)).Any(x => x.Equals(DataSource.DatabaseType.ToString(), StringComparison.OrdinalIgnoreCase)) &&
               (Runtime is not null &&
               Runtime.GraphQL is not null &&
               Runtime.GraphQL.MultipleMutationOptions is not null &&
               Runtime.GraphQL.MultipleMutationOptions.MultipleCreateOptions is not null &&
               Runtime.GraphQL.MultipleMutationOptions.MultipleCreateOptions.Enabled);
    }

    public uint DefaultPageSize()
    {
        return (uint?)Runtime?.Pagination?.DefaultPageSize ?? PaginationOptions.DEFAULT_PAGE_SIZE;
    }

    public uint MaxPageSize()
    {
        return (uint?)Runtime?.Pagination?.MaxPageSize ?? PaginationOptions.MAX_PAGE_SIZE;
    }

    /// <summary>
    /// Get the pagination limit from the runtime configuration.
    /// </summary>
    /// <param name="first">The pagination input from the user. Example: $first=10</param>
    /// <returns></returns>
    /// <exception cref="DataApiBuilderException"></exception>
    public uint GetPaginationLimit(int? first)
    {
        uint defaultPageSize = this.DefaultPageSize();
        uint maxPageSize = this.MaxPageSize();

        if (first is not null)
        {
            if (first < -1 || first == 0 || first > maxPageSize)
            {
                throw new DataApiBuilderException(
                message: $"Invalid number of items requested, {nameof(first)} argument must be either -1 or a positive number within the max page size limit of {maxPageSize}. Actual value: {first}",
                statusCode: HttpStatusCode.BadRequest,
                subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }
            else
            {
                return (first == -1 ? maxPageSize : (uint)first);
            }
        }
        else
        {
            return defaultPageSize;
        }
    }
}
