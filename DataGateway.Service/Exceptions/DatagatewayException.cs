using System;

namespace Azure.DataGateway.Service.Exceptions
{
    /// <summary>
    /// Represents an exception thrown from the Datagateway service.
    /// Message and http statusCode will be returned to the user but
    /// subStatus code is not returned.
    /// </summary>
#pragma warning disable CA1032 // Supressing since we only use the 3 argument constructor
    public class DatagatewayException : Exception
    {
        public enum SubStatusCodes
        {
            /// <summary>
            /// The given request was invalid and could not be handled. This only includes
            /// validation errors that do not require access to the database. So only the server config and the request itself
            /// </summary>
            BadRequest,
            /// <summary>
            /// The entity for which an operation was requested does not exist.
            /// </summary>
            EntityNotFound,
            /// <summary>
            /// Request failed authorization.
            /// </summary>
            AuthorizationCheckFailed,
            /// <summary>
            /// The requested operation failed on the database.
            /// </summary>
            DatabaseOperationFailed
        }

        public int StatusCode { get; }
        public SubStatusCodes SubStatusCode { get; }

        public DatagatewayException(string message, int statusCode, SubStatusCodes subStatusCode)
            : base(message)
        {
            StatusCode = statusCode;
            SubStatusCode = subStatusCode;
        }
    }
}
