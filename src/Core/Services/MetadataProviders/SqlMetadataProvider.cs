// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Parsers;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Service.Exceptions;
using HotChocolate.Language;
using Microsoft.Extensions.Logging;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLNaming;

[assembly: InternalsVisibleTo("Azure.DataApiBuilder.Service.Tests")]
namespace Azure.DataApiBuilder.Core.Services
{
    /// <summary>
    /// Reads schema information from the database to make it
    /// available for the GraphQL/REST services.
    /// </summary>
    public abstract class SqlMetadataProvider<ConnectionT, DataAdapterT, CommandT> : ISqlMetadataProvider
        where ConnectionT : DbConnection, new()
        where DataAdapterT : DbDataAdapter, new()
        where CommandT : DbCommand, new()
    {
        private ODataParser _oDataParser = new();

        private readonly DatabaseType _databaseType;

        // Represents the entities exposed in the runtime config.
        private IReadOnlyDictionary<string, Entity> _entities;

        // Represents the linking entities created by DAB to support multiple mutations for entities having an M:N relationship between them.
        protected Dictionary<string, Entity> _linkingEntities = new();

        protected readonly string _dataSourceName;

        // Dictionary containing mapping of graphQL stored procedure exposed query/mutation name
        // to their corresponding entity names defined in the config.
        public Dictionary<string, string> GraphQLStoredProcedureExposedNameToEntityNameMap { get; set; } = new();

        // Contains all the referencing and referenced columns for each pair
        // of referencing and referenced tables.
        public Dictionary<RelationShipPair, ForeignKeyDefinition>? PairToFkDefinition { get; set; }

        protected IQueryExecutor QueryExecutor { get; }

        protected const int NUMBER_OF_RESTRICTIONS = 4;

        protected string ConnectionString { get; init; }

        protected IQueryBuilder SqlQueryBuilder { get; init; }

        protected DataSet EntitiesDataSet { get; init; }

        private RuntimeConfigProvider _runtimeConfigProvider;

        private Dictionary<string, Dictionary<string, string>> EntityBackingColumnsToExposedNames { get; } = new();

        private Dictionary<string, Dictionary<string, string>> EntityExposedNamesToBackingColumnNames { get; } = new();

        private Dictionary<string, string> EntityPathToEntityName { get; } = new();

        protected IAbstractQueryManagerFactory QueryManagerFactory { get; init; }

        /// <summary>
        /// Maps an entity name to a DatabaseObject.
        /// </summary>
        public Dictionary<string, DatabaseObject> EntityToDatabaseObject { get; set; } =
            new(StringComparer.InvariantCulture);

        protected readonly ILogger<ISqlMetadataProvider> _logger;

        public readonly bool _isValidateOnly;
        public List<Exception> SqlMetadataExceptions { get; private set; } = new();

        private void HandleOrRecordException(Exception e)
        {
            if (_isValidateOnly)
            {
                SqlMetadataExceptions.Add(e);
            }
            else
            {
                throw e;
            }
        }

        public SqlMetadataProvider(
            RuntimeConfigProvider runtimeConfigProvider,
            IAbstractQueryManagerFactory engineFactory,
            ILogger<ISqlMetadataProvider> logger,
            string dataSourceName,
            bool isValidateOnly = false)
        {
            RuntimeConfig runtimeConfig = runtimeConfigProvider.GetConfig();
            _runtimeConfigProvider = runtimeConfigProvider;
            _dataSourceName = dataSourceName;
            _databaseType = runtimeConfig.GetDataSourceFromDataSourceName(dataSourceName).DatabaseType;
            _entities = runtimeConfig.Entities.Where(x => string.Equals(runtimeConfig.GetDataSourceNameFromEntityName(x.Key), _dataSourceName, StringComparison.OrdinalIgnoreCase)).ToDictionary(x => x.Key, x => x.Value);
            _logger = logger;
            _isValidateOnly = isValidateOnly;
            foreach ((string entityName, Entity entityMetatdata) in _entities)
            {
                if (runtimeConfig.IsRestEnabled)
                {
                    string restPath = entityMetatdata.Rest?.Path ?? entityName;
                    _logger.LogInformation("[{entity}] REST path: {globalRestPath}/{entityRestPath}", entityName, runtimeConfig.RestPath, restPath);
                }
                else
                {
                    _logger.LogInformation(message: "REST calls are disabled for the entity: {entity}", entityName);
                }
            }

            ConnectionString = runtimeConfig.GetDataSourceFromDataSourceName(dataSourceName).ConnectionString;
            EntitiesDataSet = new();
            QueryManagerFactory = engineFactory;
            SqlQueryBuilder = QueryManagerFactory.GetQueryBuilder(_databaseType);
            QueryExecutor = QueryManagerFactory.GetQueryExecutor(_databaseType);
        }

        /// <inheritdoc />
        public ODataParser GetODataParser()
        {
            return _oDataParser;
        }

        /// <inheritdoc />
        public DatabaseType GetDatabaseType()
        {
            return _databaseType;
        }

        /// <summary>
        /// Obtains the underlying query builder.
        /// </summary>
        /// <returns></returns>
        public IQueryBuilder GetQueryBuilder()
        {
            return SqlQueryBuilder;
        }

        /// <inheritdoc />
        public virtual string GetSchemaName(string entityName)
        {
            if (!EntityToDatabaseObject.TryGetValue(entityName, out DatabaseObject? databaseObject))
            {
                throw new DataApiBuilderException(message: $"Table Definition for {entityName} has not been inferred.",
                    statusCode: HttpStatusCode.InternalServerError,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.EntityNotFound);
            }

            return databaseObject!.SchemaName;
        }

        /// <summary>
        /// Gets the database name. This method is only relevant for MySql where the terms schema and database are used interchangeably.
        /// </summary>
        public virtual string GetDatabaseName() => string.Empty;

        /// <inheritdoc />
        public string GetDatabaseObjectName(string entityName)
        {
            if (!EntityToDatabaseObject.TryGetValue(entityName, out DatabaseObject? databaseObject))
            {
                throw new DataApiBuilderException(message: $"Table Definition for {entityName} has not been inferred.",
                    statusCode: HttpStatusCode.InternalServerError,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.EntityNotFound);
            }

            return databaseObject!.Name;
        }

        /// <inheritdoc />
        public SourceDefinition GetSourceDefinition(string entityName)
        {
            if (!EntityToDatabaseObject.TryGetValue(entityName, out DatabaseObject? databaseObject))
            {
                throw new DataApiBuilderException(message: $"Table Definition for {entityName} has not been inferred.",
                    statusCode: HttpStatusCode.InternalServerError,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.EntityNotFound);
            }

            return databaseObject.SourceDefinition;
        }

        /// <inheritdoc />
        public StoredProcedureDefinition GetStoredProcedureDefinition(string entityName)
        {
            if (!EntityToDatabaseObject.TryGetValue(entityName, out DatabaseObject? databaseObject))
            {
                throw new DataApiBuilderException(message: $"Stored Procedure Definition for {entityName} has not been inferred.",
                    statusCode: HttpStatusCode.InternalServerError,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.EntityNotFound);
            }

            return ((DatabaseStoredProcedure)databaseObject).StoredProcedureDefinition;
        }

        /// <inheritdoc />
        public bool TryGetExposedColumnName(string entityName, string backingFieldName, [NotNullWhen(true)] out string? name)
        {
            return EntityBackingColumnsToExposedNames[entityName].TryGetValue(backingFieldName, out name);
        }

        /// <inheritdoc />
        public bool TryGetBackingColumn(string entityName, string field, [NotNullWhen(true)] out string? name)
        {
            return EntityExposedNamesToBackingColumnNames[entityName].TryGetValue(field, out name);
        }

        /// <inheritdoc />
        public virtual bool TryGetEntityNameFromPath(string entityPathName, [NotNullWhen(true)] out string? entityName)
        {
            return EntityPathToEntityName.TryGetValue(entityPathName, out entityName);
        }

        /// <inheritdoc />
        public IReadOnlyDictionary<string, DatabaseObject> GetEntityNamesAndDbObjects()
        {
            return EntityToDatabaseObject;
        }

        /// <inheritdoc />
        public string GetEntityName(string graphQLType)
        {
            if (_entities.ContainsKey(graphQLType))
            {
                return graphQLType;
            }

            foreach ((string entityName, Entity entity) in _entities)
            {
                if (entity.GraphQL.Singular == graphQLType)
                {
                    return entityName;
                }
            }

            throw new DataApiBuilderException(
                "GraphQL type doesn't match any entity name or singular type in the runtime config.",
                HttpStatusCode.BadRequest,
                DataApiBuilderException.SubStatusCodes.BadRequest);
        }

        /// <inheritdoc />
        public async Task InitializeAsync()
        {
            System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();
            GenerateDatabaseObjectForEntities();
            if (_isValidateOnly)
            {
                // Currently Validate mode only support single datasource,
                // so using the below validation we can check connection once instead of checking for each entity.
                // To enable to check for multiple data-sources just remove this validation and each entity will have its own connection check.
                try
                {
                    await ValidateDatabaseConnection();
                }
                catch (DataApiBuilderException e)
                {
                    HandleOrRecordException(e);
                    return;
                }
            }

            await PopulateObjectDefinitionForEntities();
            GenerateExposedToBackingColumnMapsForEntities();
            // When IsLateConfigured is true we are in a hosted scenario and do not reveal primary key information.
            if (!_runtimeConfigProvider.IsLateConfigured)
            {
                LogPrimaryKeys();
            }

            GenerateRestPathToEntityMap();
            InitODataParser();
            timer.Stop();
            _logger.LogTrace($"Done inferring Sql database schema in {timer.ElapsedMilliseconds}ms.");
        }

        /// <inheritdoc />
        public void InitializeAsync(
            Dictionary<string, DatabaseObject> entityToDatabaseObject,
            Dictionary<string, string> graphQLStoredProcedureExposedNameToEntityNameMap)
        {
            EntityToDatabaseObject = entityToDatabaseObject ?? EntityToDatabaseObject;
            GraphQLStoredProcedureExposedNameToEntityNameMap = graphQLStoredProcedureExposedNameToEntityNameMap ?? GraphQLStoredProcedureExposedNameToEntityNameMap;
            GenerateExposedToBackingColumnMapsForEntities();
        }

        /// <inheritdoc/>
        public bool TryGetExposedFieldToBackingFieldMap(string entityName, [NotNullWhen(true)] out IReadOnlyDictionary<string, string>? mappings)
        {
            Dictionary<string, string>? entityToColumnMappings;
            mappings = null;
            if (EntityExposedNamesToBackingColumnNames.TryGetValue(entityName, out entityToColumnMappings))
            {
                mappings = entityToColumnMappings;
                return true;
            }

            return false;
        }

        /// <inheritdoc/>
        public bool TryGetBackingFieldToExposedFieldMap(string entityName, [NotNullWhen(true)] out IReadOnlyDictionary<string, string>? mappings)
        {
            Dictionary<string, string>? columntoEntityMappings;
            mappings = null;
            if (EntityBackingColumnsToExposedNames.TryGetValue(entityName, out columntoEntityMappings))
            {
                mappings = columntoEntityMappings;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Log Primary key information. Function only called when not
        /// in a hosted scenario. Log relevant information about Primary keys
        /// including backing and exposed names, type, isNullable, and isAutoGenerated.
        /// </summary>
        private void LogPrimaryKeys()
        {
            ColumnDefinition column;
            foreach ((string entityName, Entity _) in _entities)
            {
                try
                {
                    SourceDefinition sourceDefinition = GetSourceDefinition(entityName);
                    _logger.LogDebug("Logging primary key information for entity: {entityName}.", entityName);
                    foreach (string pK in sourceDefinition.PrimaryKey)
                    {
                        column = sourceDefinition.Columns[pK];
                        if (TryGetExposedColumnName(entityName, pK, out string? exposedPKeyName))
                        {
                            _logger.LogDebug(
                                message: "Primary key column name: {pK}\n" +
                                "      Primary key mapped name: {exposedPKeyName}\n" +
                                "      Type: {column.SystemType.Name}\n" +
                                "      IsNullable: {column.IsNullable}\n" +
                                "      IsAutoGenerated: {column.IsAutoGenerated}",
                                pK,
                                exposedPKeyName,
                                column.SystemType.Name,
                                column.IsNullable,
                                column.IsAutoGenerated);
                        }
                    }
                }
                catch (Exception ex)
                {
                    HandleOrRecordException(new DataApiBuilderException(
                        message: $"Failed to log primary key information for entity: {entityName} due to: {ex.Message}",
                        innerException: ex,
                        statusCode: HttpStatusCode.InternalServerError,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization));
                }
            }
        }

        /// <summary>
        /// Verify that the stored procedure exists in the database schema, then populate its database object parameters accordingly
        /// </summary>
        protected virtual async Task FillSchemaForStoredProcedureAsync(
            Entity procedureEntity,
            string entityName,
            string schemaName,
            string storedProcedureSourceName,
            StoredProcedureDefinition storedProcedureDefinition)
        {
            using ConnectionT conn = new();
            conn.ConnectionString = ConnectionString;
            DataTable procedureMetadata;
            string[] procedureRestrictions = new string[NUMBER_OF_RESTRICTIONS];

            try
            {
                await QueryExecutor.SetManagedIdentityAccessTokenIfAnyAsync(conn, _dataSourceName);
                await conn.OpenAsync();

                // To restrict the parameters for the current stored procedure, specify its name
                procedureRestrictions[0] = conn.Database;
                procedureRestrictions[1] = schemaName;
                procedureRestrictions[2] = storedProcedureSourceName;

                procedureMetadata = await conn.GetSchemaAsync(collectionName: "Procedures", restrictionValues: procedureRestrictions);
            }
            catch (Exception ex)
            {
                string message = $"Cannot obtain Schema for entity {entityName} " +
                            $"with underlying database object source: {schemaName}.{storedProcedureSourceName} " +
                            $"due to: {ex.Message}";

                throw new DataApiBuilderException(
                    message: message,
                    innerException: ex,
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

            // Stored procedure does not exist in DB schema
            if (procedureMetadata.Rows.Count == 0)
            {
                throw new DataApiBuilderException(
                    message: $"No stored procedure definition found for the given database object {storedProcedureSourceName}",
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

            // Each row in the procedureParams DataTable corresponds to a single parameter
            DataTable parameterMetadata = await conn.GetSchemaAsync(collectionName: "ProcedureParameters", restrictionValues: procedureRestrictions);

            // For each row/parameter, add an entry to StoredProcedureDefinition.Parameters dictionary
            foreach (DataRow row in parameterMetadata.Rows)
            {
                // row["DATA_TYPE"] has value type string so a direct cast to System.Type is not supported.
                // See https://learn.microsoft.com/en-us/dotnet/framework/data/adonet/sql-server-data-type-mappings
                string sqlType = (string)row["DATA_TYPE"];
                Type systemType = SqlToCLRType(sqlType);
                ParameterDefinition paramDefinition = new()
                {
                    SystemType = systemType,
                    DbType = TypeHelper.GetDbTypeFromSystemType(systemType)
                };

                // Add to parameters dictionary without the leading @ sign
                storedProcedureDefinition.Parameters.TryAdd(((string)row["PARAMETER_NAME"])[1..], paramDefinition);
            }

            // Loop through parameters specified in config, throw error if not found in schema
            // else set runtime config defined default values.
            // Note: we defer type checking of parameters specified in config until request time
            Dictionary<string, object>? configParameters = procedureEntity.Source.Parameters;
            if (configParameters is not null)
            {
                foreach ((string configParamKey, object configParamValue) in configParameters)
                {
                    if (!storedProcedureDefinition.Parameters.TryGetValue(configParamKey, out ParameterDefinition? parameterDefinition))
                    {
                        HandleOrRecordException(new DataApiBuilderException(
                            message: $"Could not find parameter \"{configParamKey}\" specified in config for procedure \"{schemaName}.{storedProcedureSourceName}\"",
                            statusCode: HttpStatusCode.ServiceUnavailable,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization));
                    }
                    else
                    {
                        parameterDefinition.HasConfigDefault = true;
                        parameterDefinition.ConfigDefaultValue = configParamValue?.ToString();
                    }
                }
            }

            // Generating exposed stored-procedure query/mutation name and adding to the dictionary mapping it to its entity name.
            GraphQLStoredProcedureExposedNameToEntityNameMap.TryAdd(GenerateStoredProcedureGraphQLFieldName(entityName, procedureEntity), entityName);
        }

        /// <summary>
        /// Takes a string version of a sql data type and returns its .NET common language runtime (CLR) counterpart
        /// </summary>
        public abstract Type SqlToCLRType(string sqlType);

        /// <summary>
        /// Updates a table's SourceDefinition object's metadata with whether any enabled insert/update DML triggers exist for the table.
        /// This method is only called for tables in MsSql.
        /// </summary>
        /// <param name="entityName">Name of the entity.</param>
        /// <param name="schemaName">Name of the schema in which the table is present.</param>
        /// <param name="tableName">Name of the table.</param>
        /// <param name="sourceDefinition">Table definition to update.</param>
        public virtual Task PopulateTriggerMetadataForTable(string entityName, string schemaName, string tableName, SourceDefinition sourceDefinition)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Generates the map used to find a given entity based
        /// on the path that will be used for that entity.
        /// </summary>
        private void GenerateRestPathToEntityMap()
        {
            RuntimeConfig runtimeConfig = _runtimeConfigProvider.GetConfig();
            string graphQLGlobalPath = runtimeConfig.GraphQLPath;

            foreach ((string entityName, Entity entity) in _entities)
            {
                try
                {
                    string path = GetEntityPath(entity, entityName).TrimStart('/');
                    ValidateEntityAndGraphQLPathUniqueness(path, graphQLGlobalPath);

                    if (!string.IsNullOrEmpty(path))
                    {
                        EntityPathToEntityName[path] = entityName;
                    }
                }
                catch (Exception e)
                {
                    HandleOrRecordException(e);
                }
            }
        }

        /// <summary>
        /// Validate that an Entity's REST path does not conflict with the developer configured
        /// or the internal default GraphQL path (/graphql).
        /// </summary>
        /// <param name="path">Entity's calculated REST path.</param>
        /// <param name="graphQLGlobalPath">Developer configured GraphQL Path</param>
        /// <exception cref="DataApiBuilderException"></exception>
        public void ValidateEntityAndGraphQLPathUniqueness(string path, string graphQLGlobalPath)
        {
            // Handle case when path does not have forward slash (/) prefix
            // by adding one if not present or ignoring an existing slash.
            // entityName -> /entityName
            // /entityName -> /entityName (no change)
            if (!string.IsNullOrWhiteSpace(path) && path[0] != '/')
            {
                path = '/' + path;
            }

            if (string.Equals(path, graphQLGlobalPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(path, GraphQLRuntimeOptions.DEFAULT_PATH, StringComparison.OrdinalIgnoreCase))
            {
                HandleOrRecordException(new DataApiBuilderException(
                    message: "Entity's REST path conflicts with GraphQL reserved paths.",
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError));
            }
        }

        /// <summary>
        /// Deserialize and return the entity's path.
        /// </summary>
        /// <param name="entity">Entity object to get the path of.</param>
        /// <param name="entityName">name of the entity</param>
        /// <returns>route for the given Entity.</returns>
        private static string GetEntityPath(Entity entity, string entityName)
        {
            // if entity.Rest is null or it's enabled without a custom path, return the entity name
            if (entity.Rest is null || (entity.Rest.Enabled && string.IsNullOrEmpty(entity.Rest.Path)))
            {
                return entityName;
            }

            // for false return empty string so we know not to add in caller
            if (!entity.Rest.Enabled)
            {
                return string.Empty;
            }

            // otherwise return the custom path
            return entity.Rest.Path!;
        }

        /// <summary>
        /// Returns the default schema name. Throws exception here since
        /// each derived class should override this method.
        /// </summary>
        /// <exception cref="NotSupportedException"></exception>
        public virtual string GetDefaultSchemaName()
        {
            throw new NotSupportedException($"Cannot get default schema " +
                $"name for database type {_databaseType}");
        }

        /// <summary>
        /// Creates a Database object with the given schema and table names.
        /// </summary>
        protected virtual DatabaseTable GenerateDbTable(string schemaName, string tableName)
        {
            return new(schemaName, tableName);
        }

        /// <summary>
        /// Builds the dictionary of parameters and their values required for the
        /// foreign key query.
        /// </summary>
        /// <param name="schemaNames"></param>
        /// <param name="tableNames"></param>
        /// <returns>The dictionary populated with parameters.</returns>
        protected virtual Dictionary<string, DbConnectionParam>
            GetForeignKeyQueryParams(
                string[] schemaNames,
                string[] tableNames)
        {
            Dictionary<string, DbConnectionParam> parameters = new();
            string[] schemaNameParams =
                BaseSqlQueryBuilder.CreateParams(
                    kindOfParam: BaseSqlQueryBuilder.SCHEMA_NAME_PARAM,
                    schemaNames.Count());
            string[] tableNameParams =
                BaseSqlQueryBuilder.CreateParams(
                    kindOfParam: BaseSqlQueryBuilder.TABLE_NAME_PARAM,
                    tableNames.Count());

            for (int i = 0; i < schemaNames.Count(); ++i)
            {
                parameters.Add(schemaNameParams[i], new(schemaNames[i], DbType.String));
            }

            for (int i = 0; i < tableNames.Count(); ++i)
            {
                parameters.Add(tableNameParams[i], new(tableNames[i], DbType.String));
            }

            return parameters;
        }

        /// <summary>
        /// Create a DatabaseObject for all the exposed entities.
        /// </summary>
        private void GenerateDatabaseObjectForEntities()
        {
            Dictionary<string, DatabaseObject> sourceObjects = new();
            foreach ((string entityName, Entity entity) in _entities)
            {
                PopulateDatabaseObjectForEntity(entity, entityName, sourceObjects);
            }
        }

        protected void PopulateDatabaseObjectForEntity(
            Entity entity,
            string entityName,
            Dictionary<string, DatabaseObject> sourceObjects)
        {
            try
            {
                EntitySourceType sourceType = GetEntitySourceType(entityName, entity);
                if (!EntityToDatabaseObject.ContainsKey(entityName))
                {
                    if (entity.Source.Object is null)
                    {
                        throw new DataApiBuilderException(
                            message: $"The entity {entityName} does not have a valid source object.",
                            statusCode: HttpStatusCode.InternalServerError,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError);
                    }

                    // Reuse the same Database object for multiple entities if they share the same source.
                    if (!sourceObjects.TryGetValue(entity.Source.Object, out DatabaseObject? sourceObject))
                    {
                        // parse source name into a tuple of (schemaName, databaseObjectName)
                        (string schemaName, string dbObjectName) = ParseSchemaAndDbTableName(entity.Source.Object)!;

                        // if specified as stored procedure in config,
                        // initialize DatabaseObject as DatabaseStoredProcedure,
                        // else with DatabaseTable (for tables) / DatabaseView (for views).

                        if (sourceType is EntitySourceType.StoredProcedure)
                        {
                            sourceObject = new DatabaseStoredProcedure(schemaName, dbObjectName)
                            {
                                SourceType = sourceType,
                                StoredProcedureDefinition = new()
                            };
                        }
                        else if (sourceType is EntitySourceType.Table)
                        {
                            sourceObject = new DatabaseTable()
                            {
                                SchemaName = schemaName,
                                Name = dbObjectName,
                                SourceType = sourceType,
                                TableDefinition = new()
                            };
                        }
                        else
                        {
                            sourceObject = new DatabaseView(schemaName, dbObjectName)
                            {
                                SchemaName = schemaName,
                                Name = dbObjectName,
                                SourceType = sourceType,
                                ViewDefinition = new()
                            };
                        }

                        sourceObjects.Add(entity.Source.Object, sourceObject);
                    }

                    EntityToDatabaseObject.Add(entityName, sourceObject);

                    if (entity.Relationships is not null && entity.Source.Type is EntitySourceType.Table)
                    {
                        ProcessRelationships(entityName, entity, (DatabaseTable)sourceObject, sourceObjects);
                    }
                }
            }
            catch (Exception e)
            {
                HandleOrRecordException(e);
            }
        }

        /// <summary>
        /// Get the EntitySourceType for the given entity or throw an exception if it is null.
        /// </summary>
        /// <param name="entityName">Name of the entity, used to provide info if an error is raised.</param>
        /// <param name="entity">Entity to get the source type from.</param>
        /// <returns>The non-nullable EntitySourceType.</returns>
        /// <exception cref="DataApiBuilderException">If the EntitySourceType is null raise an exception as it is required for a SQL entity.</exception>
        private static EntitySourceType GetEntitySourceType(string entityName, Entity entity)
        {
            return entity.Source.Type ??
                                throw new DataApiBuilderException(
                                    $"The entity {entityName} does not have a source type. A null source type is only valid if the database type is CosmosDB_NoSQL.",
                                    statusCode: HttpStatusCode.ServiceUnavailable,
                                    subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError);
        }

        /// <summary>
        /// Adds a foreign key definition for each of the nested entities
        /// specified in the relationships section of this entity
        /// to gather the referencing and referenced columns from the database at a later stage.
        /// Sets the referencing and referenced tables based on the kind of relationship.
        /// If encounter a linking object, use that as the referencing table
        /// for the foreign key definition.
        /// There may not be a foreign key defined on the backend in which case
        /// the relationship.source.fields and relationship.target fields are mandatory.
        /// Initializing a definition here is an indication to find the foreign key
        /// between the referencing and referenced tables.
        /// </summary>
        /// <param name="entityName"></param>
        /// <param name="entity"></param>
        /// <param name="databaseTable"></param>
        /// <exception cref="InvalidOperationException"></exception>
        private void ProcessRelationships(
            string entityName,
            Entity entity,
            DatabaseTable databaseTable,
            Dictionary<string, DatabaseObject> sourceObjects)
        {
            SourceDefinition sourceDefinition = GetSourceDefinition(entityName);
            if (!sourceDefinition.SourceEntityRelationshipMap
                .TryGetValue(entityName, out RelationshipMetadata? relationshipData))
            {
                relationshipData = new();
                sourceDefinition.SourceEntityRelationshipMap.Add(entityName, relationshipData);
            }

            string targetSchemaName, targetDbTableName, linkingTableSchema, linkingTableName;
            foreach (EntityRelationship relationship in entity.Relationships!.Values)
            {
                string targetEntityName = relationship.TargetEntity;
                if (!_entities.TryGetValue(targetEntityName, out Entity? targetEntity))
                {
                    throw new InvalidOperationException($"Target Entity {targetEntityName} should be one of the exposed entities.");
                }

                if (targetEntity.Source.Object is null)
                {
                    throw new DataApiBuilderException(
                                message: $"Target entity {entityName} does not have a valid source object.",
                                statusCode: HttpStatusCode.InternalServerError,
                                subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError);
                }

                (targetSchemaName, targetDbTableName) = ParseSchemaAndDbTableName(targetEntity.Source.Object)!;
                DatabaseTable targetDbTable = new(targetSchemaName, targetDbTableName);
                // If a linking object is specified,
                // give that higher preference and add two foreign keys for this targetEntity.
                if (relationship.LinkingObject is not null)
                {
                    (linkingTableSchema, linkingTableName) = ParseSchemaAndDbTableName(relationship.LinkingObject)!;
                    DatabaseTable linkingDbTable = new(linkingTableSchema, linkingTableName);
                    AddForeignKeyForTargetEntity(
                        targetEntityName,
                        referencingDbTable: linkingDbTable,
                        referencedDbTable: databaseTable,
                        referencingColumns: relationship.LinkingSourceFields,
                        referencedColumns: relationship.SourceFields,
                        relationshipData);

                    AddForeignKeyForTargetEntity(
                        targetEntityName,
                        referencingDbTable: linkingDbTable,
                        referencedDbTable: targetDbTable,
                        referencingColumns: relationship.LinkingTargetFields,
                        referencedColumns: relationship.TargetFields,
                        relationshipData);

                    RuntimeConfig runtimeConfig = _runtimeConfigProvider.GetConfig();

                    // Populating metadata for linking object is only required when multiple create operation is enabled and those database types that support multiple create operation.
                    if (runtimeConfig.IsMultipleCreateOperationEnabled())
                    {
                        // When a linking object is encountered for a database table, we will create a linking entity for the object.
                        // Subsequently, we will also populate the Database object for the linking entity. This is used to infer
                        // metadata about linking object needed to create GQL schema for multiple insertions.
                        if (entity.Source.Type is EntitySourceType.Table)
                        {
                            PopulateMetadataForLinkingObject(
                                entityName: entityName,
                                targetEntityName: targetEntityName,
                                linkingObject: relationship.LinkingObject,
                                sourceObjects: sourceObjects);
                        }
                    }
                }
                else if (relationship.Cardinality == Cardinality.One)
                {
                    // For Many-One OR One-One Relationships, optimistically
                    // add foreign keys from either sides in the hopes of finding their metadata
                    // at a later stage when we query the database about foreign keys.
                    // Both or either of these may be present if its a One-One relationship,
                    // The second fk would not be present if its a Many-One relationship.
                    // When the configuration file doesn't specify how to relate these entities,
                    // at least 1 of the following foreign keys should be present.

                    // Adding this foreign key in the hopes of finding a foreign key
                    // in the underlying database object of the source entity referencing
                    // the target entity.
                    // This foreign key may NOT exist for either of the following reasons:
                    // a. this source entity is related to the target entity in an One-to-One relationship
                    // but the foreign key was added to the target entity's underlying source
                    // This is covered by the foreign key below.
                    // OR
                    // b. no foreign keys were defined at all.
                    AddForeignKeyForTargetEntity(
                        targetEntityName,
                        referencingDbTable: databaseTable,
                        referencedDbTable: targetDbTable,
                        referencingColumns: relationship.SourceFields,
                        referencedColumns: relationship.TargetFields,
                        relationshipData);

                    // Adds another foreign key definition with targetEntity.GetSourceName()
                    // as the referencingTableName - in the situation of a One-to-One relationship
                    // and the foreign key is defined in the source of targetEntity.
                    // This foreign key WILL NOT exist if its a Many-One relationship.
                    AddForeignKeyForTargetEntity(
                        targetEntityName,
                        referencingDbTable: targetDbTable,
                        referencedDbTable: databaseTable,
                        referencingColumns: relationship.TargetFields,
                        referencedColumns: relationship.SourceFields,
                        relationshipData);
                }
                else if (relationship.Cardinality is Cardinality.Many)
                {
                    // Case of publisher(One)-books(Many)
                    // we would need to obtain the foreign key information from the books table
                    // about the publisher id so we can do the join.
                    // so, the referencingTable is the source of the target entity.
                    AddForeignKeyForTargetEntity(
                        targetEntityName,
                        referencingDbTable: targetDbTable,
                        referencedDbTable: databaseTable,
                        referencingColumns: relationship.TargetFields,
                        referencedColumns: relationship.SourceFields,
                        relationshipData);
                }
            }
        }

        /// <summary>
        /// Helper method to create a linking entity and a database object for the given linking object (which relates the source and target with an M:N relationship).
        /// The created linking entity and its corresponding database object definition is later used during GraphQL schema generation
        /// to enable multiple mutations.
        /// </summary>
        /// <param name="entityName">Source entity name.</param>
        /// <param name="targetEntityName">Target entity name.</param>
        /// <param name="linkingObject">Linking object</param>
        /// <param name="sourceObjects">Dictionary storing a collection of database objects which have been created.</param>
        protected virtual void PopulateMetadataForLinkingObject(
            string entityName,
            string targetEntityName,
            string linkingObject,
            Dictionary<string, DatabaseObject> sourceObjects)
        {
            return;
        }

        /// <summary>
        /// Adds a new foreign key definition for the target entity
        /// in the relationship metadata.
        /// </summary>
        private static void AddForeignKeyForTargetEntity(
            string targetEntityName,
            DatabaseTable referencingDbTable,
            DatabaseTable referencedDbTable,
            string[]? referencingColumns,
            string[]? referencedColumns,
            RelationshipMetadata relationshipData)
        {
            ForeignKeyDefinition foreignKeyDefinition = new()
            {
                Pair = new()
                {
                    ReferencingDbTable = referencingDbTable,
                    ReferencedDbTable = referencedDbTable
                }
            };

            if (referencingColumns is not null)
            {
                foreignKeyDefinition.ReferencingColumns.AddRange(referencingColumns);
            }

            if (referencedColumns is not null)
            {
                foreignKeyDefinition.ReferencedColumns.AddRange(referencedColumns);
            }

            if (relationshipData
                .TargetEntityToFkDefinitionMap.TryGetValue(targetEntityName, out List<ForeignKeyDefinition>? foreignKeys))
            {
                foreignKeys.Add(foreignKeyDefinition);
            }
            else
            {
                relationshipData.TargetEntityToFkDefinitionMap
                    .Add(targetEntityName,
                        new List<ForeignKeyDefinition>() { foreignKeyDefinition });
            }
        }

        /// <summary>
        /// Helper function will parse the schema and database object name
        /// from the provided source string and sort out if a default schema
        /// should be used.
        /// </summary>
        /// <param name="source">source string to parse</param>
        /// <returns>The appropriate schema and db object name as a tuple of strings.</returns>
        /// <exception cref="DataApiBuilderException"></exception>
        public (string, string) ParseSchemaAndDbTableName(string source)
        {
            (string? schemaName, string dbTableName) = EntitySourceNamesParser.ParseSchemaAndTable(source)!;

            // if schemaName is empty we check if the DB type is postgresql
            // and if the schema name was included in the connection string
            // as a value associated with the keyword 'SearchPath'.
            // if the DB type is not postgresql or if the connection string
            // does not include the schema name, we use the default schema name.
            // if schemaName is not empty we must check if Database Type is MySql
            // and in this case we throw an exception since there should be no
            // schema name in this case.
            if (string.IsNullOrEmpty(schemaName))
            {
                // if DatabaseType is not postgresql will short circuit and use default
                if (_databaseType is not DatabaseType.PostgreSQL ||
                    !PostgreSqlMetadataProvider.TryGetSchemaFromConnectionString(
                        connectionString: ConnectionString,
                        out schemaName))
                {
                    schemaName = GetDefaultSchemaName();
                }
            }
            else if (_databaseType is DatabaseType.MySQL)
            {
                throw new DataApiBuilderException(message: $"Invalid database object name: \"{schemaName}.{dbTableName}\"",
                                               statusCode: HttpStatusCode.ServiceUnavailable,
                                               subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

            return (schemaName, dbTableName);
        }

        /// <inheritdoc />
        public List<string> GetSchemaGraphQLFieldNamesForEntityName(string entityName)
            => throw new NotImplementedException();

        /// <inheritdoc />
        public string? GetSchemaGraphQLFieldTypeFromFieldName(string graphQLType, string fieldName)
            => throw new NotImplementedException();

        /// <inheritdoc />
        public FieldDefinitionNode? GetSchemaGraphQLFieldFromFieldName(string graphQLType, string fieldName)
            => throw new NotImplementedException();

        public IReadOnlyDictionary<string, Entity> GetLinkingEntities()
        {
            return _linkingEntities;
        }

        /// <summary>
        /// Enrich the entities in the runtime config with the
        /// object definition information needed by the runtime to serve requests.
        /// Populates table definition for entities specified as tables or views
        /// Populates procedure definition for entities specified as stored procedures
        /// </summary>
        private async Task PopulateObjectDefinitionForEntities()
        {
            foreach ((string entityName, Entity entity) in _entities)
            {
                await PopulateObjectDefinitionForEntity(entityName, entity);
            }

            foreach ((string entityName, Entity entity) in _linkingEntities)
            {
                await PopulateObjectDefinitionForEntity(entityName, entity);
            }

            try
            {
                await PopulateForeignKeyDefinitionAsync();
            }
            catch (Exception e)
            {
                HandleOrRecordException(e);
            }
        }

        private async Task PopulateObjectDefinitionForEntity(string entityName, Entity entity)
        {
            try
            {
                EntitySourceType entitySourceType = GetEntitySourceType(entityName, entity);
                if (entitySourceType is EntitySourceType.StoredProcedure)
                {
                    await FillSchemaForStoredProcedureAsync(
                        entity,
                        entityName,
                        GetSchemaName(entityName),
                        GetDatabaseObjectName(entityName),
                        GetStoredProcedureDefinition(entityName));

                    if (GetDatabaseType() == DatabaseType.MSSQL || GetDatabaseType() == DatabaseType.DWSQL)
                    {
                        await PopulateResultSetDefinitionsForStoredProcedureAsync(
                            GetSchemaName(entityName),
                            GetDatabaseObjectName(entityName),
                            GetStoredProcedureDefinition(entityName));
                    }
                }
                else if (entitySourceType is EntitySourceType.Table)
                {
                    await PopulateSourceDefinitionAsync(
                        entityName,
                        GetSchemaName(entityName),
                        GetDatabaseObjectName(entityName),
                        GetSourceDefinition(entityName),
                        entity.Source.KeyFields);
                }
                else
                {
                    ViewDefinition viewDefinition = (ViewDefinition)GetSourceDefinition(entityName);
                    await PopulateSourceDefinitionAsync(
                        entityName,
                        GetSchemaName(entityName),
                        GetDatabaseObjectName(entityName),
                        viewDefinition,
                        entity.Source.KeyFields);
                }
            }
            catch (Exception e)
            {
                HandleOrRecordException(e);
            }
        }

        /// <summary>
        /// Queries DB to get the result fields name and type to
        /// populate the result set definition for entities specified as stored procedures
        /// </summary>
        private async Task PopulateResultSetDefinitionsForStoredProcedureAsync(
            string schemaName,
            string storedProcedureName,
            SourceDefinition sourceDefinition)
        {
            StoredProcedureDefinition storedProcedureDefinition = (StoredProcedureDefinition)sourceDefinition;
            string dbStoredProcedureName = $"{schemaName}.{storedProcedureName}";
            // Generate query to get result set details
            // of the stored procedure.
            string queryForResultSetDetails = SqlQueryBuilder.BuildStoredProcedureResultDetailsQuery(
                dbStoredProcedureName);

            // Execute the query to get columns' details.
            JsonArray? resultArray = await QueryExecutor.ExecuteQueryAsync(
                sqltext: queryForResultSetDetails,
                parameters: null!,
                dataReaderHandler: QueryExecutor.GetJsonArrayAsync,
                dataSourceName: _dataSourceName);

            using JsonDocument sqlResult = JsonDocument.Parse(resultArray!.ToJsonString());

            // Iterate through each row returned by the query which corresponds to
            // one row in the result set.
            foreach (JsonElement element in sqlResult.RootElement.EnumerateArray())
            {
                string resultFieldName = element.GetProperty(BaseSqlQueryBuilder.STOREDPROC_COLUMN_NAME).ToString();
                Type resultFieldType = SqlToCLRType(element.GetProperty(BaseSqlQueryBuilder.STOREDPROC_COLUMN_SYSTEMTYPENAME).ToString());
                bool isResultFieldNullable = element.GetProperty(BaseSqlQueryBuilder.STOREDPROC_COLUMN_ISNULLABLE).GetBoolean();

                // Store the dictionary containing result set field with its type as Columns
                storedProcedureDefinition.Columns.TryAdd(resultFieldName, new(resultFieldType) { IsNullable = isResultFieldNullable });
            }
        }

        /// <summary>
        /// Helper method to create params for the query.
        /// </summary>
        /// <param name="paramName">Common prefix of param names.</param>
        /// <param name="paramValues">Values of the param.</param>
        /// <returns></returns>
        private static Dictionary<string, object> GetQueryParams(
            string paramName,
            object[] paramValues)
        {
            Dictionary<string, object> parameters = new();
            for (int paramNumber = 0; paramNumber < paramValues.Length; paramNumber++)
            {
                parameters.Add($"{paramName}{paramNumber}", paramValues[paramNumber]);
            }

            return parameters;
        }

        /// <summary>
        /// Generate the mappings of exposed names to
        /// backing columns, and of backing columns to
        /// exposed names. Used to generate EDM Model using
        /// the exposed names, and to translate between
        /// exposed name and backing column (or the reverse)
        /// when needed while processing the request.
        /// For now, only do this for tables/views as Stored Procedures do not have a SourceDefinition
        /// In the future, mappings for SPs could be used for parameter renaming.
        /// We also handle logging the primary key information here since this is when we first have
        /// the exposed names suitable for logging.
        /// </summary>
        private void GenerateExposedToBackingColumnMapsForEntities()
        {
            foreach ((string entityName, Entity _) in _entities)
            {
                try
                {
                    // For StoredProcedures, result set definitions become the column definition.
                    Dictionary<string, string>? mapping = GetMappingForEntity(entityName);
                    EntityBackingColumnsToExposedNames[entityName] = mapping is not null ? mapping : new();
                    EntityExposedNamesToBackingColumnNames[entityName] = EntityBackingColumnsToExposedNames[entityName].ToDictionary(x => x.Value, x => x.Key);
                    SourceDefinition sourceDefinition = GetSourceDefinition(entityName);
                    foreach (string columnName in sourceDefinition.Columns.Keys)
                    {
                        if (!EntityExposedNamesToBackingColumnNames[entityName].ContainsKey(columnName) && !EntityBackingColumnsToExposedNames[entityName].ContainsKey(columnName))
                        {
                            EntityBackingColumnsToExposedNames[entityName].Add(columnName, columnName);
                            EntityExposedNamesToBackingColumnNames[entityName].Add(columnName, columnName);
                        }
                    }
                }
                catch (Exception e)
                {
                    HandleOrRecordException(e);
                }
            }
        }

        /// <summary>
        /// Obtains the underlying mapping that belongs
        /// to a given entity.
        /// </summary>
        /// <param name="entityName">entity whose map we get.</param>
        /// <returns>mapping belonging to entity.</returns>
        private Dictionary<string, string>? GetMappingForEntity(string entityName)
        {
            _entities.TryGetValue(entityName, out Entity? entity);
            return entity?.Mappings;
        }

        /// <summary>
        /// Initialize OData parser by building OData model.
        /// The parser will be used for parsing filter clause and order by clause.
        /// </summary>
        private void InitODataParser()
        {
            _oDataParser.BuildModel(this);
        }

        /// <summary>
        /// Fills the table definition with information of all columns and
        /// primary keys.
        /// </summary>
        /// <param name="schemaName">Name of the schema.</param>
        /// <param name="tableName">Name of the table.</param>
        /// <param name="sourceDefinition">Table definition to fill.</param>
        /// <param name="entityName">EntityName included to pass on for error messaging.</param>
        private async Task PopulateSourceDefinitionAsync(
            string entityName,
            string schemaName,
            string tableName,
            SourceDefinition sourceDefinition,
            string[]? runtimeConfigKeyFields)
        {
            DataTable dataTable = await GetTableWithSchemaFromDataSetAsync(entityName, schemaName, tableName);

            List<DataColumn> primaryKeys = new(dataTable.PrimaryKey);
            if (runtimeConfigKeyFields is null || runtimeConfigKeyFields.Length == 0)
            {
                sourceDefinition.PrimaryKey = new(primaryKeys.Select(primaryKey => primaryKey.ColumnName));
            }
            else
            {
                sourceDefinition.PrimaryKey = new(runtimeConfigKeyFields);
            }

            if (sourceDefinition.PrimaryKey.Count == 0)
            {
                throw new DataApiBuilderException(
                       message: $"Primary key not configured on the given database object {tableName}",
                       statusCode: HttpStatusCode.ServiceUnavailable,
                       subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

            _entities.TryGetValue(entityName, out Entity? entity);
            if (GetDatabaseType() is DatabaseType.MSSQL && entity is not null && entity.Source.Type is EntitySourceType.Table)
            {
                await PopulateTriggerMetadataForTable(entityName, schemaName, tableName, sourceDefinition);
            }

            using DataTableReader reader = new(dataTable);
            DataTable schemaTable = reader.GetSchemaTable();
            RuntimeConfig runtimeConfig = _runtimeConfigProvider.GetConfig();
            foreach (DataRow columnInfoFromAdapter in schemaTable.Rows)
            {
                string columnName = columnInfoFromAdapter["ColumnName"].ToString()!;

                if (runtimeConfig.IsGraphQLEnabled
                    && entity is not null
                    && IsGraphQLReservedName(entity, columnName, graphQLEnabledGlobally: runtimeConfig.IsGraphQLEnabled))
                {
                    throw new DataApiBuilderException(
                       message: $"The column '{columnName}' violates GraphQL name restrictions.",
                       statusCode: HttpStatusCode.ServiceUnavailable,
                       subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
                }

                ColumnDefinition column = new()
                {
                    IsNullable = (bool)columnInfoFromAdapter["AllowDBNull"],
                    IsAutoGenerated = (bool)columnInfoFromAdapter["IsAutoIncrement"],
                    SystemType = (Type)columnInfoFromAdapter["DataType"],
                    // An auto-increment column is also considered as a read-only column. For other types of read-only columns,
                    // the flag is populated later via PopulateColumnDefinitionsWithReadOnlyFlag() method.
                    IsReadOnly = (bool)columnInfoFromAdapter["IsAutoIncrement"]
                };

                // Tests may try to add the same column simultaneously
                // hence we use TryAdd here.
                // If the addition fails, it is assumed the column definition
                // has already been added and need not error out.
                sourceDefinition.Columns.TryAdd(columnName, column);
            }

            DataTable columnsInTable = await GetColumnsAsync(schemaName, tableName);

            PopulateColumnDefinitionWithHasDefaultAndDbType(
                sourceDefinition,
                columnsInTable);

            if (entity is not null && entity.Source.Type is EntitySourceType.Table)
            {
                // For MySql, database name is equivalent to schema name.
                string schemaOrDatabaseName = GetDatabaseType() is DatabaseType.MySQL ? GetDatabaseName() : schemaName;
                await PopulateColumnDefinitionsWithReadOnlyFlag(tableName, schemaOrDatabaseName, sourceDefinition);
            }
        }

        /// <summary>
        /// Helper method to populate the column definitions of each column in a table with the info about
        /// whether the column can be updated or not.
        /// </summary>
        /// <param name="tableName">Name of the table.</param>
        /// <param name="schemaOrDatabaseName">Name of the schema (for MsSql/PgSql)/database (for MySql) of the table.</param>
        /// <param name="sourceDefinition">Table definition.</param>
        private async Task PopulateColumnDefinitionsWithReadOnlyFlag(string tableName, string schemaOrDatabaseName, SourceDefinition sourceDefinition)
        {
            string schemaOrDatabaseParamName = $"{BaseQueryStructure.PARAM_NAME_PREFIX}param0";
            string tableParamName = $"{BaseQueryStructure.PARAM_NAME_PREFIX}param1";
            string queryToGetReadOnlyColumns = SqlQueryBuilder.BuildQueryToGetReadOnlyColumns(schemaOrDatabaseParamName, tableParamName);
            Dictionary<string, DbConnectionParam> parameters = new()
            {
                { schemaOrDatabaseParamName, new(schemaOrDatabaseName, DbType.String) },
                { tableParamName, new(tableName, DbType.String) }
            };

            List<string>? readOnlyFields = await QueryExecutor.ExecuteQueryAsync(
                sqltext: queryToGetReadOnlyColumns,
                parameters: parameters,
                dataReaderHandler: SummarizeReadOnlyFieldsMetadata,
                dataSourceName: _dataSourceName);

            if (readOnlyFields is not null && readOnlyFields.Count > 0)
            {
                foreach (string readOnlyField in readOnlyFields)
                {
                    if (sourceDefinition.Columns.TryGetValue(readOnlyField, out ColumnDefinition? columnDefinition))
                    {
                        // Mark the column as read-only.
                        columnDefinition.IsReadOnly = true;
                    }
                }
            }
        }

        /// <summary>
        /// Determine whether the provided field of a GraphQL enabled entity meets GraphQL reserved name requirements.
        /// Criteria:
        /// - Is GraphQL enabled globally
        /// - Is GraphQL implicitly enabled e.g. entity.GraphQL is null, or explicitly enabled e.g. entity.GraphQL is true).
        /// - If field has a mapped value (alias), then use the mapped value to evaluate name violation.
        /// - If field does not have an alias/mapped value, then use the provided field name to
        /// check for naming violations.
        /// </summary>
        /// <param name="entity">Entity to check </param>
        /// <param name="databaseColumnName">Name to evaluate against GraphQL naming requirements</param>
        /// <param name="graphQLEnabledGlobally">Whether GraphQL is enabled globally in the runtime configuration.</param>
        /// <exception cref="DataApiBuilderException"/>
        /// <returns>True if no name rules are broken. Otherwise, false</returns>
        public static bool IsGraphQLReservedName(Entity entity, string databaseColumnName, bool graphQLEnabledGlobally)
        {
            if (graphQLEnabledGlobally)
            {
                if (entity.GraphQL is null || (entity.GraphQL.Enabled))
                {
                    if (entity.Mappings is not null
                        && entity.Mappings.TryGetValue(databaseColumnName, out string? fieldAlias)
                        && !string.IsNullOrWhiteSpace(fieldAlias))
                    {
                        databaseColumnName = fieldAlias;
                    }

                    return IsIntrospectionField(databaseColumnName);
                }
            }

            return false;
        }

        /// <summary>
        /// Gets the DataTable from the EntitiesDataSet if already present.
        /// If not present, fills it first and returns the same.
        /// </summary>
        private async Task<DataTable> GetTableWithSchemaFromDataSetAsync(
            string entityName,
            string schemaName,
            string tableName)
        {
            // Because we have an instance of SqlMetadataProvider for each individual database
            // (note: this means each actual database not each database type), we do not
            // need to worry about collisions beyond that schema, hence no database name is needed.
            string tableNameWithSchemaPrefix = GetTableNameWithSchemaPrefix(
                schemaName: schemaName,
                tableName: tableName);

            DataTable? dataTable = EntitiesDataSet.Tables[tableNameWithSchemaPrefix];
            if (dataTable is null)
            {
                try
                {
                    dataTable = await FillSchemaForTableAsync(schemaName, tableName);
                }
                catch (Exception ex) when (ex is not DataApiBuilderException)
                {
                    string message;
                    // Check exception content to ensure proper error message for connection string.
                    // If MySql has a non-empty, invalid connection string, it will have the
                    // MYSQL_INVALID_CONNECTION_STRING_MESSAGE in its message when the connection
                    // string is totally invalid and lacks even the basic format of a valid connection
                    // string (ie: ConnectionString="&#@&^@*&^#$"), or will have a targetsite in
                    // the exception with a name of MYSQL_INVALID_CONNECTION_STRING_OPTIONS in the
                    // case where the connection string follows the correct general form, but does
                    // not have keys with valid names (ie: ConnectionString="foo=bar;baz=qux")
                    if (ex.Message.Contains(MySqlMetadataProvider.MYSQL_INVALID_CONNECTION_STRING_MESSAGE) ||
                       (ex.TargetSite is not null &&
                        string.Equals(ex.TargetSite.Name, MySqlMetadataProvider.MYSQL_INVALID_CONNECTION_STRING_OPTIONS)))
                    {
                        message = DataApiBuilderException.CONNECTION_STRING_ERROR_MESSAGE +
                            $"Underlying Exception message: {ex.Message}";
                    }
                    else
                    {
                        message = $"Cannot obtain Schema for entity {entityName} " +
                            $"with underlying database object source: {schemaName}.{tableName} " +
                            $"due to: {ex.Message}";
                    }

                    throw new DataApiBuilderException(
                        message,
                        statusCode: HttpStatusCode.ServiceUnavailable,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
                }
            }

            return dataTable!;
        }

        /// <summary>
        /// This method attempts to open a database connection using the provided connection string.
        /// If the connection fails to open, it catches the exception and throws a DataApiBuilderException.
        /// It is specifically used to validate the connection string provided in the runtime configuration
        /// for single datasource.
        /// </summary>
        private async Task ValidateDatabaseConnection()
        {
            using ConnectionT conn = new();
            conn.ConnectionString = ConnectionString;
            await QueryExecutor.SetManagedIdentityAccessTokenIfAnyAsync(conn, _dataSourceName);
            try
            {
                await conn.OpenAsync();
            }
            catch (Exception ex)
            {
                string message = DataApiBuilderException.CONNECTION_STRING_ERROR_MESSAGE +
                    $" Database connection failed due to: {ex.Message}";
                throw new DataApiBuilderException(
                    message,
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization,
                    innerException: ex);
            }
        }

        /// <summary>
        /// Using a data adapter, obtains the schema of the given table name
        /// and adds the corresponding entity in the data set.
        /// </summary>
        private async Task<DataTable> FillSchemaForTableAsync(
            string schemaName,
            string tableName)
        {
            using ConnectionT conn = new();
            // If connection string is set to empty string
            // we throw here to avoid having to sort out
            // complicated db specific exception messages.
            // This is caught and returned as DataApiBuilderException.
            // The runtime config has a public setter so we check
            // here for empty connection string to ensure that
            // it was not set to an invalid state after initialization.
            if (string.IsNullOrWhiteSpace(ConnectionString))
            {
                throw new DataApiBuilderException(
                    DataApiBuilderException.CONNECTION_STRING_ERROR_MESSAGE +
                    " Connection string is null, empty, or whitespace.",
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

            try
            {
                // for non-MySql DB types, this will throw an exception
                // for malformed connection strings
                conn.ConnectionString = ConnectionString;
                await QueryExecutor.SetManagedIdentityAccessTokenIfAnyAsync(conn, _dataSourceName);
            }
            catch (Exception ex)
            {
                string message = DataApiBuilderException.CONNECTION_STRING_ERROR_MESSAGE +
                    $" Underlying Exception message: {ex.Message}";
                throw new DataApiBuilderException(
                    message,
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization,
                    innerException: ex);
            }

            await conn.OpenAsync();

            DataAdapterT adapterForTable = new();
            CommandT selectCommand = new()
            {
                Connection = conn
            };

            string tableNameWithSchemaPrefix = GetTableNameWithSchemaPrefix(schemaName, tableName);
            selectCommand.CommandText
                = $"SELECT * FROM {tableNameWithSchemaPrefix}";
            adapterForTable.SelectCommand = selectCommand;

            DataTable[] dataTable = adapterForTable.FillSchema(EntitiesDataSet, SchemaType.Source, tableNameWithSchemaPrefix);
            return dataTable[0];
        }

        /// <summary>
        /// Gets the correctly formatted table name with schema as prefix, if one exists.
        /// A schema prefix is simply the correctly formatted and prefixed schema name that
        /// is provided, separated from the table name by a ".". The formatting for both the
        /// schema and table name is based on database type and may or may not include
        /// [] quotes depending how the particular database type handles said format.
        /// </summary>
        /// <param name="schemaName">Name of schema the table belongs within.</param>
        /// <param name="tableName">Name of the table.</param>
        /// <returns>Properly formatted table name with schema prefix if it exists.</returns>
        internal string GetTableNameWithSchemaPrefix(string schemaName, string tableName)
        {
            IQueryBuilder queryBuilder = GetQueryBuilder();
            StringBuilder tablePrefix = new();

            if (!string.IsNullOrEmpty(schemaName))
            {
                // Determine schemaName for prefix.
                schemaName = queryBuilder.QuoteIdentifier(schemaName);
                // Database name is empty we just need the schema name.
                tablePrefix.Append(schemaName);
            }

            string queryPrefix = string.IsNullOrEmpty(tablePrefix.ToString()) ? string.Empty : $"{tablePrefix}.";
            return $"{queryPrefix}{SqlQueryBuilder.QuoteIdentifier(tableName)}";
        }

        /// <summary>
        /// Gets the metadata information of each column of
        /// the given schema.table
        /// </summary>
        /// <returns>A data table where each row corresponds to a
        /// column of the table.</returns>
        protected virtual async Task<DataTable> GetColumnsAsync(
            string schemaName,
            string tableName)
        {
            using ConnectionT conn = new();
            conn.ConnectionString = ConnectionString;
            await QueryExecutor.SetManagedIdentityAccessTokenIfAnyAsync(conn, _dataSourceName);
            await conn.OpenAsync();
            // We can specify the Catalog, Schema, Table Name, Column Name to get
            // the specified column(s).
            // Hence, we should create a 4 members array.
            string[] columnRestrictions = new string[NUMBER_OF_RESTRICTIONS];

            // To restrict the columns for the current table, specify the table's name
            // in column restrictions.
            columnRestrictions[0] = conn.Database;
            columnRestrictions[1] = schemaName;
            columnRestrictions[2] = tableName;

            // Each row in the columnsInTable DataTable corresponds to
            // a single column of the table.
            DataTable columnsInTable = await conn.GetSchemaAsync("Columns", columnRestrictions);

            return columnsInTable;
        }

        /// <summary>
        /// Helper method to populate the column definition with HasDefault and DbType properties.
        /// </summary>
        protected virtual void PopulateColumnDefinitionWithHasDefaultAndDbType(
            SourceDefinition sourceDefinition,
            DataTable allColumnsInTable)
        {
            foreach (DataRow columnInfo in allColumnsInTable.Rows)
            {
                string columnName = (string)columnInfo["COLUMN_NAME"];
                bool hasDefault =
                    Type.GetTypeCode(columnInfo["COLUMN_DEFAULT"].GetType()) != TypeCode.DBNull;
                if (sourceDefinition.Columns.TryGetValue(columnName, out ColumnDefinition? columnDefinition))
                {
                    columnDefinition.HasDefault = hasDefault;

                    if (hasDefault)
                    {
                        columnDefinition.DefaultValue = columnInfo["COLUMN_DEFAULT"];
                    }

                    columnDefinition.DbType = TypeHelper.GetDbTypeFromSystemType(columnDefinition.SystemType);
                }
            }
        }

        /// <summary>
        /// Fills the table definition with information of the foreign keys
        /// for all the tables.
        /// </summary>
        private async Task PopulateForeignKeyDefinitionAsync()
        {
            // For each database object, that has a relationship metadata,
            // build the array storing all the schemaNames(for now the defaultSchemaName)
            // and the array for all tableNames
            List<string> schemaNames = new();
            List<string> tableNames = new();
            IEnumerable<SourceDefinition> dbEntitiesToBePopulatedWithFK =
                FindAllEntitiesWhoseForeignKeyIsToBeRetrieved(schemaNames, tableNames);

            // No need to do any further work if there are no FK to be retrieved
            if (!dbEntitiesToBePopulatedWithFK.Any())
            {
                return;
            }

            // Build the query required to get the foreign key information.
            string queryForForeignKeyInfo =
                ((BaseSqlQueryBuilder)GetQueryBuilder()).BuildForeignKeyInfoQuery(tableNames.Count);

            // Build the parameters dictionary for the foreign key info query
            // consisting of all schema names and table names.
            Dictionary<string, DbConnectionParam> parameters =
                GetForeignKeyQueryParams(
                    schemaNames.ToArray(),
                    tableNames.ToArray());

            // Gather all the referencing and referenced columns for each pair
            // of referencing and referenced tables.
            PairToFkDefinition = await QueryExecutor.ExecuteQueryAsync(
                queryForForeignKeyInfo, parameters, SummarizeFkMetadata, _dataSourceName, httpContext: null, args: null);

            if (PairToFkDefinition is not null)
            {
                FillInferredFkInfo(dbEntitiesToBePopulatedWithFK);
            }

            ValidateAllFkHaveBeenInferred(dbEntitiesToBePopulatedWithFK);
        }

        /// <summary>
        /// Helper method to find all the entities whose foreign key information is to be retrieved.
        /// </summary>
        /// <param name="schemaNames">List of names of the schemas to which entities belong.</param>
        /// <param name="tableNames">List of names of the entities(tables)</param>
        /// <returns>A collection of entity names</returns>
        private IEnumerable<SourceDefinition> FindAllEntitiesWhoseForeignKeyIsToBeRetrieved(
            List<string> schemaNames,
            List<string> tableNames)
        {
            Dictionary<string, SourceDefinition> sourceNameToSourceDefinition = new();
            foreach ((string entityName, DatabaseObject dbObject) in EntityToDatabaseObject)
            {
                // Ensure we're only doing this on tables, not stored procedures which have no table definition,
                // not views whose underlying base table's foreign key constraints are taken care of
                // by database itself.
                if (dbObject.SourceType is EntitySourceType.Table)
                {
                    if (!sourceNameToSourceDefinition.ContainsKey(dbObject.Name))
                    {
                        SourceDefinition sourceDefinition = GetSourceDefinition(entityName);
                        foreach ((_, RelationshipMetadata relationshipData)
                            in sourceDefinition.SourceEntityRelationshipMap)
                        {
                            IEnumerable<List<ForeignKeyDefinition>> foreignKeysForAllTargetEntities
                                = relationshipData.TargetEntityToFkDefinitionMap.Values;
                            foreach (List<ForeignKeyDefinition> fkDefinitionsForTargetEntity
                                in foreignKeysForAllTargetEntities)
                            {
                                foreach (ForeignKeyDefinition fk in fkDefinitionsForTargetEntity)
                                {
                                    schemaNames.Add(fk.Pair.ReferencingDbTable.SchemaName);
                                    tableNames.Add(fk.Pair.ReferencingDbTable.Name);
                                    sourceNameToSourceDefinition.TryAdd(dbObject.Name, sourceDefinition);
                                }
                            }
                        }
                    }
                }
            }

            return sourceNameToSourceDefinition.Values;
        }

        /// <summary>
        /// Method to validate that the foreign key information is populdated
        /// for all the expected entities
        /// </summary>
        /// <param name="dbEntitiesToBePopulatedWithFK">List of database entities
        /// whose definition has to be populated with foreign key information.</param>
        /// <exception cref="NotSupportedException"></exception>
        private void ValidateAllFkHaveBeenInferred(
            IEnumerable<SourceDefinition> dbEntitiesToBePopulatedWithFK)
        {
            foreach (SourceDefinition sourceDefinition in dbEntitiesToBePopulatedWithFK)
            {
                foreach ((string sourceEntityName, RelationshipMetadata relationshipData)
                        in sourceDefinition.SourceEntityRelationshipMap)
                {
                    IEnumerable<List<ForeignKeyDefinition>> foreignKeys = relationshipData.TargetEntityToFkDefinitionMap.Values;
                    // If none of the inferred foreign keys have the referencing columns,
                    // it means metadata is still missing fail the bootstrap.
                    if (!foreignKeys.Any(fkList => fkList.Any(fk => fk.ReferencingColumns.Count() != 0)))
                    {
                        HandleOrRecordException(new NotSupportedException($"Some of the relationship information missing and could not be inferred for {sourceEntityName}."));
                    }
                }
            }
        }

        /// <summary>
        /// Each row in the results of the given data reader represents one column from one foreign key
        /// between an ordered pair of referencing and referenced database objects.
        /// This data reader handler summarizes this foreign key metadata so that
        /// for each referencing and referenced table pair, there is exactly one foreign key definition
        /// containing the list of all referencing columns and referenced columns.
        /// </summary>
        /// <param name="reader">The DbDataReader.</param>
        /// <param name="args">Arguments to this function.</param>
        /// <returns>A dictionary mapping ordered relationship pairs to
        /// foreign key definition between them.</returns>
        private async Task<Dictionary<RelationShipPair, ForeignKeyDefinition>?>
            SummarizeFkMetadata(DbDataReader reader, List<string>? args = null)
        {
            // Extract all the rows in the current Result Set of DbDataReader.
            DbResultSet foreignKeysInfoWithProperties =
                await QueryExecutor.ExtractResultSetFromDbDataReader(reader);

            Dictionary<RelationShipPair, ForeignKeyDefinition> pairToFkDefinition = new();

            foreach (DbResultSetRow foreignKeyInfoWithProperties in foreignKeysInfoWithProperties.Rows)
            {
                Dictionary<string, object?> foreignKeyInfo = foreignKeyInfoWithProperties.Columns;
                string referencingSchemaName =
                    (string)foreignKeyInfo[$"Referencing{nameof(DatabaseObject.SchemaName)}"]!;
                string referencingTableName = (string)foreignKeyInfo[$"Referencing{nameof(SourceDefinition)}"]!;
                string referencedSchemaName =
                    (string)foreignKeyInfo[$"Referenced{nameof(DatabaseObject.SchemaName)}"]!;
                string referencedTableName = (string)foreignKeyInfo[$"Referenced{nameof(SourceDefinition)}"]!;

                DatabaseTable referencingDbObject = GenerateDbTable(referencingSchemaName, referencingTableName);
                DatabaseTable referencedDbObject = GenerateDbTable(referencedSchemaName, referencedTableName);
                RelationShipPair pair = new(referencingDbObject, referencedDbObject);
                if (!pairToFkDefinition.TryGetValue(pair, out ForeignKeyDefinition? foreignKeyDefinition))
                {
                    foreignKeyDefinition = new()
                    {
                        Pair = pair
                    };
                    pairToFkDefinition.Add(pair, foreignKeyDefinition);
                }

                // Add the referenced and referencing columns to the foreign key definition.
                foreignKeyDefinition.ReferencedColumns.Add(
                    (string)foreignKeyInfo[nameof(ForeignKeyDefinition.ReferencedColumns)]!);
                foreignKeyDefinition.ReferencingColumns.Add(
                    (string)foreignKeyInfo[nameof(ForeignKeyDefinition.ReferencingColumns)]!);
            }

            return pairToFkDefinition;
        }

        /// <summary>
        /// Helper method to get all the read-only fields name in a table by processing the DbDataReader instance
        /// which contains the name of all the fields - one field per DbResult row.
        /// </summary>
        /// <param name="reader">The DbDataReader.</param>
        /// <param name="args">Arguments to this function. This parameter is unused in this method.
        /// This is added so that the method conforms with the Func delegate's signature.</param>
        /// <returns>List of read-only fields present in the table.</returns>
        private async Task<List<string>>
            SummarizeReadOnlyFieldsMetadata(DbDataReader reader, List<string>? args = null)
        {
            // Extract all the rows in the current Result Set of DbDataReader.
            DbResultSet readOnlyFieldRowsWithProperties =
                await QueryExecutor.ExtractResultSetFromDbDataReader(reader);

            List<string> readOnlyFields = new();

            foreach (DbResultSetRow readOnlyFieldRowWithProperties in readOnlyFieldRowsWithProperties.Rows)
            {
                Dictionary<string, object?> readOnlyFieldInfo = readOnlyFieldRowWithProperties.Columns;
                string fieldName = (string)readOnlyFieldInfo["column_name"]!;
                readOnlyFields.Add(fieldName);
            }

            return readOnlyFields;
        }

        /// <summary>
        /// Fills the table definition with the inferred foreign key metadata
        /// about the referencing and referenced columns.
        /// </summary>
        /// <param name="dbEntitiesToBePopulatedWithFK">List of database entities
        /// whose definition has to be populated with foreign key information.</param>
        private void FillInferredFkInfo(
            IEnumerable<SourceDefinition> dbEntitiesToBePopulatedWithFK)
        {
            // For each table definition that has to be populated with the inferred
            // foreign key information.
            foreach (SourceDefinition sourceDefinition in dbEntitiesToBePopulatedWithFK)
            {
                // For each source entities, which maps to this table definition
                // and has a relationship metadata to be filled.
                foreach ((string sourceEntityName, RelationshipMetadata relationshipData)
                       in sourceDefinition.SourceEntityRelationshipMap)
                {
                    // Enumerate all the foreign keys required for all the target entities
                    // that this source is related to.
                    foreach ((string targetEntityName, List<ForeignKeyDefinition> fKDefinitionsToTarget) in relationshipData.TargetEntityToFkDefinitionMap)
                    {
                        // 
                        // Scenario 1: When a FK constraint is defined between source and target entities in the database.
                        // In this case, there will be exactly one ForeignKeyDefinition with the right pair of Referencing and Referenced tables. 
                        // Scenario 2: When no FK constraint is defined between source and target entities, but the relationship fields are configured through config file
                        // In this case, two entries will be created. 
                        // First entry: Referencing table: Source entity, Referenced table: Target entity
                        // Second entry: Referencing table: Target entity, Referenced table: Source entity 
                        List<ForeignKeyDefinition> validatedFKDefinitionsToTarget = GetValidatedFKs(fKDefinitionsToTarget);
                        relationshipData.TargetEntityToFkDefinitionMap[targetEntityName] = validatedFKDefinitionsToTarget;
                    }
                }
            }
        }

        /// <summary>
        /// Loops over all the foreign key definitions defined for the target entity in the source entity's definition
        /// and adds to the set of validated FK definitions:
        /// 1. All the FK definitions which actually map to a foreign key constraint defined in the database.
        /// In such a case, if the source/target fields are also provided in the config, they are given precedence over the FK constraint.
        /// 2. FK definitions for custom relationships defined by the user in the configuration file where no FK constraint exists between
        /// the pair of (source, target) entities.
        /// </summary>
        /// <param name="fKDefinitionsToTarget">List of FK definitions defined from source to target.</param>
        /// <returns>List of validated FK definitions from source to target.</returns>
        private List<ForeignKeyDefinition> GetValidatedFKs(
            List<ForeignKeyDefinition> fKDefinitionsToTarget)
        {
            List<ForeignKeyDefinition> validatedFKDefinitionsToTarget = new();
            foreach (ForeignKeyDefinition fKDefinitionToTarget in fKDefinitionsToTarget)
            {
                // This code block adds FK definitions between source and target entities when there is an FK constraint defined
                // in the database, either from source->target or target->source entities or both.

                // Add the referencing and referenced columns for this foreign key definition for the target.
                if (PairToFkDefinition is not null &&
                    PairToFkDefinition.TryGetValue(fKDefinitionToTarget.Pair, out ForeignKeyDefinition? inferredFKDefinition))
                {
                    // Being here indicates that we inferred an FK constraint for the current foreign key definition.
                    // The count of referencing and referenced columns being > 0 indicates that source.fields and target.fields 
                    // have been specified in the config file.
                    // In this scenario, higher precedence is given to the fields configured through the config file. So, the existing FK definition is retained as is.
                    if (fKDefinitionToTarget.ReferencingColumns.Count > 0 && fKDefinitionToTarget.ReferencedColumns.Count > 0)
                    {
                        validatedFKDefinitionsToTarget.Add(fKDefinitionToTarget);
                    }
                    // The count of referenced and referencing columns being = 0 indicates that source.fields and target.fields 
                    // are not configured through the config file. In this case, the FK fields inferred from the database are populated.
                    else
                    {
                        validatedFKDefinitionsToTarget.Add(inferredFKDefinition);
                    }
                }
                else
                {
                    // This code block adds FK definitions between source and target entities when DAB hasn't yet identified an FK constraint.
                    //
                    // Being here indicates that we haven't yet found an FK constraint in the database for the current FK definition.
                    // But this does not indicate absence of an FK constraint between the source, target entities yet.
                    // This may happen when an FK constraint exists between two tables, but in an order opposite to the order
                    // of referencing and referenced tables present in the current FK definition. This happens because for a relationship
                    // with right cardinality as 1, we add FK definitions from both source->target and target->source to the source entity's definition.
                    // because at that point we don't know if the relationship is an N:1 relationship or a 1:1 relationship.
                    // So here, we need to remove the wrong FK definition for:
                    // 1. N:1 relationships,
                    // 2. 1:1 relationships where an FK constraint exists only from source->target or target->source but not both.
                    //
                    // E.g. for a relationship between Book-Publisher entities with cardinality 1, we would have added a Foreign key definition
                    // from Book->Publisher and Publisher->Book to Book's source definition earlier.
                    // Since it is an N:1 relationship, it might have been the case that the current FK definition had
                    // 'publishers' table as the referencing table and 'books' table as the referenced table, and hence,
                    // we did not find any FK constraint. But an FK constraint does exist where 'books' is the referencing table
                    // while the 'publishers' is the referenced table.
                    // (The definition for that constraint would be taken care of while adding database FKs above.)
                    //
                    // So, before concluding that there is no FK constraint between the source, target entities, we need
                    // to confirm absence of FK constraint from source->target and target->source tables.
                    RelationShipPair inverseFKPair = new(fKDefinitionToTarget.Pair.ReferencedDbTable, fKDefinitionToTarget.Pair.ReferencingDbTable);

                    // Add FK definition to the set of validated FKs only if no FK constraint is defined for the source and target entities
                    // in the database, either from source -> target or target -> source.
                    // When a foreign key constraint is not identified using inverseFKPair and fKDefinitionToTarget.Pair, it means that
                    // the FK constraint is only defined in the runtime config file.
                    if (PairToFkDefinition is not null && !PairToFkDefinition.ContainsKey(inverseFKPair))
                    {
                        validatedFKDefinitionsToTarget.Add(fKDefinitionToTarget);
                    }
                }
            }

            return validatedFKDefinitionsToTarget;
        }

        /// <summary>
        /// For the given two database objects, returns true if a foreignKey exists between them.
        /// Else returns false.
        /// </summary>
        public bool VerifyForeignKeyExistsInDB(
            DatabaseTable databaseTableA,
            DatabaseTable databaseTableB)
        {
            if (PairToFkDefinition is null)
            {
                return false;
            }

            RelationShipPair pairAB = new(databaseTableA, databaseTableB);
            RelationShipPair pairBA = new(databaseTableB, databaseTableA);

            return (PairToFkDefinition.ContainsKey(pairAB) || PairToFkDefinition.ContainsKey(pairBA));
        }

        /// <summary>
        /// Retrieving the partition key path, for cosmosdb_nosql only
        /// </summary>
        public string? GetPartitionKeyPath(string database, string container)
            => throw new NotImplementedException();

        /// <summary>
        /// Setting the partition key path, for cosmosdb_nosql only
        /// </summary>
        public void SetPartitionKeyPath(string database, string container, string partitionKeyPath)
            => throw new NotImplementedException();

        public bool IsDevelopmentMode()
        {
            return _runtimeConfigProvider.GetConfig().IsDevelopmentMode();
        }
    }
}

