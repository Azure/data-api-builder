using System;
using Microsoft.Sql.Rest.Utils;

namespace Cosmos.GraphQL.Service.Resolvers
{
    public class SQLClientProvider: IClientProvider<DbConnectionService>
    {
        private static DbConnectionService _sqlConnectionService;

        public SQLClientProvider(IDbConnectionService dbConnectionService)
        {
            _sqlConnectionService = (DbConnectionService)dbConnectionService;
        }

        public DbConnectionService getClient()
        {
            return _sqlConnectionService;
        }
    }
}
