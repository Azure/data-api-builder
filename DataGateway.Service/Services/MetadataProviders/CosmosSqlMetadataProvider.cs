using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Threading.Tasks;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Configurations;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Parsers;
using Azure.DataGateway.Service.Resolvers;

namespace Azure.DataGateway.Service.Services.MetadataProviders
{
    public class CosmosSqlMetadataProvider : ISqlMetadataProvider
    {
        private readonly IFileSystem _fileSystem;
        private readonly DatabaseType _databaseType;
        private readonly Dictionary<string, Entity> _entities;
        private CosmosDbOptions _cosmosDb;
        private readonly RuntimeConfig _runtimeConfig;
        private Dictionary<string, string> _partitionKeyPaths = new();

        public FilterParser ODataFilterParser => new();

        /// <inheritdoc />
        public Dictionary<string, DatabaseObject> EntityToDatabaseObject { get; set; } = new(StringComparer.InvariantCultureIgnoreCase);

        public CosmosSqlMetadataProvider(RuntimeConfigProvider runtimeConfigProvider, IFileSystem fileSystem)
        {
            _fileSystem = fileSystem;
            _runtimeConfig = runtimeConfigProvider.GetRuntimeConfiguration();

            _databaseType = _runtimeConfig.DatabaseType;
            _entities = _runtimeConfig.Entities;

            CosmosDbOptions? cosmosDb = _runtimeConfig.CosmosDb;

            if (cosmosDb is null)
            {
                throw new DataGatewayException(
                    message: "No CosmosDB configuration provided but CosmosDB is the specified database.",
                    statusCode: System.Net.HttpStatusCode.InternalServerError,
                    subStatusCode: DataGatewayException.SubStatusCodes.ErrorInInitialization);
            }

            _cosmosDb = cosmosDb;
        }

        /// <inheritdoc />
        public string GetDatabaseObjectName(string entityName)
        {
            Entity entity = _entities[entityName];

            string entitySource = entity.GetSourceName();

            return entitySource switch
            {
                string s when string.IsNullOrEmpty(s) && !string.IsNullOrEmpty(_cosmosDb.Container) => _cosmosDb.Container,
                string s when !string.IsNullOrEmpty(s) => EntitySourceNamesParser.ParseSchemaAndTable(entitySource).Item2,
                string s => s,
                _ => throw new DataGatewayException(
                        message: $"No container provided for {entityName}",
                        statusCode: System.Net.HttpStatusCode.InternalServerError,
                        subStatusCode: DataGatewayException.SubStatusCodes.ErrorInInitialization)
            };
        }

        /// <inheritdoc />
        public DatabaseType GetDatabaseType()
        {
            return _databaseType;
        }

        /// <inheritdoc />
        public string GetSchemaName(string entityName)
        {
            Entity entity = _entities[entityName];

            string entitySource = entity.GetSourceName();

            if (string.IsNullOrEmpty(entitySource))
            {
                return _cosmosDb.Database;
            }

            (string? database, _) = EntitySourceNamesParser.ParseSchemaAndTable(entitySource);

            return database switch
            {
                string s when string.IsNullOrEmpty(s) && !string.IsNullOrEmpty(_cosmosDb.Container) => _cosmosDb.Database,
                string s => s,
                _ => throw new DataGatewayException(
                        message: $"No container provided for {entityName}",
                        statusCode: System.Net.HttpStatusCode.InternalServerError,
                        subStatusCode: DataGatewayException.SubStatusCodes.ErrorInInitialization)
            };
        }

        public TableDefinition GetTableDefinition(string entityName)
        {
            if (!EntityToDatabaseObject.TryGetValue(entityName, out DatabaseObject? databaseObject))
            {
                throw new InvalidCastException($"Table Definition for {entityName} has not been inferred.");
            }

            return databaseObject!.TableDefinition;
        }

        public Task InitializeAsync()
        {
            GenerateDatabaseObjectForEntities();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Create a DatabaseObject for all the exposed entities.
        /// </summary>
        private void GenerateDatabaseObjectForEntities()
        {
            string schemaName, dbObjectName;
            Dictionary<string, DatabaseObject> sourceObjects = new();
            foreach ((string entityName, Entity entity)
                in _entities)
            {
                if (!EntityToDatabaseObject.ContainsKey(entityName))
                {
                    // Reuse the same Database object for multiple entities if they share the same source.
                    if (!sourceObjects.TryGetValue(entity.GetSourceName(), out DatabaseObject? sourceObject))
                    {
                        // parse source name into a tuple of (schemaName, databaseObjectName)
                        (schemaName, dbObjectName) = ParseSchemaAndDbObjectName(entity.GetSourceName())!;
                        sourceObject = new()
                        {
                            SchemaName = schemaName,
                            Name = dbObjectName,
                            TableDefinition = new()
                        };

                        sourceObjects.Add(entity.GetSourceName(), sourceObject);
                    }

                    EntityToDatabaseObject.Add(entityName, sourceObject);
                }
            }
        }

        /// <summary>
        /// Helper function will parse the schema and database object name
        /// from the provided source string and sort out if a default schema
        /// should be used. It then returns the appropriate schema and
        /// db object name as a tuple of strings.
        /// i.e. source = 'graphqldb.planet' -> databaseName ='graphqldb'; containerName ='planet'
        /// </summary>
        /// <param name="source">source string to parse</param>
        /// <returns></returns>
        /// <exception cref="DataGatewayException"></exception>
        public (string, string) ParseSchemaAndDbObjectName(string source)
        {
            (string? schemaName, string dbObjectName) = EntitySourceNamesParser.ParseSchemaAndTable(source)!;

            if (string.IsNullOrEmpty(schemaName))
            {
                throw new DataGatewayException(message: $"Missing database name for entity name: {source} in Config file for Cosmos",
                                               statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                                               subStatusCode: DataGatewayException.SubStatusCodes.ErrorInInitialization);
            }

            if (string.IsNullOrEmpty(dbObjectName))
            {
                throw new DataGatewayException(message: $"Missing container name for entity name: {source} in Config file for Cosmos",
                                               statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                                               subStatusCode: DataGatewayException.SubStatusCodes.ErrorInInitialization);
            }

            return (schemaName, dbObjectName);
        }

        public string GraphQLSchema()
        {
            if (_cosmosDb.GraphQLSchema is null && _fileSystem.File.Exists(_cosmosDb.GraphQLSchemaPath))
            {
                _cosmosDb = _cosmosDb with { GraphQLSchema = _fileSystem.File.ReadAllText(_cosmosDb.GraphQLSchemaPath) };
            }

            if (_cosmosDb.GraphQLSchema is null)
            {
                throw new DataGatewayException(
                    "GraphQL Schema isn't set.",
                    System.Net.HttpStatusCode.InternalServerError,
                    DataGatewayException.SubStatusCodes.ErrorInInitialization);
            }

            return _cosmosDb.GraphQLSchema;
        }

        public FilterParser GetODataFilterParser()
        {
            throw new NotImplementedException();
        }

        public IQueryBuilder GetQueryBuilder()
        {
            throw new NotImplementedException();
        }

        public bool TryGetExposedColumnName(string entityName, string field, out string? name)
        {
            throw new NotImplementedException();
        }

        public bool TryGetBackingColumn(string entityName, string field, out string? name)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<KeyValuePair<string, DatabaseObject>> GetEntityNamesAndDbObjects()
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public string? GetPartitionKeyPath(string database, string container)
        {
            _partitionKeyPaths.TryGetValue($"{database}/{container}", out string? partitionKeyPath);
            return partitionKeyPath;
        }

        /// <inheritdoc />
        public void SetPartitionKeyPath(string database, string container, string partitionKeyPath)
        {
            if (!_partitionKeyPaths.TryAdd($"{database}/{container}", partitionKeyPath))
            {
                _partitionKeyPaths[$"{database}/{container}"] = partitionKeyPath;
            }
        }
    }
}
