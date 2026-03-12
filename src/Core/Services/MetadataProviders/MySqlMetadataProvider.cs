// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace Azure.DataApiBuilder.Core.Services
{
    /// <summary>
    /// MySQL specific override for SqlMetadataProvider
    /// </summary>
    public class MySqlMetadataProvider : SqlMetadataProvider<MySqlConnection, MySqlDataAdapter, MySqlCommand>, ISqlMetadataProvider
    {
        public const string MYSQL_INVALID_CONNECTION_STRING_MESSAGE = "Format of the initialization string";
        public const string MYSQL_INVALID_CONNECTION_STRING_OPTIONS = "GetOptionForKey";
        private readonly string _databaseName = "mysql";

        public MySqlMetadataProvider(
            RuntimeConfigProvider runtimeConfigProvider,
            RuntimeConfigValidator runtimeConfigValidator,
            IAbstractQueryManagerFactory queryManagerFactory,
            ILogger<ISqlMetadataProvider> logger,
            string dataSourceName,
            bool isValidateOnly = false)
            : base(runtimeConfigProvider, runtimeConfigValidator, queryManagerFactory, logger, dataSourceName, isValidateOnly)
        {
            try
            {
                using MySqlConnection conn = new(ConnectionString);
                _databaseName = conn.Database;
            }
            catch
            {
                logger.LogWarning("Could not determine database name from the connection string. The default database name 'mysql' will be used.");
            }
        }

        /// </inheritdoc>
        /// <remarks>The schema name is ignored here, since MySQL does not
        /// support 3 level naming of tables.</remarks>
        protected override async Task<DataTable> GetColumnsAsync(
            string schemaName,
            string tableName)
        {
            using MySqlConnection conn = new(ConnectionString);
            await QueryExecutor.SetManagedIdentityAccessTokenIfAnyAsync(conn, _dataSourceName);
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
        protected override Dictionary<string, DbConnectionParam>
            GetForeignKeyQueryParams(
                string[] schemaNames,
                string[] tableNames)
        {
            MySqlConnectionStringBuilder connBuilder = new(ConnectionString);
            Dictionary<string, DbConnectionParam> parameters = new();

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
                parameters.Add(databaseNameParams[i], new(connBuilder.Database, DbType.String));
            }

            for (int i = 0; i < tableNames.Count(); ++i)
            {
                parameters.Add(tableNameParams[i], new(tableNames[i], DbType.String));
            }

            return parameters;
        }

        /// <inheritdoc />
        public override string GetDefaultSchemaName()
        {
            return string.Empty;
        }

        /// <inheritdoc/>
        public override string GetDatabaseName()
        {
            return _databaseName;
        }

        /// <inheritdoc />
        protected override DatabaseTable GenerateDbTable(string schemaName, string tableName)
        {
            return new(GetDefaultSchemaName(), tableName);
        }

        /// <summary>
        /// Takes a string version of a MySql data type and returns its .NET common language runtime (CLR) counterpart
        /// TODO: For MySql stored procedure support, this needs to be implemented.
        /// </summary>
        public override Type SqlToCLRType(string sqlType)
        {
            throw new NotImplementedException();
        }
    }
}
