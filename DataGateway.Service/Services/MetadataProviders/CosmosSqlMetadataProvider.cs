using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Threading.Tasks;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Parsers;
using Azure.DataGateway.Service.Resolvers;
using Microsoft.Extensions.Options;

namespace Azure.DataGateway.Service.Services.MetadataProviders
{
    public class CosmosSqlMetadataProvider : ISqlMetadataProvider
    {
        private readonly IOptionsMonitor<RuntimeConfigPath> _runtimeConfigPath;
        private readonly IFileSystem _fileSystem;
        private readonly DatabaseType _databaseType;
        private readonly Dictionary<string, Entity> _entities;
        private readonly CosmosDbOptions _cosmosDb;
        private Dictionary<string, string> _partitionKeyPaths = new();

        public FilterParser ODataFilterParser => new();

        /// <inheritdoc />
        public Dictionary<string, DatabaseObject> EntityToDatabaseObject { get; set; } = new(StringComparer.InvariantCultureIgnoreCase);

        public CosmosSqlMetadataProvider(IOptionsMonitor<RuntimeConfigPath> runtimeConfigPath, IFileSystem fileSystem)
        {
            _runtimeConfigPath = runtimeConfigPath;
            _fileSystem = fileSystem;
            runtimeConfigPath.CurrentValue.
                ExtractConfigValues(
                    out _databaseType,
                    out _,
                    out _entities);

            CosmosDbOptions? cosmosDb = _runtimeConfigPath.CurrentValue.ConfigValue!.CosmosDb;

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
            throw new NotSupportedException("Cosmos backends don't support direct table definitions. Definitions are provided via the GraphQL schema");
        }

        public Task InitializeAsync()
        {
            return Task.CompletedTask;
        }

        public string GraphQLSchema()
        {
            return _fileSystem.File.ReadAllText(_cosmosDb.GraphQLSchemaPath);
        }

        public FilterParser GetODataFilterParser()
        {
            throw new NotImplementedException();
        }

        public IQueryBuilder GetQueryBuilder()
        {
            throw new NotImplementedException();
        }

        public string? GetPartitionKeyPath(string database, string container)
        {
            _partitionKeyPaths.TryGetValue($"{database}/{container}", out string? partitionKeyPath);
            return partitionKeyPath;
        }

        public void SetPartitionKeyPath(string database, string container, string partitionKeyPath)
        {
            if (!_partitionKeyPaths.TryAdd($"{database}/{container}", partitionKeyPath))
            {
                _partitionKeyPaths[$"{database}/{container}"] = partitionKeyPath;
            }
        }
    }
}
