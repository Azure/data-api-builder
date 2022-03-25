using System;
using System.Net;

namespace Azure.DataGateway.Service.Exceptions
{
    /// <summary>
    /// Represents an exception thrown from the DataGateway service.
    /// Message and http statusCode will be returned to the user but
    /// subStatus code is not returned.
    /// </summary>
#pragma warning disable CA1032 // Supressing since we only use the 3 argument constructor
    public class DataGatewayException : Exception
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
            /// Request failed authentication. i.e. No/Invalid JWT token
            /// </summary>
            AuthenticationChallenge,
            /// <summary>
            /// The requested operation failed on the database.
            /// </summary>
            DatabaseOperationFailed,
            /// <summary>
            /// Unexpected error.
            /// </summary>,
            UnexpectedError
        }

        public HttpStatusCode StatusCode { get; }
        public SubStatusCodes SubStatusCode { get; }

        public DataGatewayException(string message, HttpStatusCode statusCode, SubStatusCodes subStatusCode)
            : base(message)
        {
            StatusCode = statusCode;
            SubStatusCode = subStatusCode;
        }
    }
}
