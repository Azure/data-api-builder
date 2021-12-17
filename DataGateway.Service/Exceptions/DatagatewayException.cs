using System;

namespace Azure.DataGateway.Service.Exceptions
{
    /// <summary>
    /// Represents an exception thrown from the Datagateway service.
    /// Message and http statusCode will be returned to the user but
    /// subStatus code is not returned.
    /// </summary>
#pragma warning disable CA1032 // Implement standard exception constructors
    public class DatagatewayException : Exception
    {
        public enum SubStatusCodes { BadRequest };
        public int StatusCode { get; }
        public string SubStatusCode { get; }

        public DatagatewayException(string message, int statusCode, SubStatusCodes subStatusCode)
            : base(message)
        {
            StatusCode = statusCode;
            SubStatusCode = subStatusCode.ToString();
        }
    }
}
