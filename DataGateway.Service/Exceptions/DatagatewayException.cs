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
            BadRequest
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
