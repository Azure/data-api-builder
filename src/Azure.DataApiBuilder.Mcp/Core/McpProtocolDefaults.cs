using Microsoft.Extensions.Configuration;

namespace Azure.DataApiBuilder.Mcp.Core
{
    /// <summary>
    /// Centralized defaults and configuration keys for MCP protocol settings.
    /// </summary>
    public static class McpProtocolDefaults
    {
        /// <summary>
        /// Default MCP protocol version advertised when no configuration override is provided.
        /// </summary>
        public const string DEFAULT_PROTOCOL_VERSION = "2025-06-18";

        /// <summary>
        /// Configuration key used to override the MCP protocol version.
        /// </summary>
        public const string PROTOCOL_VERSION_CONFIG_KEY = "MCP:ProtocolVersion";

        /// <summary>
        /// Helper to resolve the effective protocol version from configuration.
        /// Falls back to <see cref="DEFAULT_PROTOCOL_VERSION"/> when the key is not set.
        /// </summary>
        public static string ResolveProtocolVersion(IConfiguration? configuration)
        {
            return configuration?.GetValue<string>(PROTOCOL_VERSION_CONFIG_KEY) ?? DEFAULT_PROTOCOL_VERSION;
        }
    }
}

