using System;

namespace Azure.DataGateway.Service.Exceptions
{
    /// <summary>
    /// Represents an exception thrown from the Datagateway service.
    /// Message and http statusCode will be returned to the user but
    /// subStatus code is not returned.
    /// </summary>
    public class DatagatewayException : Exception
    {
        public int StatusCode { get; }
        public string SubStatusCode { get; }

        public DatagatewayException(string message)
            : base(message)
        {
        }

        public DatagatewayException(string message, int statusCode, string subStatusCode)
            : base(message)
        {
            StatusCode = statusCode;
            SubStatusCode = subStatusCode;
        }

        public DatagatewayException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        public DatagatewayException()
        {
        }
    }
}
