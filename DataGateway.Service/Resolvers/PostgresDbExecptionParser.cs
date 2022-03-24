using System;
using System.Data.Common;
using System.Net;
using Azure.DataGateway.Service.Exceptions;

namespace Azure.DataGateway.Service.Resolvers
{
    public class PostgresDbExceptionParser : IDbExceptionParser
    {
        public Exception Parse(DbException e)
        {
            // refer to https://www.postgresql.org/docs/current/errcodes-appendix.html
            // for error codes
            switch (e.SqlState)
            {
                case "23503": // foreign key violation
                case "23505": // unique constraint violation
                    return new DataGatewayException(
                        message: e.Message,
                        statusCode: HttpStatusCode.InternalServerError,
                        subStatusCode: DataGatewayException.SubStatusCodes.DatabaseOperationFailed
                    );
                default:
                    return e;
            }
        }
    }
}
