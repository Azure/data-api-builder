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

    public DataSource DataSource { get; init; }

    public RuntimeOptions Runtime { get; init; }

    public RuntimeEntities Entities { get; init; }

    public DataSourceFiles? DataSourceFiles { get; init; }

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
    /// <param name="DataSourceFiles">List of datasource files for multiple db scenario.</param></param>
    [JsonConstructor]
    public RuntimeConfig(string Schema, DataSource DataSource, RuntimeOptions Runtime, RuntimeEntities Entities, DataSourceFiles? DataSourceFiles = null)
    {
        this.Schema = Schema;
        this.DataSource = DataSource;
        this.Runtime = Runtime;
        this.Entities = Entities;
        this._defaultDataSourceName = Guid.NewGuid().ToString();

        // we will set them up with default values
        this._dataSourceNameToDataSource = new Dictionary<string, DataSource>
        {
            { this._defaultDataSourceName, this.DataSource }
        };

        this._entityNameToDataSourceName = new Dictionary<string, string>();

        foreach (KeyValuePair<string, Entity> entity in Entities)
        {
            _entityNameToDataSourceName.TryAdd(entity.Key, this._defaultDataSourceName);
        }

        // Multiple database scenario.
        this.DataSourceFiles = DataSourceFiles;
        IEnumerable<KeyValuePair<string, Entity>> allEntities = Entities.AsEnumerable();

        if (DataSourceFiles is not null)
        {
            try
            {
                // Iterate through all the datasource files and load the config.
                IFileSystem fileSystem = new FileSystem();
                FileSystemRuntimeConfigLoader loader = new(fileSystem);

                foreach (string dataSourceFile in DataSourceFiles.SourceFiles ?? Enumerable.Empty<string>())
                {
                    if (loader.TryLoadConfig(dataSourceFile, out RuntimeConfig? config))
                    {
                        this._dataSourceNameToDataSource = this._dataSourceNameToDataSource.Concat(config._dataSourceNameToDataSource).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                        this._entityNameToDataSourceName = this._entityNameToDataSourceName.Concat(config._entityNameToDataSourceName).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                        allEntities = allEntities.Concat(config.Entities.ToList());
                    }
                }

                this.Entities = new RuntimeEntities(allEntities.ToDictionary(x => x.Key, x => x.Value));
            }
            catch (Exception e)
            {
                // Errors could include invalid sub file paths, duplicated entity names, etc.
                throw new DataApiBuilderException($"Error while loading datasource files: {e.Message}", HttpStatusCode.BadRequest, DataApiBuilderException.SubStatusCodes.ConfigValidationError);
            }
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
}
