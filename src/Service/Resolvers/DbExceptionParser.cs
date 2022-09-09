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
    public abstract class DbExceptionParser
    {
        public const string GENERIC_DB_EXCEPTION_MESSAGE = "While processing your request the database ran into an error.";
        protected readonly bool _developerMode;

        public DbExceptionParser(RuntimeConfigProvider configProvider)
        {
            _developerMode = configProvider.IsDeveloperMode();
        }

        public Exception Parse(DbException e)
        {
            string message = _developerMode ? e.Message : GENERIC_DB_EXCEPTION_MESSAGE;
            HttpStatusCode statusCode = IsBadRequestException(e) ? HttpStatusCode.BadRequest : HttpStatusCode.InternalServerError;
            return new DataApiBuilderException(
                message: message,
                statusCode: statusCode,
                subStatusCode: DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed
            );
        }

        /// <summary>
        /// Helper method to check whether the exception is considered to have occurred because
        /// of a bad request issued by the client.
        /// </summary>
        /// <param name="e">The exception thrown as a result of execution of the request.</param>
        /// <returns>true/false</returns>
        public virtual bool IsBadRequestException(DbException e)
        {
            return true;
        }
    }
}
