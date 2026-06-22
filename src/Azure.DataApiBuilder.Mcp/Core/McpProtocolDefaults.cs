using System.Globalization;
using Azure.DataApiBuilder.Product;
using Microsoft.Extensions.Configuration;

namespace Azure.DataApiBuilder.Mcp.Core
{
    /// <summary>
    /// Centralized defaults and configuration keys for MCP protocol settings.
    /// </summary>
    public static class McpProtocolDefaults
    {
        /// <summary>
        /// Default MCP server name advertised during initialization.
        /// </summary>
        public const string MCP_SERVER_NAME = "SQL MCP Server";
        /// <summary>
        /// Default MCP server version advertised during initialization.
        /// </summary>
        public static readonly string MCP_SERVER_VERSION = ProductInfo.GetProductVersion();
        /// <summary>
        /// Default MCP protocol version advertised when no configuration override is provided.
        /// </summary>
        public const string DEFAULT_PROTOCOL_VERSION = "2025-11-25";

        /// <summary>
        /// Configuration key used to override the MCP protocol version.
        /// </summary>
        public const string PROTOCOL_VERSION_CONFIG_KEY = "MCP:ProtocolVersion";

        /// <summary>
        /// Protocol version where MCP initialize server description is expected under serverInfo.description.
        /// </summary>
        public const string SERVER_INFO_DESCRIPTION_PROTOCOL_VERSION = "2025-11-25";

        /// <summary>
        /// Helper to resolve the effective protocol version from configuration.
        /// Falls back to <see cref="DEFAULT_PROTOCOL_VERSION"/> when the key is not set.
        /// </summary>
        public static string ResolveProtocolVersion(IConfiguration? configuration)
        {
            return configuration?.GetValue<string>(PROTOCOL_VERSION_CONFIG_KEY) ?? DEFAULT_PROTOCOL_VERSION;
        }

        /// <summary>
        /// Resolves the protocol version to send in initialize response as the
        /// greatest version that does not exceed the client requested version.
        /// </summary>
        /// <param name="supportedProtocolVersion">The server's effective supported protocol version.</param>
        /// <param name="clientRequestedProtocolVersion">The protocol version requested by the client.</param>
        /// <returns>The protocol version to return to the client.</returns>
        public static string ResolveInitializeResponseProtocolVersion(string supportedProtocolVersion, string? clientRequestedProtocolVersion)
        {
            if (string.IsNullOrWhiteSpace(clientRequestedProtocolVersion))
            {
                return supportedProtocolVersion;
            }

            return CompareProtocolVersions(supportedProtocolVersion, clientRequestedProtocolVersion) <= 0
                ? supportedProtocolVersion
                : clientRequestedProtocolVersion;
        }

        /// <summary>
        /// Indicates whether initialize response metadata should use serverInfo.description instead of top-level instructions.
        /// </summary>
        public static bool ShouldUseServerInfoDescription(string protocolVersion)
        {
            return CompareProtocolVersions(protocolVersion, SERVER_INFO_DESCRIPTION_PROTOCOL_VERSION) >= 0;
        }

        private static int CompareProtocolVersions(string leftVersion, string rightVersion)
        {
            const string PROTOCOL_VERSION_DATE_FORMAT = "yyyy-MM-dd";
            if (DateOnly.TryParseExact(leftVersion, PROTOCOL_VERSION_DATE_FORMAT, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly leftDate) &&
                DateOnly.TryParseExact(rightVersion, PROTOCOL_VERSION_DATE_FORMAT, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly rightDate))
            {
                return leftDate.CompareTo(rightDate);
            }

            return string.Compare(leftVersion, rightVersion, StringComparison.Ordinal);
        }
    }
}
