using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Models;
using Microsoft.Data.SqlClient;

namespace Azure.DataGateway.Service.Services
{
    /// <summary>
    /// Reads schema information from the database to make it available for the GraphQL service.
    /// </summary>
    public class MsSqlMetadataProvider
    {
        private const int NUMBER_OF_RESTRICTIONS = 4;
        private readonly string _connectionString;
        private DataSet _dataSet = new();
        private DatabaseSchema _databaseSchema = new();

        public MsSqlMetadataProvider(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task<DatabaseSchema> GetDatabaseSchema()
        {
            await PopulateDatabaseSchemaWithTables();

            return _databaseSchema;
        }

        private async Task PopulateDatabaseSchemaWithTables()
        {
            using SqlConnection conn = new(_connectionString);
            await conn.OpenAsync();

            // We can specify the Catalog, Schema, Table Name, Table Type to get
            // the specified table(s).
            // We can use four restrictions for Table, so we create a 4 members array.
            // These restrictions are used to limit the amount of schema information returned.
            string[] tableRestrictions = new string[NUMBER_OF_RESTRICTIONS];

            // For the array, 0-member represents Catalog; 1-member represents Schema;
            // 2-member represents Table Name; 3-member represents Table Type.
            // We only need to get all the base tables, not views or system tables.
            const string TABLE_TYPE = "BASE TABLE";
            tableRestrictions[3] = TABLE_TYPE;

            DataTable allBaseTables = conn.GetSchema("Tables", tableRestrictions);

            foreach (DataRow table in allBaseTables.Rows)
            {
                string tableName = table["TABLE_NAME"].ToString()!;

                _databaseSchema.Tables.Add(tableName, new TableDefinition());
                SqlDataAdapter adapterForTable = new(
                    selectCommandText: $"SELECT * FROM {tableName}", conn);
                adapterForTable.FillSchema(_dataSet, SchemaType.Source, tableName);

                AddColumnDefinition(tableName, _databaseSchema.Tables[tableName]);

                await PopulateColumnDefinitionWithDefault(
                    tableName, _databaseSchema.Tables[tableName]);
            }
        }

        private void AddColumnDefinition(string tableName, TableDefinition tableDefinition)
        {
            DataTable? dataTable = _dataSet.Tables[tableName];
            if (dataTable != null)
            {
                List<DataColumn> primaryKeys = new(dataTable.PrimaryKey);
                tableDefinition.PrimaryKey = new(primaryKeys.Select(primaryKey => primaryKey.ColumnName));

                using (DataTableReader reader = new(dataTable))
                {
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
        }

        private async Task PopulateColumnDefinitionWithDefault(
            string tableName,
            TableDefinition tableDefinition)
        {
            using (SqlConnection conn = new(_connectionString))
            {
                conn.ConnectionString = _connectionString;
                await conn.OpenAsync();

                // We can specify the Catalog, Schema, Table Name, Column Name to get
                // the specified column(s).
                // Hence, we should create a 4 members array.
                string[] columnRestrictions = new string[NUMBER_OF_RESTRICTIONS];

                // To restrict the columns for the current table, specify the table's name
                // in column restrictions.
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
}
