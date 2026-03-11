// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Mcp.Utils
{
    /// <summary>
    /// Constants for MCP telemetry error codes.
    /// </summary>
    internal static class McpTelemetryErrorCodes
    {
        /// <summary>
        /// Generic execution failure error code.
        /// </summary>
        public const string EXECUTION_FAILED = "ExecutionFailed";

        /// <summary>
        /// Authentication failure error code.
        /// </summary>
        public const string AUTHENTICATION_FAILED = "AuthenticationFailed";

        /// <summary>
        /// Authorization failure error code.
        /// </summary>
        public const string AUTHORIZATION_FAILED = "AuthorizationFailed";

        /// <summary>
        /// Database operation failure error code.
        /// </summary>
        public const string DATABASE_ERROR = "DatabaseError";

        /// <summary>
        /// Invalid request or arguments error code.
        /// </summary>
        public const string INVALID_REQUEST = "InvalidRequest";

        /// <summary>
        /// Operation cancelled error code.
        /// </summary>
        public const string OPERATION_CANCELLED = "OperationCancelled";
    }
}
