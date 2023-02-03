using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.DataApiBuilder.Service.Resolvers;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace Azure.DataApiBuilder.Service.Services
{
    /// <summary>
    /// MySQL specific override for SqlMetadataProvider
    /// </summary>
    public class MySqlMetadataProvider : SqlMetadataProvider<MySqlConnection, MySqlDataAdapter, MySqlCommand>, ISqlMetadataProvider
    {
        public const string MYSQL_INVALID_CONNECTION_STRING_MESSAGE = "Format of the initialization string";
        public const string MYSQL_INVALID_CONNECTION_STRING_OPTIONS = "GetOptionForKey";

        public MySqlMetadataProvider(
            RuntimeConfigProvider runtimeConfigProvider,
            IQueryExecutor queryExecutor,
            IQueryBuilder sqlQueryBuilder,
            ILogger<ISqlMetadataProvider> logger)
            : base(runtimeConfigProvider, queryExecutor, sqlQueryBuilder, logger)
        {
        }

        /// </inheritdoc>
        /// <remarks>The schema name is ignored here, since MySQL does not
        /// support 3 level naming of tables.</remarks>
        protected override async Task<DataTable> GetColumnsAsync(
            string schemaName,
            string tableName)
        {
            using MySqlConnection conn = new(ConnectionString);
            await QueryExecutor.SetManagedIdentityAccessTokenIfAnyAsync(conn);
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
            MySqlConnectionStringBuilder connBuilder = new(ConnectionString);
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
                parameters.Add(databaseNameParams[i], connBuilder.Database);
            }

            for (int i = 0; i < tableNames.Count(); ++i)
            {
                parameters.Add(tableNameParams[i], tableNames[i]);
            }

            return parameters;
        }

        /// <inheritdoc />
        public override string GetDefaultSchemaName()
        {
            return string.Empty;
        }

        /// <inheritdoc />
        protected override DatabaseTable GenerateDbTable(string schemaName, string tableName)
        {
            return new(GetDefaultSchemaName(), tableName);
        }

        /// <summary>
        /// Takes a string version of a MySql data type and returns its .NET common language runtime (CLR) counterpart
        /// TODO: For MySql stored procedure/function support, this needs to be implemented.
        /// </summary>
        public override Type SqlToCLRType(string sqlType)
        {
            throw new NotImplementedException();
        }
    }
}
