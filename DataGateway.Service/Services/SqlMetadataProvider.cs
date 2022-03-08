using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Models;

namespace Azure.DataGateway.Service.Services
{
    /// <summary>
    /// Reads schema information from the database to make it
    /// available for the GraphQL/REST services.
    /// </summary>
    public class SqlMetadataProvider<ConnectionT, DataAdapterT, CommandT> : IMetadataStoreProvider
        where ConnectionT : DbConnection, new()
        where DataAdapterT : DbDataAdapter, new()
        where CommandT : DbCommand, new()
    {
        private const int NUMBER_OF_RESTRICTIONS = 4;
        private readonly string _connectionString;
        private DataSet _dataSet = new();
        private static readonly object _syncLock = new();
        private static SqlMetadataProvider<ConnectionT, DataAdapterT, CommandT>? _singleton;

        /// <summary>
        /// The derived database schema.
        /// </summary>
        public DatabaseSchema DatabaseSchema { get; init; }

        /// <summary>
        /// Retrieves the singleton for SqlMetadataProvider
        /// with the given connection string.
        /// </summary>
        public static
        SqlMetadataProvider<ConnectionT, DataAdapterT, CommandT>
        GetSqlMetadataProvider(string connectionString)
        {
            if (_singleton == null)
            {
                lock (_syncLock)
                {
                    if (_singleton == null)
                    {
                        _singleton = new(connectionString);
                    }
                }
            }

            return _singleton;
        }

        private SqlMetadataProvider(string connectionString)
        {
            _connectionString = connectionString;
            DatabaseSchema = new();
        }

        /// <inheritdoc/>
        public TableDefinition GetTableDefinition(string name)
        {
            if (!DatabaseSchema.Tables.TryGetValue(name, out TableDefinition? metadata))
            {
                throw new KeyNotFoundException($"Table Definition for {name} does not exist.");
            }

            return metadata;
        }

        /// <summary>
        /// Refreshes the database schema with table information for the given schema.
        /// This is best effort - some table information may not be accessible so
        /// will not be retrieved.
        /// </summary>
        public async Task<DatabaseSchema> RefreshDatabaseSchemaWithTablesAsync(string schemaName)
        {
            if (!string.IsNullOrEmpty(schemaName))
            {
                using ConnectionT conn = new();
                conn.ConnectionString = _connectionString;
                await conn.OpenAsync();

                DatabaseSchema.Tables.Clear();

                // We can specify the Catalog, Schema, Table Name, Table Type to get
                // the specified table(s).
                // We can use four restrictions for Table, so we create a 4 members array.
                // These restrictions are used to limit the amount of schema information returned.
                string[] tableRestrictions = new string[NUMBER_OF_RESTRICTIONS];

                // For the array, 0-member represents Catalog; 1-member represents Schema;
                // 2-member represents Table Name; 3-member represents Table Type.
                // We only need to get all the base tables, not views or system tables.
                const string TABLE_TYPE = "BASE TABLE";
                tableRestrictions[1] = schemaName;
                tableRestrictions[3] = TABLE_TYPE;

                DataTable allBaseTables = await conn.GetSchemaAsync("Tables", tableRestrictions);

                foreach (DataRow table in allBaseTables.Rows)
                {
                    string tableName = table["TABLE_NAME"].ToString()!;

                    // For MySQL, the schema name restriction doesn't seem to
                    // work so we could end up seeing same table name.
                    // Ignore such tables.
                    if (DatabaseSchema.Tables.ContainsKey(tableName))
                    {
                        continue;
                    }

                    try
                    {
                        TableDefinition tableDefinition = new();

                        DataAdapterT adapterForTable = new();
                        CommandT selectCommand = new();
                        selectCommand.Connection = conn;
                        selectCommand.CommandText = ($"SELECT * FROM {tableName}");
                        adapterForTable.SelectCommand = selectCommand;

                        adapterForTable.FillSchema(_dataSet, SchemaType.Source, tableName);

                        AddColumnDefinition(tableName, tableDefinition);

                        await PopulateColumnDefinitionWithHasDefaultAsync(
                            schemaName,
                            tableName,
                            tableDefinition);

                        DatabaseSchema.Tables.Add(tableName, tableDefinition);
                    }
                    catch (DbException db)
                    {
                        Console.WriteLine($"Unable to get information about: {tableName}" +
                            $" due to this exception: {db.Message}");
                    }
                }
            }

            return DatabaseSchema;
        }

        /// <summary>
        /// Fills the Table definition with information of all columns and
        /// primary keys.
        /// </summary>
        /// <param name="tableName">Name of the table.</param>
        /// <param name="tableDefinition">Table definition to fill.</param>
        private void AddColumnDefinition(string tableName, TableDefinition tableDefinition)
        {
            DataTable? dataTable = _dataSet.Tables[tableName];
            if (dataTable != null)
            {
                List<DataColumn> primaryKeys = new(dataTable.PrimaryKey);
                tableDefinition.PrimaryKey = new(primaryKeys.Select(primaryKey => primaryKey.ColumnName));

                using DataTableReader reader = new(dataTable);
                DataTable schemaTable = reader.GetSchemaTable();
                foreach (DataRow columnInfoFromAdapter in schemaTable.Rows)
                {
                    string columnName = columnInfoFromAdapter["ColumnName"].ToString()!;
                    ColumnDefinition column = new();
                    column.IsNullable = (bool)columnInfoFromAdapter["AllowDBNull"];
                    column.IsAutoGenerated = (bool)columnInfoFromAdapter["IsAutoIncrement"];
                    tableDefinition.Columns.Add(columnName, column);
                }
            }
        }

        /// <summary>
        /// Populates the column definition with HasDefault property.
        /// </summary>
        private async Task PopulateColumnDefinitionWithHasDefaultAsync(
            string schemaName,
            string tableName,
            TableDefinition tableDefinition)
        {
            using ConnectionT conn = new();
            conn.ConnectionString = _connectionString;
            await conn.OpenAsync();

            // We can specify the Catalog, Schema, Table Name, Column Name to get
            // the specified column(s).
            // Hence, we should create a 4 members array.
            string[] columnRestrictions = new string[NUMBER_OF_RESTRICTIONS];

            // To restrict the columns for the current table, specify the table's name
            // in column restrictions.
            columnRestrictions[1] = schemaName;
            columnRestrictions[2] = tableName;

            // Each row in the columnsInTable table corresponds to a single column of the table.
            DataTable columnsInTable = await conn.GetSchemaAsync("Columns", columnRestrictions);

            foreach (DataRow columnInfo in columnsInTable.Rows)
            {
                string columnName = (string)columnInfo["COLUMN_NAME"];
                bool hasDefault = !string.IsNullOrEmpty(columnInfo["COLUMN_DEFAULT"].ToString());
                ColumnDefinition? columnDefinition;
                if (tableDefinition.Columns.TryGetValue(columnName, out columnDefinition))
                {
                    columnDefinition.HasDefault = hasDefault;
                }
            }
        }

        /// <inheritdoc/>
        public string GetGraphQLSchema()
        {
            throw new System.NotImplementedException();
        }

        /// <inheritdoc/>
        public MutationResolver GetMutationResolver(string name)
        {
            throw new System.NotImplementedException();
        }

        /// <inheritdoc/>
        public GraphQLType GetGraphQLType(string name)
        {
            throw new System.NotImplementedException();
        }

        /// <inheritdoc/>
        public ResolverConfig GetResolvedConfig()
        {
            throw new System.NotImplementedException();
        }

        /// <inheritdoc/>
        public FilterParser GetFilterParser()
        {
            throw new System.NotImplementedException();
        }
    }
}
