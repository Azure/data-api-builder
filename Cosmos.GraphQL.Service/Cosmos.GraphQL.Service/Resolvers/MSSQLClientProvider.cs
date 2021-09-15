using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Cosmos.GraphQL.Service.configurations;

namespace Cosmos.GraphQL.Service.Resolvers
{
    /// <summary>
    /// Creates, returns, and maintains SqlConnection for all resources that make SQL database calls.
    /// </summary>
    public class MSSQLClientProvider: IClientProvider<SqlConnection>
    {
        /// <summary>
        /// Connection object shared across engines that require database access.
        /// </summary>
        private static SqlConnection _sqlConnection;
        private static readonly object syncLock = new object();

        private void init()
        {
            _sqlConnection = new SqlConnection(ConfigurationProvider.getInstance().Creds.ConnectionString);
        }

        public SqlConnection getClient()
        {
            if (_sqlConnection == null)
            {
                lock (syncLock)
                {
                    if (_sqlConnection == null)
                    {
                        init();
                    }
                }
            }

            return _sqlConnection;
        }
    }
}
