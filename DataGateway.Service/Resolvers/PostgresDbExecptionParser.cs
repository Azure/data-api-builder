using System;
using System.Data.Common;
using System.Net;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Exceptions;
using Microsoft.Extensions.Options;

namespace Azure.DataGateway.Service.Resolvers
{
    public class PostgresDbExceptionParser : DbExceptionParserBase
    {
        public const string FK_VIOLATION_MESSAGE = "PostgreSql Error 23503: Foreign Key Constraint Violation.";
        public const string FK_VIOLATION_CODE = "23503";
        public const string UNQIUE_VIOLATION_MESSAGE = "PostgreSql Error 23505: Unique Constraint Violation.";
        public const string UNQIUE_VIOLATION_CODE = "23505";

        public PostgresDbExceptionParser(IOptionsMonitor<RuntimeConfigPath> config) : base(config)
        {
        }

        public override Exception Parse(DbException e)
        {
            // refer to https://www.postgresql.org/docs/current/errcodes-appendix.html
            // for error codes
            switch (e.SqlState)
            {
                case FK_VIOLATION_CODE:
                    return new DataGatewayException(
                        message: FK_VIOLATION_MESSAGE,
                        statusCode: HttpStatusCode.Conflict,
                        subStatusCode: DataGatewayException.SubStatusCodes.DatabaseOperationFailed
                    );
                case UNQIUE_VIOLATION_CODE: // unique constraint violation
                    return new DataGatewayException(
                        message: UNQIUE_VIOLATION_MESSAGE,
                        statusCode: HttpStatusCode.Conflict,
                        subStatusCode: DataGatewayException.SubStatusCodes.DatabaseOperationFailed
                    );
                default:
                    return base.Parse(e);
            }
        }
    }
}
