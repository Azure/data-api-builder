using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Azure.DataGateway.Config;
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
            IOptionsMonitor<RuntimeConfigPath> runtimeConfigPath,
            IQueryExecutor queryExecutor,
            IQueryBuilder sqlQueryBuilder)
            : base(runtimeConfigPath, queryExecutor, sqlQueryBuilder)
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
        protected override Dictionary<string, object?>
            GetForeignKeyQueryParams(
                string[] schemaNames,
                string[] tableNames)
        {
            using MySqlConnection conn = new(ConnectionString);
            Dictionary<string, object?> parameters = new();

            string[] databaseNameParams =
                BaseSqlQueryBuilder.CreateParams(
                    kindOfParam: MySqlQueryBuilder.DATABASE_NAME_PARAM,
                    schemaNames.Count());
            string[] tableNameParams =
                BaseSqlQueryBuilder.CreateParams(
                    kindOfParam: BaseSqlQueryBuilder.TABLE_NAME_PARAM,
                    tableNames.Count());

            for (int i = 0; i < schemaNames.Count(); ++i)
            {
                parameters.Add(databaseNameParams[i], conn.Database);
            }

            for (int i = 0; i < tableNames.Count(); ++i)
            {
                parameters.Add(tableNameParams[i], tableNames[i]);
            }

            return parameters;
        }

        protected override string GetDefaultSchemaName()
        {
            return string.Empty;
        }
    }
}
