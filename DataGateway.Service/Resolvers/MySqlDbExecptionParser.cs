using System;
using System.Data.Common;
using System.Net;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Exceptions;

namespace Azure.DataGateway.Service.Resolvers
{
    public class MySqlDbExceptionParser : DbExceptionParserBase
    {
        public const string INTEGRITY_CONSTRAINT_VIOLATION_MESSAGE = "MySql Error 23000: Integrity Contraint Violation.";
        public const string INTEGRITY_CONSTRAINT_VIOLATION_CODE = "23000";

        public override Exception Parse(DbException e, HostModeType mode)
        {
            // refer to https://dev.mysql.com/doc/connector-odbc/en/connector-odbc-reference-errorcodes.html
            // for error codes
            switch (e.SqlState)
            {
                case INTEGRITY_CONSTRAINT_VIOLATION_CODE:
                    return new DataGatewayException(
                        message: INTEGRITY_CONSTRAINT_VIOLATION_MESSAGE,
                        statusCode: HttpStatusCode.Conflict,
                        subStatusCode: DataGatewayException.SubStatusCodes.DatabaseOperationFailed
                    );
                default:
                    return base.Parse(e, mode);
            }
        }
    }
}
