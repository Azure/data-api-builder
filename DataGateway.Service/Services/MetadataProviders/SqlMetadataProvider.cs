using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Configurations;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Resolvers;
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
        readonly IRuntimeConfigProvider _runtimeConfigProvider;

        public FilterParser ODataFilterParser { get; private set; } = new();

        // nullable since Mock tests do not need it.
        // TODO: Refactor the Mock tests to remove the nullability here
        // once the runtime config is implemented tracked by #353.
        private readonly IQueryExecutor? _queryExecutor;

        private const int NUMBER_OF_RESTRICTIONS = 4;

        protected const string TABLE_TYPE = "BASE TABLE";

        protected string ConnectionString { get; init; }

        protected IQueryBuilder SqlQueryBuilder { get; init; }

        protected DataSet EntitiesDataSet { get; init; }

        /// <summary>
        /// Maps an entity name to a DatabaseObject.
        /// </summary>
        public Dictionary<string, DatabaseObject> EntityToDatabaseObject { get; set; } =
            new(StringComparer.InvariantCultureIgnoreCase);

        public SqlMetadataProvider(
            IOptions<DataGatewayConfig> dataGatewayConfig,
            IRuntimeConfigProvider runtimeConfigProvider,
            IQueryExecutor queryExecutor,
            IQueryBuilder queryBuilder)
        {
            ConnectionString = dataGatewayConfig.Value.DatabaseConnection.ConnectionString;
            EntitiesDataSet = new();
            SqlQueryBuilder = queryBuilder;
            _queryExecutor = queryExecutor;
            _runtimeConfigProvider = runtimeConfigProvider;
        }

        /// <summary>
        /// Default Constructor for Mock tests.
        /// </summary>
        public SqlMetadataProvider()
        {
            ConnectionString = new(string.Empty);
            EntitiesDataSet = new();
            SqlQueryBuilder = new MsSqlQueryBuilder();
        }

        /// <summary>
        /// Obtains the underlying TableDefinition for the given entity name.
        /// </summary>
        public virtual TableDefinition GetTableDefinition(string name)
        {
            if (EntityToDatabaseObject.TryGetValue(name, out DatabaseObject? databaseObject))
            {
                throw new InvalidCastException($"Table Definition for {name} has not been inferred.");
            }

            return databaseObject!.TableDefinition;
        }

        /// <summary>
        /// Obtains the underlying source object's name.
        /// </summary>
        public virtual string GetDatabaseObjectName(string name)
        {
            if (EntityToDatabaseObject.TryGetValue(name, out DatabaseObject? databaseObject))
            {
                throw new InvalidCastException($"Table Definition for {name} has not been inferred.");
            }

            return databaseObject!.Name;
        }

        /// <summary>
        /// Obtains the underlying source object's schema name.
        /// </summary>
        public virtual string GetSchemaName(string name)
        {
            if (EntityToDatabaseObject.TryGetValue(name, out DatabaseObject? databaseObject))
            {
                throw new InvalidCastException($"Table Definition for {name} has not been inferred.");
            }

            return databaseObject!.SchemaName;
        }

        /// </inheritdoc>
        public async Task InitializeAsync()
        {
            System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();
            await FindDatabaseObjectForEntities();
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
        /// Enrich the entities in the runtime config with the
        /// table definition information needed by the runtime to serve requests.
        /// </summary>
        private async Task FindDatabaseObjectForEntities()
        {
            foreach (string entityName
                in GetEntitiesFromRuntimeConfig().Keys)
            {
                await PopulateTableDefinitionAsync(
                    GetSchemaName(entityName),
                    GetDatabaseObjectName(entityName),
                    GetTableDefinition(entityName));
            }
 
            await PopulateForeignKeyDefinitionAsync(
                schemaName,
                RuntimeConfig.Entities.Values);

        }

        /// <summary>
        /// Processes permissions for all the entities.
        /// </summary>
        private void ProcessEntityPermissions()
        {
            Dictionary<string, Entity> entities = GetEntitiesFromRuntimeConfig();
            foreach ((string entityName, Entity entity) in entities)
            {
                DetermineHttpVerbPermissions(entityName, entity.Permissions);
            }
        }

        private Dictionary<string, Entity> GetEntitiesFromRuntimeConfig()
        {
            return _runtimeConfigProvider.GetRuntimeConfig().Entities;
        }

        private void InitFilterParser()
        {
            ODataFilterParser.BuildModel(RuntimeConfig.Entities);
        }

        /// <summary>
        /// Determines the allowed HttpRest Verbs and
        /// their authorization rules for this entity.
        /// </summary>
        public override void DetermineHttpVerbPermissions(string entityName, PermissionSetting[] permissions)
        {
            TableDefinition tableDefinition = DatabaseSchema.EntityToDatabaseObject[entityName].TableDefinition;
            foreach (PermissionSetting permission in permissions)
            {
                foreach (object action in permission.Actions)
                {
                    string actionName;
                    if (((JsonElement)action).ValueKind == JsonValueKind.Object)
                    {
                        Action configAction =
                            ((JsonElement)action).Deserialize<Action>()!;
                        actionName = configAction.Name;
                    }
                    else
                    {
                        actionName = ((JsonElement)action).Deserialize<string>()!;
                    }

                    OperationAuthorizationRequirement restVerb
                            = HttpRestVerbs.GetVerb(actionName);
                    AuthorizationRule rule = new()
                    {
                        AuthorizationType =
                            (AuthorizationType)Enum.Parse(
                                typeof(AuthorizationType), permission.Role)
                    };

                    TableDefinition.HttpVerbs.Add(restVerb.ToString()!, rule);
                }
            }
        }

        /// </inheritdoc>
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

        /// </inheritdoc>
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
        private async Task<DataTable> GetColumnsAsync(
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

        /// <inheritdoc />
        private async Task PopulateForeignKeyDefinitionAsync(
           string defaultSchemaName,
           IEnumerable<Entity> sqlEntities)
        {
            // Build the query required to get the foreign key information.
            string queryForForeignKeyInfo =
                ((BaseSqlQueryBuilder)SqlQueryBuilder).BuildForeignKeyInfoQuery(sqlEntities.Count());

            // Build the array storing all the schemaNames, for now the defaultSchemaName.
            string[] schemaNames =
                Enumerable.Range(1, sqlEntities.Count())
                .Select(x => defaultSchemaName).ToArray();

            // Build a dictionary mapping source entity name to its table definition
            // for efficient lookups later to populate the retrieved foreign keys.
            Dictionary<string, TableDefinition> sourceTableDefinition = new();
            foreach (SqlEntity sqlEntity in sqlEntities)
            {
                // Perhaps, save this dictionary in the RuntimeConfig if useful later on.
                sourceTableDefinition.Add(sqlEntity.SourceName, sqlEntity.TableDefinition);
            }

            // Build the parameters dictionary for the foreign key info query
            // consisting of all schema names and table names.
            Dictionary<string, object?> parameters =
                GetForeignKeyQueryParams(
                    schemaNames,
                    sourceTableDefinition.Keys.ToArray());

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
                string twoPartTableName = (string)foreignKeyInfo[nameof(TableDefinition)]!;
                TableDefinition? tableDefinition;
                string foreignKeyName = (string)foreignKeyInfo[nameof(ForeignKeyDefinition)]!;
                ForeignKeyDefinition? foreignKeyDefinition;

                if (sourceTableDefinition.TryGetValue(twoPartTableName, out tableDefinition))
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
