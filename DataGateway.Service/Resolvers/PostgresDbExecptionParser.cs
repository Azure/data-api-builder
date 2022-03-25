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
                case "23503":
                    return new DataGatewayException(
                        message: $"PostgreSql Error {e.SqlState}: Foreign Key Constraint Violation.",
                        statusCode: HttpStatusCode.InternalServerError,
                        subStatusCode: DataGatewayException.SubStatusCodes.DatabaseOperationFailed
                    );
                case "23505": // unique constraint violation
                    return new DataGatewayException(
                        message: $"PostgreSql Error {e.SqlState}: Unique Constraint Violation.",
                        statusCode: HttpStatusCode.InternalServerError,
                        subStatusCode: DataGatewayException.SubStatusCodes.DatabaseOperationFailed
                    );
                default:
                    return e;
            }
        }
    }
}
