using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
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
        /// <param name="conn">database connection</param>
        /// <param name="schemaName">not used</param>
        /// <returns>a datatable contains tables</returns>
        protected override async Task<DataTable> GetSchemaAsync(MySqlConnection conn, string schemaName)
        {
            string[] tableRestrictions = new string[1];
            const string TABLE_TYPE = "BASE TABLE";
            DataTable alltables = await conn.GetSchemaAsync("Tables", tableRestrictions);

            // Manually filter here
            // MySQL uses scema as catalog
            List<DataRow> removetables =
                alltables
                .AsEnumerable()
                .Where(table => table["TABLE_SCHEMA"].ToString()! != conn.Database ||
                        table["TABLE_TYPE"].ToString()! != TABLE_TYPE)
                .ToList();

            // Remove selected rows.
            foreach (DataRow row in removetables)
            {
                alltables.Rows.Remove(row);
            }

            return alltables;
        }
    }
}
