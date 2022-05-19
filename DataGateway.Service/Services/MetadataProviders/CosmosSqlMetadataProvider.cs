using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Threading.Tasks;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Parsers;
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
        private readonly string _connectionString;

        public FilterParser ODataFilterParser => new();

        /// <summary>
        /// Maps an entity name to a DatabaseObject.
        /// </summary>
        public Dictionary<string, DatabaseObject> EntityToDatabaseObject { get; set; } = new(StringComparer.InvariantCultureIgnoreCase);

        public CosmosSqlMetadataProvider(IOptionsMonitor<RuntimeConfigPath> runtimeConfigPath, IFileSystem fileSystem)
        {
            _runtimeConfigPath = runtimeConfigPath;
            _fileSystem = fileSystem;
            runtimeConfigPath.CurrentValue.
                ExtractConfigValues(
                    out _databaseType,
                    out string connectionString,
                    out _entities);
            _connectionString = connectionString;

            CosmosDbOptions? cosmosDb = _runtimeConfigPath.CurrentValue.ConfigValue!.CosmosDb;

            if (cosmosDb is null)
            {
                throw new DataGatewayException("No CosmosDB configuration provided but CosmosDB is the specified database.", System.Net.HttpStatusCode.InternalServerError, DataGatewayException.SubStatusCodes.ErrorInInitialization);
            }

            _cosmosDb = cosmosDb;
        }

        public string GetDatabaseObjectName(string entityName)
        {
            return _cosmosDb.Database;
        }

        public DatabaseType GetDatabaseType()
        {
            return _databaseType;
        }

        public string GetSchemaName(string entityName)
        {
            Entity entity = _entities[entityName];

            return entity.Source switch
            {
                string s when string.IsNullOrEmpty(s) && !string.IsNullOrEmpty(_cosmosDb.Container) => _cosmosDb.Container,
                string s => s,
                _ => throw new DataGatewayException($"No container provided for {entityName}", System.Net.HttpStatusCode.InternalServerError, DataGatewayException.SubStatusCodes.ErrorInInitialization)
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
    }
}
