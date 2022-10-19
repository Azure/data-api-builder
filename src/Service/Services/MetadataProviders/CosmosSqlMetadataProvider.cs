using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Parsers;
using Azure.DataApiBuilder.Service.Resolvers;

namespace Azure.DataApiBuilder.Service.Services.MetadataProviders
{
    public class CosmosSqlMetadataProvider : ISqlMetadataProvider
    {
        private readonly IFileSystem _fileSystem;
        private readonly DatabaseType _databaseType;
        private readonly Dictionary<string, Entity> _entities;
        private CosmosDbOptions _cosmosDb;
        private readonly RuntimeConfig _runtimeConfig;
        private Dictionary<string, string> _partitionKeyPaths = new();

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
                throw new DataApiBuilderException(
                    message: "No CosmosDB configuration provided but CosmosDB is the specified database.",
                    statusCode: System.Net.HttpStatusCode.InternalServerError,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
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
                _ => throw new DataApiBuilderException(
                        message: $"No container provided for {entityName}",
                        statusCode: System.Net.HttpStatusCode.InternalServerError,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization)
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
                string db when string.IsNullOrEmpty(db) && !string.IsNullOrEmpty(_cosmosDb.Database) => _cosmosDb.Database,
                string db when !string.IsNullOrEmpty(db) => db,
                _ => throw new DataApiBuilderException(
                        message: $"No database provided for {entityName}",
                        statusCode: System.Net.HttpStatusCode.InternalServerError,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization)
            };
        }

        public SourceDefinition GetSourceDefinition(string entityName)
        {
            throw new NotSupportedException("Cosmos backends don't support direct table definitions. Definitions are provided via the GraphQL schema");
        }

        public StoredProcedureDefinition GetStoredProcedureDefinition(string entityName)
        {
            // There's a lot of unimplemented methods here, maybe need to rethink the current interface implementation
            throw new NotSupportedException("Cosmos backends (probably) don't support direct stored procedure definitions, either.");
        }

        public Task InitializeAsync()
        {
            return Task.CompletedTask;
        }

        public string GraphQLSchema()
        {
            if (_cosmosDb.GraphQLSchema is null && _fileSystem.File.Exists(_cosmosDb.GraphQLSchemaPath))
            {
                _cosmosDb = _cosmosDb with { GraphQLSchema = _fileSystem.File.ReadAllText(_cosmosDb.GraphQLSchemaPath) };
            }

            if (_cosmosDb.GraphQLSchema is null)
            {
                throw new DataApiBuilderException(
                    "GraphQL Schema isn't set.",
                    System.Net.HttpStatusCode.InternalServerError,
                    DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

            return _cosmosDb.GraphQLSchema;
        }

        public ODataParser GetODataParser()
        {
            throw new NotImplementedException();
        }

        public IQueryBuilder GetQueryBuilder()
        {
            throw new NotImplementedException();
        }

        public bool VerifyForeignKeyExistsInDB(
            DatabaseTable databaseTableA,
            DatabaseTable databaseTableB)
        {
            throw new NotImplementedException();
        }

        public (string, string) ParseSchemaAndDbTableName(string source)
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

        public IDictionary<string, DatabaseObject> GetEntityNamesAndDbObjects()
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

        public bool TryGetEntityNameFromPath(string entityPathName, [NotNullWhen(true)] out string? entityName)
        {
            throw new NotImplementedException();
        }
    }
}
