using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Resolvers;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.Options;

namespace Azure.DataGateway.Service.Services
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
        private FilterParser _oDataFilterParser = new();

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

        /// <summary>
        /// Maps an entity name to a DatabaseObject.
        /// </summary>
        public Dictionary<string, DatabaseObject> EntityToDatabaseObject { get; set; } =
            new(StringComparer.InvariantCultureIgnoreCase);

        public SqlMetadataProvider(
            IOptionsMonitor<RuntimeConfigPath> runtimeConfigPath,
            IQueryExecutor queryExecutor,
            IQueryBuilder queryBuilder)
        {
            runtimeConfigPath.CurrentValue.
                ExtractConfigValues(
                    out _databaseType,
                    out string connectionString,
                    out _entities);
            ConnectionString = connectionString;
            EntitiesDataSet = new();
            SqlQueryBuilder = queryBuilder;
            _queryExecutor = queryExecutor;
        }

        public FilterParser GetOdataFilterParser()
        {
            return _oDataFilterParser;
        }

        public DatabaseType GetDatabaseType()
        {
            return _databaseType;
        }

        /// <summary>
        /// Obtains the underlying source object's schema name.
        /// </summary>
        public virtual string GetSchemaName(string entityName)
        {
            if (!EntityToDatabaseObject.TryGetValue(entityName, out DatabaseObject? databaseObject))
            {
                throw new InvalidCastException($"Table Definition for {entityName} has not been inferred.");
            }

            return databaseObject!.SchemaName;
        }

        /// <summary>
        /// Obtains the underlying source object's name.
        /// </summary>
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
        public async Task InitializeAsync()
        {
            System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();
            GenerateDatabaseObjectForEntities();
            await PopulateTableDefinitionForEntities();
            ProcessEntityPermissions();
            InitFilterParser();
            timer.Stop();
            Console.WriteLine($"Done inferring Sql database schema in {timer.ElapsedMilliseconds}ms.");
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
            Dictionary<string, DatabaseObject> sourceObjects = new();
            foreach ((string entityName, Entity entity)
                in _entities)
            {
                if (!EntityToDatabaseObject.ContainsKey(entityName))
                {
                    // Reuse the same Database object for multiple entities if they share the same source.
                    if (!sourceObjects.TryGetValue(entity.GetSourceName(), out DatabaseObject? sourceObject))
                    {
                        sourceObject = new()
                        {
                            SchemaName = GetDefaultSchemaName(),
                            Name = entity.GetSourceName(),
                            TableDefinition = new()
                        };
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
                databaseObject.TableDefinition
                    .SourceEntityRelationshipMap[entityName] = relationshipData;
            }

            foreach (Relationship relationship in entity.Relationships!.Values)
            {
                string targetEntityName = relationship.TargetEntity;

                if (!_entities.TryGetValue(targetEntityName, out Entity? targetEntity))
                {
                    throw new InvalidOperationException("Target Entity should be one of the exposed entities.");
                }

                // If a linking object is specified,
                // give that higher preference and add two foreign keys for this targetEntity.
                if (relationship.LinkingObject is not null)
                {
                    AddForeignKeyForTargetEntity(
                        targetEntityName,
                        referencingTableName: relationship.LinkingObject,
                        referencedTableName: entity.GetSourceName(),
                        referencingColumns: relationship.LinkingSourceFields,
                        referencedColumns: relationship.SourceFields,
                        relationshipData);

                    AddForeignKeyForTargetEntity(
                        targetEntityName,
                        referencingTableName: relationship.LinkingObject,
                        referencedTableName: targetEntity.GetSourceName(),
                        referencingColumns: relationship.LinkingSourceFields,
                        referencedColumns: relationship.TargetFields,
                        relationshipData);

                    // Add the linking object as an entity for which we need to infer metadata.
                    /*if (!EntityToDatabaseObject.ContainsKey(relationship.LinkingObject))
                    {
                        DatabaseObject linkingDbObject = new()
                        {
                            SchemaName = GetDefaultSchemaName(),
                            Name = relationship.LinkingObject,
                            TableDefinition = new()
                        };

                        EntityToDatabaseObject.Add(relationship.LinkingObject, linkingDbObject);
                    }*/
                }
                else if (relationship.Cardinality == Cardinality.One)
                {
                    // Adding this foreign key in the hopes of finding a foreign key
                    // in the underlying database object of the source entity referencing
                    // the target entity.
                    // This foreign key may not exist for either of the following reasons:
                    // a. this source entity is related to the target entity in an One-to-One relationship
                    // but the foreign key was added to the target entity's underlying source
                    // OR
                    // b. no foreign keys were defined at all.
                    AddForeignKeyForTargetEntity(
                        targetEntityName,
                        referencingTableName: entity.GetSourceName(),
                        referencedTableName: targetEntity!.GetSourceName(),
                        referencingColumns: relationship.SourceFields,
                        referencedColumns: relationship.TargetFields,
                        relationshipData);

                    // Adds another foreign key defintion with targetEntity.GetSourceName()
                    // as the referencingTableName - in the situation of a One-to-One relationship
                    // and the foreign key is defined in the source of targetEntity.
                    AddForeignKeyForTargetEntity(
                        targetEntityName,
                        referencingTableName: targetEntity.GetSourceName(),
                        referencedTableName: entity.GetSourceName(),
                        referencingColumns: relationship.TargetFields,
                        referencedColumns: relationship.SourceFields,
                        relationshipData);
                }
                else if (relationship.Cardinality is Cardinality.Many)
                {
                    // Case of publisher(One)-books(Many) where books doesnt have a relationship on publisher yet
                    // we would need to obtain the foreign key information from the books table
                    // about the publisher id so we can do the join.
                    // so, the referencingTable is the source of the target entity.
                    AddForeignKeyForTargetEntity(
                        targetEntityName,
                        referencingTableName: targetEntity.GetSourceName(),
                        referencedTableName: entity.GetSourceName(),
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
            string referencingTableName,
            string referencedTableName,
            string[]? referencingColumns,
            string[]? referencedColumns,
            RelationshipMetadata relationshipData)
        {
            ForeignKeyDefinition foreignKeyDefinition = new()
            {
                Pair = new(referencingTableName, referencedTableName)
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
        /// Enrich the entities in the runtime config with the
        /// table definition information needed by the runtime to serve requests.
        /// </summary>
        private async Task PopulateTableDefinitionForEntities()
        {
            foreach (string entityName
                in EntityToDatabaseObject.Keys)
            {
                await PopulateTableDefinitionAsync(
                    GetSchemaName(entityName),
                    GetDatabaseObjectName(entityName),
                    GetTableDefinition(entityName));
            }

            await PopulateForeignKeyDefinitionAsync();

        }

        /// <summary>
        /// Processes permissions for all the entities.
        /// </summary>
        private void ProcessEntityPermissions()
        {
            foreach ((string entityName, Entity entity) in _entities)
            {
                DetermineHttpVerbPermissions(entityName, entity.Permissions);
            }
        }

        private void InitFilterParser()
        {
            _oDataFilterParser.BuildModel(EntityToDatabaseObject.Values);
        }

        /// <summary>
        /// Determines the allowed HttpRest Verbs and
        /// their authorization rules for this entity.
        /// </summary>
        private void DetermineHttpVerbPermissions(string entityName, PermissionSetting[] permissions)
        {
            TableDefinition tableDefinition = GetTableDefinition(entityName);
            foreach (PermissionSetting permission in permissions)
            {
                foreach (object action in permission.Actions)
                {
                    string actionName;
                    if (((JsonElement)action).ValueKind == JsonValueKind.Object)
                    {
                        Config.Action configAction =
                            ((JsonElement)action).Deserialize<Config.Action>()!;
                        actionName = configAction.Name;
                    }
                    else
                    {
                        actionName = ((JsonElement)action).Deserialize<string>()!;
                    }

                    OperationAuthorizationRequirement restVerb
                            = HttpRestVerbs.GetVerb(actionName);
                    if (!tableDefinition.HttpVerbs.ContainsKey(restVerb.Name.ToString()!))
                    {
                        AuthorizationRule rule = new()
                        {
                            AuthorizationType =
                              (AuthorizationType)Enum.Parse(
                                  typeof(AuthorizationType), permission.Role, ignoreCase: true)
                        };

                        tableDefinition.HttpVerbs.Add(restVerb.Name.ToString()!, rule);
                    }
                }
            }
        }

        /// <summary>
        /// Fills the table definition with information of all columns and
        /// primary keys.
        /// </summary>
        /// <param name="schemaName">Name of the schema.</param>
        /// <param name="tableName">Name of the table.</param>
        /// <param name="tableDefinition">Table definition to fill.</param>
        private async Task PopulateTableDefinitionAsync(
            string schemaName,
            string tableName,
            TableDefinition tableDefinition)
        {
            DataTable dataTable = await GetTableWithSchemaFromDataSetAsync(schemaName, tableName);

            List<DataColumn> primaryKeys = new(dataTable.PrimaryKey);
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
            string schemaName,
            string tableName)
        {
            DataTable? dataTable = EntitiesDataSet.Tables[tableName];
            if (dataTable == null)
            {
                dataTable = await FillSchemaForTableAsync(schemaName, tableName);
            }

            return dataTable;
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
            conn.ConnectionString = ConnectionString;
            await conn.OpenAsync();

            DataAdapterT adapterForTable = new();
            CommandT selectCommand = new()
            {
                Connection = conn
            };
            StringBuilder tablePrefix = new(conn.Database);
            if (!string.IsNullOrEmpty(schemaName))
            {
                tablePrefix.Append($".{schemaName}");
            }

            selectCommand.CommandText = ($"SELECT * FROM {tablePrefix}.{tableName}");
            adapterForTable.SelectCommand = selectCommand;

            DataTable[] dataTable = adapterForTable.FillSchema(EntitiesDataSet, SchemaType.Source, tableName);
            return dataTable[0];
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
                        IEnumerable<List<ForeignKeyDefinition>> foreignKeys = relationshipData.TargetEntityToFkDefinitionMap.Values;
                        // if any of the added foreign keys, don't have any reference columns,
                        // it means metadata is missing and we need to find that information from the db.
                        foreach (List<ForeignKeyDefinition> fkDefinitions in foreignKeys)
                        {
                            foreach(ForeignKeyDefinition fk in fkDefinitions)
                            {
                                schemaNames.Add(dbObject.SchemaName);
                                tableNames.Add(fk.Pair.ReferencingTable);
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
            foreach(TableDefinition tableDefinition in tablesToBePopulatedWithFK)
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
                string referencingTableName = (string)foreignKeyInfo[nameof(TableDefinition)]!;
                string referencedTableName = (string)foreignKeyInfo[nameof(ForeignKeyDefinition.Pair.ReferencedTable)]!;
                RelationShipPair pair = new(referencingTableName, referencedTableName);
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
                            // if the referencing columns count > 0, we have already gathered this information.
                            if (fk.ReferencingColumns.Count > 0)
                            {
                                continue;
                            }

                            // Add the referencing and referenced columns for this foreign key definition
                            // for the target.
                            if (pairToFkDefinition.TryGetValue(
                                    fk.Pair, out ForeignKeyDefinition? inferredDefinition))
                            {
                                fk.ReferencingColumns.AddRange(inferredDefinition.ReferencingColumns);
                                fk.ReferencedColumns.AddRange(inferredDefinition.ReferencedColumns);
                            }
                        }
                    }
                }
            }
        }
    }
}
