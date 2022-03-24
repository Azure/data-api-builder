using System;
using System.Data.Common;

namespace Azure.DataGateway.Service.Resolvers
{
    ///<summary>
    /// Parses some db exceptions and converts them to useful exceptions that can be reported
    /// to the user
    ///</summary>
    public interface IDbExceptionParser
    {
        public Exception Parse(DbException e);
    }
}
