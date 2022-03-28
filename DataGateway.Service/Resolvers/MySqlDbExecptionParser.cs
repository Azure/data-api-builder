using System;
using System.Data.Common;
using System.Net;
using Azure.DataGateway.Service.Exceptions;

namespace Azure.DataGateway.Service.Resolvers
{
    public class MySqlDbExceptionParser : DbExceptionParserBase
    {
        public const string INTEGRITY_CONSTRAINT_VIOLATION = "MySql Error 23000: Integrity Contraint Violation.";

        public override Exception Parse(DbException e)
        {
            // refer to https://dev.mysql.com/doc/connector-odbc/en/connector-odbc-reference-errorcodes.html
            // for error codes
            switch (e.SqlState)
            {
                case "23000":
                    return new DataGatewayException(
                        message: INTEGRITY_CONSTRAINT_VIOLATION,
                        statusCode: HttpStatusCode.Conflict,
                        subStatusCode: DataGatewayException.SubStatusCodes.DatabaseOperationFailed
                    );
                default:
                    return base.Parse(e);
            }
        }
    }
}
