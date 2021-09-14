using System;
using System.Threading.Tasks;
using LruCacheNet;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Microsoft.Sql.Rest.Utils
{
    /// <summary>
    /// Service for managing the database connection.
    /// </summary>
    public class DbConnectionService : IDbConnectionService
    {
        private readonly LruCache<string, string> _connStringCache = new(10_000);
        private readonly ILogger _logger;
        private readonly string _server;
        private readonly string _proxyUser;
        private readonly string _proxyPassword;

        /// <summary>
        /// Initializes a new instance of the <see cref="DbConnectionService"/> class.
        /// given an Ilogger and server name.
        /// </summary>
        public DbConnectionService(ILogger logger, string server)
        {
            _logger = logger;
            _server = server;
        }

        /// <summary>
        /// Get a connection to a database. Cache the connection string after the first access.
        /// </summary>
        /// <param name="databaseName">Database name.</param>
        /// <returns>Opened sql connection.</returns>
        public async Task<SqlConnection> GetOpenedConnection(string databaseName)
        {
            string connString = GetConnectionString(databaseName);

            SqlConnection conn = new(connString);
            await conn.OpenAsync();
            return conn;
        }

        /// <summary>
        /// Get a connection string to a given database.
        /// </summary>
        /// <param name="databaseName">Database name.</param>
        /// <returns></returns>
        public string GetConnectionString(string databaseName)
        {
            if(!_connStringCache.TryGetValue(databaseName, out string connString))
            {
                SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder
                {
                    InitialCatalog = databaseName,
                    DataSource = _server,
                };

                // Use SQL Login as a proxy user if it exists.
                // Otherwise, use integrated security.
                if(!string.IsNullOrWhiteSpace(_proxyUser) && !string.IsNullOrWhiteSpace(_proxyPassword))
                {
                    builder.UserID = _proxyUser;
                    builder.Password = _proxyPassword;
                }
                else
                {
                    builder.IntegratedSecurity = true;
                }

                _connStringCache[databaseName] = connString = builder.ToString();

                if(_logger != null)
                {
                    _logger.LogTrace($"Create and cache new connection string for {databaseName}");
                }
            }

            return connString;
        }
    }
}
