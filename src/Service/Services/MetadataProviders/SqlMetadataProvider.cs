using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Parsers;
using Azure.DataApiBuilder.Service.Resolvers;
using Humanizer;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Service.Services
{
    /// <summary>
    /// Reads schema information from the database to make it
    /// available for the GraphQL/REST services.
    /// </summary>
    public class SqlMetadataProvider<ConnectionT, DataAdapterT, CommandT> : ISqlMetadataProvider
        where ConnectionT : DbConnection, new()
        where DataAdapterT : DbDataAdapter, new()
        where CommandT : DbCommand, new()
    {
        private ODataParser _oDataParser = new();

        private readonly DatabaseType _databaseType;

        private readonly Dictionary<string, Entity> _entities;

        // nullable since Mock tests do not need it.
        // TODO: Refactor the Mock tests to remove the nullability here
        // once the runtime config is implemented tracked by #353.
        private readonly IQueryExecutor? _queryExecutor;

        private const int NUMBER_OF_RESTRICTIONS = 4;

        protected string ConnectionString { get; init; }

        protected IQueryBuilder SqlQueryBuilder { get; init; }

        protected DataSet EntitiesDataSet { get; init; }

        private Dictionary<string, Dictionary<string, string>> EntityBackingColumnsToExposedNames { get; } = new();

        private Dictionary<string, Dictionary<string, string>> EntityExposedNamesToBackingColumnNames { get; } = new();

        private Dictionary<string, string> EntityRouteToEntityName { get; } = new();

        /// <summary>
        /// Maps an entity name to a DatabaseObject.
        /// </summary>
        public Dictionary<string, DatabaseObject> EntityToDatabaseObject { get; set; } =
            new(StringComparer.InvariantCulture);

        private readonly ILogger<ISqlMetadataProvider> _logger;

        public SqlMetadataProvider(
            RuntimeConfigProvider runtimeConfigProvider,
            IQueryExecutor queryExecutor,
            IQueryBuilder queryBuilder,
            ILogger<ISqlMetadataProvider> logger)
        {
            RuntimeConfig runtimeConfig = runtimeConfigProvider.GetRuntimeConfiguration();

            _databaseType = runtimeConfig.DatabaseType;
            _entities = runtimeConfig.Entities;
            ConnectionString = runtimeConfig.ConnectionString;
            EntitiesDataSet = new();
            SqlQueryBuilder = queryBuilder;
            _queryExecutor = queryExecutor;
            _logger = logger;
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
                throw new InvalidCastException($"Table Definition for {entityName} has not been inferred.");
            }

            return databaseObject!.SchemaName;
        }

        /// <inheritdoc />
        public string GetDatabaseObjectName(string entityName)
        {
            if (!EntityToDatabaseObject.TryGetValue(entityName, out DatabaseObject? databaseObject))
            {
                throw new InvalidCastException($"Table Definition for {entityName} has not been inferred.");
            }

            return databaseObject!.Name;
        }

        /// <inheritdoc />
        public TableDefinition GetTableDefinition(string entityName)
        {
            if (!EntityToDatabaseObject.TryGetValue(entityName, out DatabaseObject? databaseObject))
            {
                throw new InvalidCastException($"Table Definition for {entityName} has not been inferred.");
            }

            return databaseObject!.TableDefinition;
        }

        /// <inheritdoc />
        public bool TryGetExposedColumnName(string entityName, string backingFieldName, out string? name)
        {
            return EntityBackingColumnsToExposedNames[entityName].TryGetValue(backingFieldName, out name);
        }

        /// <inheritdoc />
        public bool TryGetBackingColumn(string entityName, string field, out string? name)
        {
            return EntityExposedNamesToBackingColumnNames[entityName].TryGetValue(field, out name);
        }

        /// <inheritdoc />
        public virtual bool TryGetEntityNameFromRoute(string entityRouteName, out string? entityName)
        {
            return EntityRouteToEntityName.TryGetValue(entityRouteName, out entityName);
        }

        /// <inheritdoc />
        public IEnumerable<KeyValuePair<string, DatabaseObject>> GetEntityNamesAndDbObjects()
        {
            return EntityToDatabaseObject.ToList();
        }

        /// <inheritdoc />
        public async Task InitializeAsync()
        {
            System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();
            GenerateDatabaseObjectForEntities();
            await PopulateTableDefinitionForEntities();
            GenerateExposedToBackingColumnMapsForEntities();
            GenerateRouteToEntityMap();
            InitODataParser();
            timer.Stop();
            _logger.LogTrace($"Done inferring Sql database schema in {timer.ElapsedMilliseconds}ms.");
        }

        /// <summary>
        /// Generates the map used to find a given entity based
        /// on the request route that will be used for that entity.
        /// </summary>
        private void GenerateRouteToEntityMap()
        {
            foreach (string entityName in _entities.Keys)
            {
                Entity entity = _entities[entityName];
                string pluralizedRoute = PluralizeEntityRoute(entity, entityName);
                EntityRouteToEntityName[pluralizedRoute] = entityName;
            }
        }

        /// <summary>
        /// Correctly pluralize the entity's route.
        /// </summary>
        /// <param name="entity">Entity to pluralize the route of.</param>
        /// <returns>pluralized route for the given Entity.</returns>
        private static string PluralizeEntityRoute(Entity entity, string entityName)
        {
            // if entity.Rest is null or a bool we just use source name
            if (entity.Rest is null || ((JsonElement)entity.Rest).ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return entityName;
            }

            // otherwise we have to convert each part of the Rest property we want into correct objects
            // they are json element so this means deserializing at each step with case insensitivity
            JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true };
            RestEntitySettings rest = JsonSerializer.Deserialize<RestEntitySettings>((JsonElement)entity.Rest, options)!;
            SingularPlural restRoute = JsonSerializer.Deserialize<SingularPlural>((JsonElement)rest.Route, options)!;
            // Plural takes precedence, otherwise we pluralize singular before returning
            return !string.IsNullOrWhiteSpace(restRoute.Plural) ? restRoute.Plural : restRoute.Singular.Pluralize();
        }

        /// <summary>
        /// Returns the default schema name. Throws exception here since
        /// each derived class should override this method.
        /// </summary>
        /// <exception cref="NotSupportedException"></exception>
        protected virtual string GetDefaultSchemaName()
        {
            throw new NotSupportedException($"Cannot get default schema " +
                $"name for database type {_databaseType}");
        }

        /// <summary>
        /// Creates a Database object with the given schema and table names.
        /// </summary>
        protected virtual DatabaseObject GenerateDbObject(string schemaName, string tableName)
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
        protected virtual Dictionary<string, object?>
            GetForeignKeyQueryParams(
                string[] schemaNames,
                string[] tableNames)
        {
            Dictionary<string, object?> parameters = new();
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
                parameters.Add(schemaNameParams[i], schemaNames[i]);
            }

            for (int i = 0; i < tableNames.Count(); ++i)
            {
                parameters.Add(tableNameParams[i], tableNames[i]);
            }

            return parameters;
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

                    if (entity.Relationships is not null)
                    {
                        AddForeignKeysForRelationships(entityName, entity, sourceObject);
                    }
                }
            }
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
        /// <param name="databaseObject"></param>
        /// <exception cref="InvalidOperationException"></exception>
        private void AddForeignKeysForRelationships(
            string entityName,
            Entity entity,
            DatabaseObject databaseObject)
        {
            RelationshipMetadata? relationshipData;
            if (!databaseObject.TableDefinition.SourceEntityRelationshipMap
                .TryGetValue(entityName, out relationshipData))
            {
                relationshipData = new();
                databaseObject.TableDefinition.SourceEntityRelationshipMap.Add(entityName, relationshipData);
            }

            string targetSchemaName, targetDbObjectName, linkingObjectSchema, linkingObjectName;
            foreach (Relationship relationship in entity.Relationships!.Values)
            {
                string targetEntityName = relationship.TargetEntity;
                if (!_entities.TryGetValue(targetEntityName, out Entity? targetEntity))
                {
                    throw new InvalidOperationException($"Target Entity {targetEntityName} should be one of the exposed entities.");
                }

                (targetSchemaName, targetDbObjectName) = ParseSchemaAndDbObjectName(targetEntity.GetSourceName())!;
                DatabaseObject targetDbObject = new(targetSchemaName, targetDbObjectName);
                // If a linking object is specified,
                // give that higher preference and add two foreign keys for this targetEntity.
                if (relationship.LinkingObject is not null)
                {
                    (linkingObjectSchema, linkingObjectName) = ParseSchemaAndDbObjectName(relationship.LinkingObject)!;
                    DatabaseObject linkingDbObject = new(linkingObjectSchema, linkingObjectName);
                    AddForeignKeyForTargetEntity(
                        targetEntityName,
                        referencingDbObject: linkingDbObject,
                        referencedDbObject: databaseObject,
                        referencingColumns: relationship.LinkingSourceFields,
                        referencedColumns: relationship.SourceFields,
                        relationshipData);

                    AddForeignKeyForTargetEntity(
                        targetEntityName,
                        referencingDbObject: linkingDbObject,
                        referencedDbObject: targetDbObject,
                        referencingColumns: relationship.LinkingTargetFields,
                        referencedColumns: relationship.TargetFields,
                        relationshipData);
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
                        referencingDbObject: databaseObject,
                        referencedDbObject: targetDbObject,
                        referencingColumns: relationship.SourceFields,
                        referencedColumns: relationship.TargetFields,
                        relationshipData);

                    // Adds another foreign key defintion with targetEntity.GetSourceName()
                    // as the referencingTableName - in the situation of a One-to-One relationship
                    // and the foreign key is defined in the source of targetEntity.
                    // This foreign key WILL NOT exist if its a Many-One relationship.
                    AddForeignKeyForTargetEntity(
                        targetEntityName,
                        referencingDbObject: targetDbObject,
                        referencedDbObject: databaseObject,
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
                        referencingDbObject: targetDbObject,
                        referencedDbObject: databaseObject,
                        referencingColumns: relationship.TargetFields,
                        referencedColumns: relationship.SourceFields,
                        relationshipData);
                }
            }
        }

        /// <summary>
        /// Adds a new foreign key definition for the target entity
        /// in the relationship metadata.
        /// </summary>
        private static void AddForeignKeyForTargetEntity(
            string targetEntityName,
            DatabaseObject referencingDbObject,
            DatabaseObject referencedDbObject,
            string[]? referencingColumns,
            string[]? referencedColumns,
            RelationshipMetadata relationshipData)
        {
            ForeignKeyDefinition foreignKeyDefinition = new()
            {
                Pair = new()
                {
                    ReferencingDbObject = referencingDbObject,
                    ReferencedDbObject = referencedDbObject
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
        /// from the provided and string and sort out if a default schema
        /// should be used. It then returns the appropriate schema and
        /// db object name as a tuple of strings.
        /// </summary>
        /// <param name="source">source string to parse</param>
        /// <returns></returns>
        /// <exception cref="DataApiBuilderException"></exception>
        public (string, string) ParseSchemaAndDbObjectName(string source)
        {
            (string? schemaName, string dbObjectName) = EntitySourceNamesParser.ParseSchemaAndTable(source)!;

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
                if (_databaseType is not DatabaseType.postgresql ||
                    !PostgreSqlMetadataProvider.TryGetSchemaFromConnectionString(
                        connectionString: ConnectionString,
                        out schemaName))
                {
                    schemaName = GetDefaultSchemaName();
                }
            }
            else if (_databaseType is DatabaseType.mysql)
            {
                throw new DataApiBuilderException(message: $"Invalid database object name: \"{schemaName}.{dbObjectName}\"",
                                               statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                                               subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

            return (schemaName, dbObjectName);
        }

        /// <summary>
        /// Enrich the entities in the runtime config with the
        /// table definition information needed by the runtime to serve requests.
        /// </summary>
        private async Task PopulateTableDefinitionForEntities()
        {
            foreach (string entityName
                in EntityToDatabaseObject.Keys)
            {
                await PopulateTableDefinitionAsync(
                    entityName,
                    GetSchemaName(entityName),
                    GetDatabaseObjectName(entityName),
                    GetTableDefinition(entityName));
            }

            await PopulateForeignKeyDefinitionAsync();

        }

        /// <summary>
        /// Generate the mappings of exposed names to
        /// backing columns, and of backing columns to
        /// exposed names. Used to generate EDM Model using
        /// the exposed names, and to translate between
        /// exposed name and backing column (or the reverse)
        /// when needed while processing the request.
        /// </summary>
        private void GenerateExposedToBackingColumnMapsForEntities()
        {
            foreach (string entityName in _entities.Keys)
            {
                Dictionary<string, string>? mapping = GetMappingForEntity(entityName);
                EntityBackingColumnsToExposedNames[entityName] = mapping is not null ? mapping : new();
                EntityExposedNamesToBackingColumnNames[entityName] = EntityBackingColumnsToExposedNames[entityName].ToDictionary(x => x.Value, x => x.Key);
                foreach (string column in EntityToDatabaseObject[entityName].TableDefinition.Columns.Keys)
                {
                    if (!EntityExposedNamesToBackingColumnNames[entityName].ContainsKey(column) && !EntityBackingColumnsToExposedNames[entityName].ContainsKey(column))
                    {
                        EntityBackingColumnsToExposedNames[entityName].Add(column, column);
                        EntityExposedNamesToBackingColumnNames[entityName].Add(column, column);
                    }
                }
            }
        }

        /// <summary>
        /// Obtains the underlying mapping that belongs
        /// to a given entity.
        /// </summary>
        /// <param name="entityName">entity whose map we get.</param>
        /// <returns>mapping belonging to eneity.</returns>
        private Dictionary<string, string>? GetMappingForEntity(string entityName)
        {
            _entities.TryGetValue(entityName, out Entity? entity);
            return entity is not null ? entity.Mappings : null;
        }

        /// <summary>
        /// Initialize OData parser by buidling OData model.
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
        /// <param name="tableDefinition">Table definition to fill.</param>
        /// <param name="entityName">EntityName included to pass on for error messaging.</param>
        private async Task PopulateTableDefinitionAsync(
            string entityName,
            string schemaName,
            string tableName,
            TableDefinition tableDefinition)
        {
            DataTable dataTable = await GetTableWithSchemaFromDataSetAsync(entityName, schemaName, tableName);

            List<DataColumn> primaryKeys = new(dataTable.PrimaryKey);

            if (primaryKeys.Count == 0)
            {
                throw new DataApiBuilderException(
                       message: $"Primary key not configured on the given database object {tableName}",
                       statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                       subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

            tableDefinition.PrimaryKey = new(primaryKeys.Select(primaryKey => primaryKey.ColumnName));

            using DataTableReader reader = new(dataTable);
            DataTable schemaTable = reader.GetSchemaTable();
            foreach (DataRow columnInfoFromAdapter in schemaTable.Rows)
            {
                string columnName = columnInfoFromAdapter["ColumnName"].ToString()!;
                ColumnDefinition column = new()
                {
                    IsNullable = (bool)columnInfoFromAdapter["AllowDBNull"],
                    IsAutoGenerated = (bool)columnInfoFromAdapter["IsAutoIncrement"],
                    SystemType = (Type)columnInfoFromAdapter["DataType"]
                };

                // Tests may try to add the same column simultaneously
                // hence we use TryAdd here.
                // If the addition fails, it is assumed the column definition
                // has already been added and need not error out.
                tableDefinition.Columns.TryAdd(columnName, column);
            }

            DataTable columnsInTable = await GetColumnsAsync(schemaName, tableName);

            PopulateColumnDefinitionWithHasDefault(
                tableDefinition,
                columnsInTable);
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
            DataTable? dataTable = EntitiesDataSet.Tables[tableName];
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
            }
            catch (Exception ex)
            {
                string message = DataApiBuilderException.CONNECTION_STRING_ERROR_MESSAGE +
                    $" Underlying Exception message: {ex.Message}";
                throw new DataApiBuilderException(
                    message,
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

            await conn.OpenAsync();

            DataAdapterT adapterForTable = new();
            CommandT selectCommand = new()
            {
                Connection = conn
            };

            string tablePrefix = GetTablePrefix(conn.Database, schemaName);
            selectCommand.CommandText
                = ($"SELECT * FROM {tablePrefix}.{SqlQueryBuilder.QuoteIdentifier(tableName)}");
            adapterForTable.SelectCommand = selectCommand;

            DataTable[] dataTable = adapterForTable.FillSchema(EntitiesDataSet, SchemaType.Source, tableName);
            return dataTable[0];
        }

        private string GetTablePrefix(string databaseName, string schemaName)
        {
            StringBuilder tablePrefix = new(SqlQueryBuilder.QuoteIdentifier(databaseName));
            if (!string.IsNullOrEmpty(schemaName))
            {
                schemaName = SqlQueryBuilder.QuoteIdentifier(schemaName);
                tablePrefix.Append($".{schemaName}");
            }

            return tablePrefix.ToString();
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
        /// Populates the column definition with HasDefault property.
        /// </summary>
        private static void PopulateColumnDefinitionWithHasDefault(
            TableDefinition tableDefinition,
            DataTable allColumnsInTable)
        {
            foreach (DataRow columnInfo in allColumnsInTable.Rows)
            {
                string columnName = (string)columnInfo["COLUMN_NAME"];
                bool hasDefault =
                    Type.GetTypeCode(columnInfo["COLUMN_DEFAULT"].GetType()) != TypeCode.DBNull;
                ColumnDefinition? columnDefinition;
                if (tableDefinition.Columns.TryGetValue(columnName, out columnDefinition))
                {
                    columnDefinition.HasDefault = hasDefault;

                    if (hasDefault)
                    {
                        columnDefinition.DefaultValue = columnInfo["COLUMN_DEFAULT"];
                    }
                }
            }
        }

        /// <summary>
        /// Fills the table definition with information of the foreign keys
        /// for all the tables.
        /// </summary>
        /// <param name="schemaName">Name of the default schema.</param>
        /// <param name="tables">Dictionary of all tables.</param>
        private async Task PopulateForeignKeyDefinitionAsync()
        {
            // For each database object, that has a relationship metadata,
            // build the array storing all the schemaNames(for now the defaultSchemaName)
            // and the array for all tableNames
            List<string> schemaNames = new();
            List<string> tableNames = new();
            IEnumerable<TableDefinition> tablesToBePopulatedWithFK =
                FindAllTablesWhoseForeignKeyIsToBeRetrieved(schemaNames, tableNames);

            // No need to do any further work if there are no FK to be retrieved
            if (tablesToBePopulatedWithFK.Count() == 0)
            {
                return;
            }

            // Build the query required to get the foreign key information.
            string queryForForeignKeyInfo =
                ((BaseSqlQueryBuilder)SqlQueryBuilder).BuildForeignKeyInfoQuery(tableNames.Count());

            // Build the parameters dictionary for the foreign key info query
            // consisting of all schema names and table names.
            Dictionary<string, object?> parameters =
                GetForeignKeyQueryParams(
                    schemaNames.ToArray(),
                    tableNames.ToArray());

            // Gather all the referencing and referenced columns for each pair
            // of referencing and referenced tables.
            Dictionary<RelationShipPair, ForeignKeyDefinition> pairToFkDefinition
                = await ExecuteAndSummarizeFkMetadata(queryForForeignKeyInfo, parameters);

            FillInferredFkInfo(pairToFkDefinition, tablesToBePopulatedWithFK);

            ValidateAllFkHaveBeenInferred(tablesToBePopulatedWithFK);
        }

        private IEnumerable<TableDefinition>
            FindAllTablesWhoseForeignKeyIsToBeRetrieved(
                List<string> schemaNames,
                List<string> tableNames)
        {
            Dictionary<string, TableDefinition> sourceNameToTableDefinition = new();
            foreach ((_, DatabaseObject dbObject) in EntityToDatabaseObject)
            {
                if (!sourceNameToTableDefinition.ContainsKey(dbObject.Name))
                {
                    foreach ((_, RelationshipMetadata relationshipData)
                        in dbObject.TableDefinition.SourceEntityRelationshipMap)
                    {
                        IEnumerable<List<ForeignKeyDefinition>> foreignKeysForAllTargetEntities
                            = relationshipData.TargetEntityToFkDefinitionMap.Values;
                        foreach (List<ForeignKeyDefinition> fkDefinitionsForTargetEntity
                            in foreignKeysForAllTargetEntities)
                        {
                            foreach (ForeignKeyDefinition fk in fkDefinitionsForTargetEntity)
                            {
                                schemaNames.Add(fk.Pair.ReferencingDbObject.SchemaName);
                                tableNames.Add(fk.Pair.ReferencingDbObject.Name);
                                sourceNameToTableDefinition.TryAdd(dbObject.Name, dbObject.TableDefinition);
                            }
                        }
                    }
                }
            }

            return sourceNameToTableDefinition.Values;
        }

        private static void ValidateAllFkHaveBeenInferred(
            IEnumerable<TableDefinition> tablesToBePopulatedWithFK)
        {
            foreach (TableDefinition tableDefinition in tablesToBePopulatedWithFK)
            {
                foreach ((string sourceEntityName, RelationshipMetadata relationshipData)
                        in tableDefinition.SourceEntityRelationshipMap)
                {
                    IEnumerable<List<ForeignKeyDefinition>> foreignKeys = relationshipData.TargetEntityToFkDefinitionMap.Values;
                    // If none of the inferred foreign keys have the referencing columns,
                    // it means metadata is still missing fail the bootstrap.
                    if (!foreignKeys.Any(fkList => fkList.Any(fk => fk.ReferencingColumns.Count() != 0)))
                    {
                        throw new NotSupportedException($"Some of the relationship information missing and could not be inferred for {sourceEntityName}.");
                    }
                }
            }
        }

        /// <summary>
        /// Executes the given foreign key query with parameters
        /// and summarizes the results for each referencing and referenced table pair.
        /// </summary>
        /// <param name="queryForForeignKeyInfo"></param>
        /// <param name="parameters"></param>
        /// <returns></returns>
        private async Task<Dictionary<RelationShipPair, ForeignKeyDefinition>>
            ExecuteAndSummarizeFkMetadata(
                string queryForForeignKeyInfo,
                Dictionary<string, object?> parameters)
        {
            // Execute the foreign key info query.
            using DbDataReader reader =
                await _queryExecutor!.ExecuteQueryAsync(queryForForeignKeyInfo, parameters);

            // Extract the first row from the result.
            Dictionary<string, object?>? foreignKeyInfo =
                await _queryExecutor!.ExtractRowFromDbDataReader(reader);

            Dictionary<RelationShipPair, ForeignKeyDefinition> pairToFkDefinition = new();
            while (foreignKeyInfo != null)
            {
                string referencingSchemaName =
                    (string)foreignKeyInfo[$"Referencing{nameof(DatabaseObject.SchemaName)}"]!;
                string referencingTableName = (string)foreignKeyInfo[$"Referencing{nameof(TableDefinition)}"]!;
                string referencedSchemaName =
                    (string)foreignKeyInfo[$"Referenced{nameof(DatabaseObject.SchemaName)}"]!;
                string referencedTableName = (string)foreignKeyInfo[$"Referenced{nameof(TableDefinition)}"]!;

                DatabaseObject referencingDbObject = GenerateDbObject(referencingSchemaName, referencingTableName);
                DatabaseObject referencedDbObject = GenerateDbObject(referencedSchemaName, referencedTableName);
                RelationShipPair pair = new(referencingDbObject, referencedDbObject);
                if (!pairToFkDefinition.TryGetValue(pair, out ForeignKeyDefinition? foreignKeyDefinition))
                {
                    foreignKeyDefinition = new()
                    {
                        Pair = pair
                    };
                    pairToFkDefinition.Add(pair, foreignKeyDefinition);
                }

                // add the referenced and referencing columns to the foreign key definition.
                foreignKeyDefinition.ReferencedColumns.Add(
                    (string)foreignKeyInfo[nameof(ForeignKeyDefinition.ReferencedColumns)]!);
                foreignKeyDefinition.ReferencingColumns.Add(
                    (string)foreignKeyInfo[nameof(ForeignKeyDefinition.ReferencingColumns)]!);

                foreignKeyInfo = await _queryExecutor.ExtractRowFromDbDataReader(reader);
            }

            return pairToFkDefinition;
        }

        /// <summary>
        /// Fills the table definition with the inferred foreign key metadata
        /// about the referencing and referenced columns.
        /// </summary>
        /// <param name="pairToFkDefinition"></param>
        /// <param name="tablesToBePopulatedWithFK"></param>
        private static void FillInferredFkInfo(
            Dictionary<RelationShipPair, ForeignKeyDefinition> pairToFkDefinition,
            IEnumerable<TableDefinition> tablesToBePopulatedWithFK)
        {
            // For each table definition that has to be populated with the inferred
            // foreign key information.
            foreach (TableDefinition tableDefinition in tablesToBePopulatedWithFK)
            {
                // For each source entities, which maps to this table definition
                // and has a relationship metadata to be filled.
                foreach ((_, RelationshipMetadata relationshipData)
                       in tableDefinition.SourceEntityRelationshipMap)
                {
                    // Enumerate all the foreign keys required for all the target entities
                    // that this source is related to.
                    IEnumerable<List<ForeignKeyDefinition>> foreignKeysForAllTargetEntities =
                        relationshipData.TargetEntityToFkDefinitionMap.Values;
                    // For each target, loop through each foreign key
                    foreach (List<ForeignKeyDefinition> foreignKeysForTarget in foreignKeysForAllTargetEntities)
                    {
                        // For each foreign key between this pair of source and target entities
                        // which needs the referencing columns,
                        // find the fk inferred for this pair the backend and
                        // equate the referencing columns and referenced columns.
                        foreach (ForeignKeyDefinition fk in foreignKeysForTarget)
                        {
                            // if the referencing and referenced columns count > 0,
                            // we have already gathered this information from the runtime config.
                            if (fk.ReferencingColumns.Count > 0 && fk.ReferencedColumns.Count > 0)
                            {
                                continue;
                            }

                            // Add the referencing and referenced columns for this foreign key definition
                            // for the target.
                            if (pairToFkDefinition.TryGetValue(
                                    fk.Pair, out ForeignKeyDefinition? inferredDefinition))
                            {
                                // Only add the referencing columns if they have not been
                                // specified in the configuration file.
                                if (fk.ReferencingColumns.Count == 0)
                                {
                                    fk.ReferencingColumns.AddRange(inferredDefinition.ReferencingColumns);
                                }

                                // Only add the referenced columns if they have not been
                                // specified in the configuration file.
                                if (fk.ReferencedColumns.Count == 0)
                                {
                                    fk.ReferencedColumns.AddRange(inferredDefinition.ReferencedColumns);
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Retrieving the partition key path, for Cosmos only
        /// </summary>
        public string? GetPartitionKeyPath(string database, string container)
            => throw new NotImplementedException();

        /// <summary>
        /// Setting the partition key path, for Cosmos only
        /// </summary>
        public void SetPartitionKeyPath(string database, string container, string partitionKeyPath)
            => throw new NotImplementedException();

    }
}

