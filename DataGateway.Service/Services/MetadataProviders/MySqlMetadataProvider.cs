using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Models;
using MySqlConnector;

namespace Azure.DataGateway.Service.Services
{
    /// <summary>
    /// MySQL specific override for SqlMetadataProvider
    /// </summary>
    public class MySqlMetadataProvider : SqlMetadataProvider<MySqlConnection, MySqlDataAdapter, MySqlCommand>
    {
        public MySqlMetadataProvider(string connectionString)
            : base(connectionString)
        {
        }

        /// <summary>
        /// Get the schema information for one database.
        /// Since MySQL connector doesn't support filtering, filter the table manually here
        /// </summary>
        /// <param name="schemaName">not used</param>
        /// <returns>a datatable contains tables</returns>
        protected override async Task PopulateColumnDefinitionWithHasDefaultAsync(
            string schemaName,
            string tableName,
            TableDefinition tableDefinition)
        {
            using MySqlConnection conn = new();
            conn.ConnectionString = ConnectionString;
            await conn.OpenAsync();

            // Each row in the allColumns table corresponds to a single column of the table.
            DataTable allColumns = await conn.GetSchemaAsync("Columns");

            // Manually filter here
            // MySQL uses schema as catalog
            List<DataRow> removeColumns =
                allColumns
                .AsEnumerable()
                .Where(column => column["TABLE_SCHEMA"].ToString()! != conn.Database ||
                        column["TABLE_NAME"].ToString()! != tableName)
                .ToList();

            // Remove selected rows.
            foreach (DataRow row in removeColumns)
            {
                allColumns.Rows.Remove(row);
            }

            foreach (DataRow columnInfo in allColumns.Rows)
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

        /// <summary>
        /// Using a data adapter, obtains the schema of the given table name
        /// and adds the corresponding entity in the data set.
        /// </summary>
        protected override async Task<DataTable> FillSchemaForTable(
            string schemaName,
            string tableName)
        {
            using MySqlConnection conn = new();
            conn.ConnectionString = ConnectionString;
            await conn.OpenAsync();

            MySqlDataAdapter adapterForTable = new();
            MySqlCommand selectCommand = new();
            selectCommand.Connection = conn;
            selectCommand.CommandText = ($"SELECT * FROM {conn.Database}.{tableName}");
            adapterForTable.SelectCommand = selectCommand;

            DataTable[] dataTable = adapterForTable.FillSchema(EntitiesDataSet, SchemaType.Source, tableName);
            return dataTable[0];
        }
    }
}
