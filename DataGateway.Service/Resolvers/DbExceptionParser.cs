using System;
using System.Data.Common;
using System.Net;
using Azure.DataGateway.Service.Configurations;
using Azure.DataGateway.Service.Exceptions;

namespace Azure.DataGateway.Service.Resolvers
{
    ///<summary>
    /// Parses some db exceptions and converts them to useful exceptions that can be reported
    /// to the user
    ///</summary>
    public class DbExceptionParser
    {
        public const string GENERIC_DB_EXCEPTION_MESSAGE = "While processing your request the database ran into an error.";
        private readonly bool _developerMode;

        public DbExceptionParser(RuntimeConfigProvider config)
        {
            _developerMode = config.IsDeveloperMode();
        }

        public virtual Exception Parse(DbException e)
        {
            string message = _developerMode ? e.Message : GENERIC_DB_EXCEPTION_MESSAGE;
            return new DataGatewayException(
                message: message,
                statusCode: HttpStatusCode.InternalServerError,
                subStatusCode: DataGatewayException.SubStatusCodes.DatabaseOperationFailed
            );
        }
    }
}
