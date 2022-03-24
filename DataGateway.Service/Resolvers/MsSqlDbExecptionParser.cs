using System;
using System.Data.Common;

namespace Azure.DataGateway.Service.Resolvers
{
    public class MsSqlDbExceptionParser : IDbExceptionParser
    {
        public Exception Parse(DbException e)
        {
            // refer to https://docs.microsoft.com/en-us/sql/relational-databases/errors-events/database-engine-events-and-errors?view=sql-server-ver15
            // for error codes
            return e;
        }
    }
}
