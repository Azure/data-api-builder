using System;
using System.Data.Common;
using System.Net;
using Azure.DataGateway.Service.Exceptions;
namespace Azure.DataGateway.Service.Resolvers
{
    ///<summary>
    /// Parses some db exceptions and converts them to useful exceptions that can be reported
    /// to the user
    ///</summary>
    public class DbExceptionParserBase
    {
        public const string GENERIC_DB_EXCEPTION_MESSAGE = "While processing your request the database ran into an unexpected error.";
        public virtual Exception Parse(DbException e)
        {
            return new DataGatewayException(
                message: DbExceptionParserBase.GENERIC_DB_EXCEPTION_MESSAGE,
                statusCode: HttpStatusCode.InternalServerError,
                subStatusCode: DataGatewayException.SubStatusCodes.DatabaseOperationFailed
            );
        }
    }
}
