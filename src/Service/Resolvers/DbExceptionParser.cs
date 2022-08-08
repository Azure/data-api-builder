using System;
using System.Data.Common;
using System.Net;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.DataApiBuilder.Service.Exceptions;

namespace Azure.DataApiBuilder.Service.Resolvers
{
    ///<summary>
    /// Parses some db exceptions and converts them to useful exceptions that can be reported
    /// to the user
    ///</summary>
    public class DbExceptionParser
    {
        public const string GENERIC_DB_EXCEPTION_MESSAGE = "While processing your request the database ran into an error.";
        private readonly bool _developerMode;

        public DbExceptionParser(RuntimeConfigProvider configProvider)
        {
            _developerMode = configProvider.IsDeveloperMode();
        }

        public virtual Exception Parse(DbException e)
        {
            string message = _developerMode ? e.Message : GENERIC_DB_EXCEPTION_MESSAGE;
            return new DataApiBuilderException(
                message: message,
                statusCode: HttpStatusCode.InternalServerError,
                subStatusCode: DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed
            );
        }
    }
}
