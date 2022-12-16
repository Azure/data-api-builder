namespace Azure.DataApiBuilder.Service.Models
{
    /// <summary>
    /// Represents the constant values of HTTP Methods.
    /// </summary>
    public static class HttpConstants
    {
        public const string GET = "GET";
        public const string POST = "POST";
        public const string PATCH = "PATCH";
        public const string PUT = "PUT";
        public const string DELETE = "DELETE"; 
    }

    public static class HttpHeaders
    {
        public const string CORRELATION_ID = "x-ms-correlation-id";
    }
}
