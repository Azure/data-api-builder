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
    public class SqlMetadataProvider<ConnectionT, DataAdapterT, CommandT>
        where ConnectionT : DbConnection, new()
        where DataAdapterT : DbDataAdapter, new()
        where CommandT : DbCommand, new()
    {
        private const int NUMBER_OF_RESTRICTIONS = 4;
        private DataSet _dataSet = new();

        protected const string TABLE_TYPE = "BASE TABLE";

        protected string ConnectionString { get; init; }

        /// <summary>
        /// The derived database schema.
        /// </summary>
        public DatabaseSchema DatabaseSchema { get; init; }

        public SqlMetadataProvider(string connectionString)
        {
            ConnectionString = connectionString;
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
                DatabaseSchema.Tables.Clear();

                DataTable allBaseTables = await GetSchemaAsync(schemaName);

                await PopulateDatabaseSchemaWithTableDefinition(allBaseTables);
            }

            return DatabaseSchema;
        }

        /// <summary>
        /// Get the schema information for one database.
        /// </summary>
        /// <param name="schemaName">schema name</param>
        /// <returns>a datatable contains tables</returns>
        protected virtual async Task<DataTable> GetSchemaAsync(string schemaName)
        {
            using ConnectionT conn = new();
            conn.ConnectionString = ConnectionString;
            await conn.OpenAsync();

            // We can specify the Catalog, Schema, Table Name, Table Type to get
            // the specified table(s).
            // We can use four restrictions for Table, so we create a 4 members array.
            // These restrictions are used to limit the amount of schema information returned.
            string[] tableRestrictions = new string[NUMBER_OF_RESTRICTIONS];

            // For the array, 0-member represents Catalog; 1-member represents Schema;
            // 2-member represents Table Name; 3-member represents Table Type.
            // We only need to get all the base tables, not views or system tables.
            tableRestrictions[1] = schemaName;
            tableRestrictions[3] = TABLE_TYPE;

            DataTable allBaseTables = await conn.GetSchemaAsync("Tables", tableRestrictions);

            return allBaseTables;
        }

        /// <summary>
        /// Populates the database schema with all the table definitions
        /// obtained from the DataTable format of the base tables.
        /// </summary>
        private async Task PopulateDatabaseSchemaWithTableDefinition(DataTable allBaseTables)
        {
            using ConnectionT conn = new();
            conn.ConnectionString = ConnectionString;
            await conn.OpenAsync();

            foreach (DataRow table in allBaseTables.Rows)
            {
                string tableName = table["TABLE_NAME"].ToString()!;
                string schemaName = table["TABLE_SCHEMA"].ToString()!;
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
            conn.ConnectionString = ConnectionString;
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
    }
}
