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
using Azure.DataGateway.Service.Parsers;
using Azure.DataGateway.Service.Resolvers;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.Options;
using Npgsql;

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
        public FilterParser ODataFilterParser { get; } = new();
        private FilterParser _oDataFilterParser = new();

        public DatabaseType DatabaseType { get; }
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
            string? schemaName, dbObjectName;
            foreach ((string entityName, Entity entity)
                in _entities)
            {
                if (!EntityToDatabaseObject.ContainsKey(entityName))
                {
                    // parse source name into a tuple of (schemaName, databaseObjectName)
                    (schemaName, dbObjectName) = ParseSchemaAndDbObjectName(entity.GetSourceName())!;

                    DatabaseObject databaseObject = new()
                    {
                        SchemaName = schemaName!,
                        Name = dbObjectName!,
                        TableDefinition = new()
                    };

                    EntityToDatabaseObject.Add(entityName, databaseObject);
                }
                

                if (entity.Relationships != null)
                {
                    // Add all the linking objects as well - so that we can infer
                    // their metadata too.
                    foreach (Relationship relationship in entity.Relationships.Values)
                    {
                        if (relationship.LinkingObject != null
                            && !EntityToDatabaseObject.ContainsKey(relationship.LinkingObject))
                        {
                            // linking object can have its own schema, so we parse and update here
                            (schemaName, dbObjectName) = ParseSchemaAndDbObjectName(relationship.LinkingObject);
                            DatabaseObject linkingDatabaseObject = new()
                            {
                                SchemaName = schemaName!,
                                Name = dbObjectName!,
                                TableDefinition = new()
                            };

                            EntityToDatabaseObject.Add(
                                relationship.LinkingObject,
                                linkingDatabaseObject);
                        }
                    }
                }
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
        /// <exception cref="DataGatewayException"></exception>
        public (string?, string?) ParseSchemaAndDbObjectName(string source)
        {
            (string? schemaName, string? dbObjectName) = EntitySourceNamesParser.ParseSchemaAndTable(source)!;

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
                if (DatabaseType is not DatabaseType.postgresql || !TryGetSchemaFromConnectionString(
                                                                    out schemaName,
                                                                    connectionString: ConnectionString))
                {
                    schemaName = GetDefaultSchemaName();
                }
            }
            else if (DatabaseType is DatabaseType.mysql)
            {
                throw new DataGatewayException(message: $"Invalid database object name: \"{schemaName}.{dbObjectName}\"",
                                               statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                                               subStatusCode: DataGatewayException.SubStatusCodes.ErrorInInitialization);
            }

            return (schemaName, dbObjectName);
        }

        /// <summary>
        /// Only used for PostgreSql.
        /// The connection string could contain the schema,
        /// in which case it will be associated with the
        /// property 'SearchPath' in the string builder we create.
        /// If `SearchPath` is null we assign the empty string to the
        /// the out param schemaName, otherwise we assign the
        /// value associated with `SearchPath`.
        /// </summary>
        /// <param name="schemaName">the schema name we save.</param>
        /// <returns>true if non empty schema in connection string, false otherwise.</returns>
        public static bool TryGetSchemaFromConnectionString(out string schemaName, string connectionString)
        {
            NpgsqlConnectionStringBuilder connectionStringBuilder = new(connectionString);
            schemaName = connectionStringBuilder.SearchPath is null ? string.Empty : connectionStringBuilder.SearchPath;
            return string.IsNullOrEmpty(schemaName) ? false : true;
        }

        /// <summary>
        /// Returns the default schema name. Throws exception here since
        /// each derived class should override this method.
        /// </summary>
        /// <exception cref="NotSupportedException"></exception>
        protected virtual string GetDefaultSchemaName()
        {
            throw new NotSupportedException($"Cannot get default schema " +
                $"name for database type {DatabaseType}");
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

            await PopulateForeignKeyDefinitionAsync(EntityToDatabaseObject.Values);

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
            ODataFilterParser.BuildModel(EntityToDatabaseObject.Values);
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
        private async Task PopulateForeignKeyDefinitionAsync(IEnumerable<DatabaseObject> databaseObjects)
        {
            // Build the query required to get the foreign key information.
            string queryForForeignKeyInfo =
                ((BaseSqlQueryBuilder)SqlQueryBuilder).BuildForeignKeyInfoQuery(databaseObjects.Count());

            // Build the array storing all the schemaNames, for now the defaultSchemaName.
            List<string> schemaNames = new();
            List<string> tableNames = new();
            Dictionary<string, TableDefinition> sourceNameToTableDefinition = new();
            foreach (DatabaseObject dbObject in databaseObjects)
            {
                schemaNames.Add(dbObject.SchemaName);
                tableNames.Add(dbObject.Name);
                sourceNameToTableDefinition.Add(dbObject.Name, dbObject.TableDefinition);
            }

            // Build the parameters dictionary for the foreign key info query
            // consisting of all schema names and table names.
            Dictionary<string, object?> parameters =
                GetForeignKeyQueryParams(
                    schemaNames.ToArray(),
                    tableNames.ToArray());

            // Execute the foreign key info query.
            using DbDataReader reader =
                await _queryExecutor!.ExecuteQueryAsync(queryForForeignKeyInfo, parameters);

            // Extract the first row from the result.
            Dictionary<string, object?>? foreignKeyInfo =
                await _queryExecutor!.ExtractRowFromDbDataReader(reader);

            // While the result is not null
            // keep populating the table definition for all tables with all foreign keys.
            while (foreignKeyInfo != null)
            {
                string tableName = (string)foreignKeyInfo[nameof(TableDefinition)]!;
                TableDefinition? tableDefinition;
                string foreignKeyName = (string)foreignKeyInfo[nameof(ForeignKeyDefinition)]!;
                ForeignKeyDefinition? foreignKeyDefinition;

                if (sourceNameToTableDefinition.TryGetValue(tableName, out tableDefinition))
                {
                    if (!tableDefinition.ForeignKeys.TryGetValue(foreignKeyName, out foreignKeyDefinition))
                    {
                        // If this is the first column in this foreign key for this table,
                        // add the referenced table to the tableDefinition.
                        foreignKeyDefinition = new()
                        {
                            ReferencedTable =
                            (string)foreignKeyInfo[nameof(ForeignKeyDefinition.ReferencedTable)]!
                        };
                        tableDefinition.ForeignKeys.Add(foreignKeyName, foreignKeyDefinition);
                    }

                    // add the referenced and referencing columns to the foreign key definition.
                    foreignKeyDefinition.ReferencedColumns.Add(
                        (string)foreignKeyInfo[nameof(ForeignKeyDefinition.ReferencedColumns)]!);
                    foreignKeyDefinition.ReferencingColumns.Add(
                        (string)foreignKeyInfo[nameof(ForeignKeyDefinition.ReferencingColumns)]!);
                }
                else
                {
                    // This should not happen.
                    throw new DataGatewayException(
                        message: "Foreign key information is retrieved for a table that is not to be exposed.",
                        statusCode: System.Net.HttpStatusCode.InternalServerError,
                        subStatusCode: DataGatewayException.SubStatusCodes.UnexpectedError);
                }

                foreignKeyInfo = await _queryExecutor.ExtractRowFromDbDataReader(reader);
            }
        }
    }
}

