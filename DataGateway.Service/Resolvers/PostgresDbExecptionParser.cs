using System;
using System.Data.Common;
using System.Net;
using Azure.DataGateway.Service.Exceptions;

namespace Azure.DataGateway.Service.Resolvers
{
    public class PostgresDbExceptionParser : DbExceptionParserBase
    {
        public const string FK_VIOLATION = "PostgreSql Error 23503: Foreign Key Constraint Violation.";
        public const string UNQIUE_VIOLATION = "PostgreSql Error 23505: Unique Constraint Violation.";

        public override Exception Parse(DbException e)
        {
            // refer to https://www.postgresql.org/docs/current/errcodes-appendix.html
            // for error codes
            switch (e.SqlState)
            {
                case "23503":
                    return new DataGatewayException(
                        message: FK_VIOLATION,
                        statusCode: HttpStatusCode.Conflict,
                        subStatusCode: DataGatewayException.SubStatusCodes.DatabaseOperationFailed
                    );
                case "23505": // unique constraint violation
                    return new DataGatewayException(
                        message: UNQIUE_VIOLATION,
                        statusCode: HttpStatusCode.Conflict,
                        subStatusCode: DataGatewayException.SubStatusCodes.DatabaseOperationFailed
                    );
                default:
                    return base.Parse(e);
            }
        }
    }
}
