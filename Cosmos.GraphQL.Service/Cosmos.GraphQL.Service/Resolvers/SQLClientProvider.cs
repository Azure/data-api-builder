using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Cosmos.GraphQL.Service.configurations;

namespace Cosmos.GraphQL.Service.Resolvers
{
    public class SQLClientProvider: IClientProvider<SqlConnection>
    {
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
