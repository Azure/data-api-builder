// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
using System.Linq;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Parsers;
using Azure.DataApiBuilder.Service.Resolvers;
using HotChocolate.Language;

namespace Azure.DataApiBuilder.Service.Services.MetadataProviders
{
    public class CosmosSqlMetadataProvider : ISqlMetadataProvider
    {
        private readonly IFileSystem _fileSystem;
        private readonly DatabaseType _databaseType;
        private readonly Dictionary<string, Entity> _entities;
        private CosmosDbNoSqlOptions _cosmosDb;
        private readonly RuntimeConfig _runtimeConfig;
        private Dictionary<string, string> _partitionKeyPaths = new();
        private Dictionary<string, string> _graphQLSingularTypeToEntityNameMap = new();
        private Dictionary<string, List<FieldDefinitionNode>> _graphQLTypeToFieldsMap = new();
        private readonly RuntimeConfigProvider _runtimeConfigProvider;
        public DocumentNode GraphQLSchemaRoot;

        /// <inheritdoc />
        public Dictionary<string, string> GraphQLStoredProcedureExposedNameToEntityNameMap { get; set; } = new();

        /// <inheritdoc />
        public Dictionary<string, DatabaseObject> EntityToDatabaseObject { get; set; } = new(StringComparer.InvariantCultureIgnoreCase);

        public Dictionary<RelationShipPair, ForeignKeyDefinition>? PairToFkDefinition => throw new NotImplementedException();

        public CosmosSqlMetadataProvider(RuntimeConfigProvider runtimeConfigProvider, IFileSystem fileSystem)
        {
            _fileSystem = fileSystem;
            _runtimeConfigProvider = runtimeConfigProvider;
            _runtimeConfig = runtimeConfigProvider.GetRuntimeConfiguration();

            _entities = _runtimeConfig.Entities;
            _databaseType = _runtimeConfig.DatabaseType;
            _graphQLSingularTypeToEntityNameMap = _runtimeConfig.GraphQLSingularTypeToEntityNameMap;
            CosmosDbNoSqlOptions? cosmosDb = _runtimeConfig.DataSource.CosmosDbNoSql;

            if (cosmosDb is null)
            {
                throw new DataApiBuilderException(
                    message: "No CosmosDB configuration provided but CosmosDB is the specified database.",
                    statusCode: System.Net.HttpStatusCode.InternalServerError,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

            _cosmosDb = cosmosDb;
            ParseSchemaGraphQLDocument();

            if (GraphQLSchemaRoot is null)
            {
                throw new DataApiBuilderException(
                    message: "Invalid GraphQL schema was provided for CosmosDB. Please define a valid GraphQL object model in the schema file.",
                    statusCode: System.Net.HttpStatusCode.InternalServerError,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError);
            }

            ParseSchemaGraphQLFieldsForGraphQLType();
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

        /// <summary>
        /// Even though there is no source definition for underlying entity names for
        /// cosmosdb_nosql, we return back an empty source definition required for
        /// graphql filter parser.
        /// </summary>
        /// <param name="entityName"></param>
        public SourceDefinition GetSourceDefinition(string entityName)
        {
            return new SourceDefinition();
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

        public void ParseSchemaGraphQLDocument()
        {
            string graphqlSchema = GraphQLSchema();

            if (string.IsNullOrEmpty(graphqlSchema))
            {
                throw new DataApiBuilderException(
                    message: "No GraphQL object model was provided for CosmosDB. Please define a GraphQL object model and link it in the runtime config.",
                    statusCode: System.Net.HttpStatusCode.InternalServerError,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError);
            }

            GraphQLSchemaRoot = Utf8GraphQLParser.Parse(graphqlSchema);
        }

        private void ParseSchemaGraphQLFieldsForGraphQLType()
        {
            IEnumerable<ObjectTypeDefinitionNode> objectNodes = GraphQLSchemaRoot.Definitions.Where(d => d is ObjectTypeDefinitionNode).Cast<ObjectTypeDefinitionNode>();
            foreach (ObjectTypeDefinitionNode node in objectNodes)
            {
                string typeName = node.Name.Value;
                _graphQLTypeToFieldsMap.TryAdd(typeName, new List<FieldDefinitionNode>());
                foreach (FieldDefinitionNode field in node.Fields)
                {
                    _graphQLTypeToFieldsMap[typeName].Add(field);
                }
            }
        }

        public List<string> GetSchemaGraphQLFieldNamesForEntityName(string entityName)
        {
            List<FieldDefinitionNode>? fields;
            // Check if entity name has a GraphQL object type name alias. If so, fetch GraphQL object type fields with the alias name
            foreach (string typeName in _graphQLSingularTypeToEntityNameMap.Keys)
            {
                if (_graphQLSingularTypeToEntityNameMap[typeName] == entityName && _graphQLTypeToFieldsMap.TryGetValue(typeName, out fields))
                {
                    return fields is null ? new List<string>() : fields.Select(x => x.Name.Value).ToList();
                }
            }

            // Otherwise, entity name is not found
            return new List<string>();
        }

        /// <summary>
        /// Give an entity name and its field name, 
        /// this method is to first look up the GraphQL field type using the entity name,
        /// then find the field type with the entity name and its field name.
        /// </summary>
        /// <param name="entityName">entity name</param>
        /// <param name="fieldName">GraphQL field name</param>
        /// <returns></returns>
        public string? GetSchemaGraphQLFieldTypeFromFieldName(string entityName, string fieldName)
        {
            List<FieldDefinitionNode>? fields;

            // Check if entity name is using alias name, if so, fetch graph type name with the entity alias name
            foreach (string graphQLType in _graphQLSingularTypeToEntityNameMap.Keys)
            {
                if (_graphQLSingularTypeToEntityNameMap[graphQLType] == entityName && _graphQLTypeToFieldsMap.TryGetValue(graphQLType, out fields))
                {
                    return fields is null ? null : fields.Where(x => x.Name.Value == fieldName).FirstOrDefault()?.Type.ToString();
                }
            }

            return null;
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
            name = field;
            return true;
        }

        /// <summary>
        /// Mapped column are not yet supported for Cosmos.
        /// Returns the value of the field provided.
        /// </summary>
        /// <param name="entityName">Name of the entity.</param>
        /// <param name="field">Name of the database field.</param>
        /// <param name="name">Mapped name, which for CosmosDB is the value provided for field."</param>
        /// <returns>True, with out variable set as the value of the input "field" value.</returns>
        public bool TryGetBackingColumn(string entityName, string field, [NotNullWhen(true)] out string? name)
        {
            name = field;
            return true;
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

        /// <inheritdoc />
        public string GetEntityName(string graphQLType)
        {
            if (_entities.ContainsKey(graphQLType))
            {
                return graphQLType;
            }

            if (!_graphQLSingularTypeToEntityNameMap.TryGetValue(graphQLType, out string? entityName))
            {
                throw new DataApiBuilderException(
                    "GraphQL type doesn't match any entity name or singular type in the runtime config.",
                    System.Net.HttpStatusCode.BadRequest,
                    DataApiBuilderException.SubStatusCodes.BadRequest);
            }

            return entityName!;
        }

        /// <inheritdoc />
        public string GetDefaultSchemaName()
        {
            return string.Empty;
        }

        public bool IsDevelopmentMode()
        {
            return _runtimeConfigProvider.IsDeveloperMode();
        }
    }
}
