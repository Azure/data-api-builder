using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Configurations;
using Azure.DataGateway.Service.Resolvers;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Azure.DataGateway.Service.Services
{
    /// <summary>
    /// MySQL specific override for SqlMetadataProvider
    /// </summary>
    public class MySqlMetadataProvider : SqlMetadataProvider<MySqlConnection, MySqlDataAdapter, MySqlCommand>, ISqlMetadataProvider
    {
        public MySqlMetadataProvider(
            IOptions<DataGatewayConfig> dataGatewayConfig,
            IQueryExecutor queryExecutor,
            IQueryBuilder sqlQueryBuilder)
            : base(dataGatewayConfig, queryExecutor, sqlQueryBuilder)
        {
        }

        /// </inheritdoc>
        /// <remarks>The schema name is ignored here, since MySQL does not
        /// support 3 level naming of tables.</remarks>
        protected override async Task<DataTable> GetColumnsAsync(
            string schemaName,
            string tableName)
        {
            using MySqlConnection conn = new();
            conn.ConnectionString = ConnectionString;
            await conn.OpenAsync();

            // Each row in the allColumns table corresponds to a single column.
            // Since column restrictions are ignored, this retrieves all the columns
            // in the engine irrespective of database and table name.
            DataTable allColumns = await conn.GetSchemaAsync("Columns");

            // Manually filter here to find out which columns need to be removed
            // by checking the database name and table name.
            // MySQL uses schema as catalog
            List<DataRow> removeColumns =
                allColumns
                .AsEnumerable()
                .Where(column => column["TABLE_SCHEMA"].ToString()! != conn.Database ||
                        column["TABLE_NAME"].ToString()! != tableName)
                .ToList();

            // Remove unnecessary columns.
            foreach (DataRow row in removeColumns)
            {
                allColumns.Rows.Remove(row);
            }

            return allColumns;
        }

        /// <inheritdoc />
        /// <remarks>For MySql, the table name is only a 2 part name.
        /// The database name from the connection string needs to be used instead of schemaName.
        /// </remarks>
        protected override string GetForeignKeyQuery(string schemaName, string tableName)
        {
            using MySqlConnection conn = new(ConnectionString);
            return SqlQueryBuilder!.BuildForeignKeyQuery(conn.Database, tableName);
        }

        /// <inheritdoc />
        /// <remarks>For MySql, the table name is only a 2 part name.
        /// The database name from the connection string needs to be used instead of schemaName.
        /// </remarks>
        protected override Dictionary<string, object?>
            GetForeignKeyQueryParams(string schemaName, string tableName)
        {
            using MySqlConnection conn = new(ConnectionString);
            string databaseName = conn.Database;
            Dictionary<string, object?> parameters = new();
            parameters.Add(nameof(databaseName), databaseName);
            parameters.Add(nameof(tableName), tableName);
            return parameters;
        }
    }
}
