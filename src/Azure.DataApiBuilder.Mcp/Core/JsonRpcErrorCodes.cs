namespace Azure.DataApiBuilder.Mcp.Core
{
    /// <summary>
    /// JSON-RPC 2.0 standard error codes used by the MCP stdio server.
    /// These values come from the JSON-RPC 2.0 specification and are shared
    /// so they are not hard-coded throughout the codebase.
    /// </summary>
    internal static class JsonRpcErrorCodes
    {
        /// <summary>
        /// Invalid JSON was received by the server.
        /// An error occurred on the server while parsing the JSON text.
        /// </summary>
        public const int PARSEERROR = -32700;

        /// <summary>
        /// The JSON sent is not a valid Request object.
        /// </summary>
        public const int INVALIDREQUEST = -32600;

        /// <summary>
        /// The method does not exist / is not available.
        /// </summary>
        public const int METHODNOTFOUND = -32601;

        /// <summary>
        /// Invalid method parameter(s).
        /// </summary>
        public const int INVALIDPARAMS = -32602;

        /// <summary>
        /// Internal JSON-RPC error.
        /// </summary>
        public const int INTERNALERROR = -32603;
    }
}
