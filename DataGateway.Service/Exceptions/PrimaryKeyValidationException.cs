using System;

namespace Azure.DataGateway.Service.Exceptions
{
    public class PrimaryKeyValidationException : Exception
    {
        public PrimaryKeyValidationException()
        {
        }

        public PrimaryKeyValidationException(string message)
            : base(message)
        {
        }

        public PrimaryKeyValidationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
