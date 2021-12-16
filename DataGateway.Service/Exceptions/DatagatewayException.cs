using System;

namespace Azure.DataGateway.Service.Exceptions
{
    public class DatagatewayException : Exception
    {
        public int StatusCode { get; }
        public int SubStatusCode { get; }

        public DatagatewayException(string message)
            : base(message)
        {
        }

        public DatagatewayException(string message, int statusCode, int subStatusCode)
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
