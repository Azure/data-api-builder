// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Core.Telemetry
{
    /// <summary>
    /// Provides conversion between .NET LogLevel and MCP log level strings.
    /// MCP log levels: debug, info, notice, warning, error, critical, alert, emergency.
    /// </summary>
    /// <remarks>
    /// This class centralizes the mapping between MCP and .NET log levels,
    /// avoiding duplication across DynamicLogLevelProvider and McpLogNotificationWriter.
    /// </remarks>
    public static class McpLogLevelConverter
    {
        /// <summary>
        /// Maps MCP log level strings to Microsoft.Extensions.Logging.LogLevel.
        /// </summary>
        private static readonly Dictionary<string, LogLevel> _mcpToLogLevel = new(StringComparer.OrdinalIgnoreCase)
        {
            ["debug"] = LogLevel.Debug,
            ["info"] = LogLevel.Information,
            ["notice"] = LogLevel.Information, // MCP "notice" maps to Information (no direct equivalent)
            ["warning"] = LogLevel.Warning,
            ["error"] = LogLevel.Error,
            ["critical"] = LogLevel.Critical,
            ["alert"] = LogLevel.Critical,     // MCP "alert" maps to Critical
            ["emergency"] = LogLevel.Critical  // MCP "emergency" maps to Critical
        };

        /// <summary>
        /// Converts an MCP log level string to a .NET LogLevel.
        /// </summary>
        /// <param name="mcpLevel">The MCP log level string (e.g., "debug", "info", "warning").</param>
        /// <param name="logLevel">The converted LogLevel if successful.</param>
        /// <returns>True if the conversion was successful; false if the MCP level was not recognized.</returns>
        public static bool TryConvertFromMcp(string mcpLevel, out LogLevel logLevel)
        {
            if (string.IsNullOrWhiteSpace(mcpLevel))
            {
                logLevel = LogLevel.None;
                return false;
            }

            return _mcpToLogLevel.TryGetValue(mcpLevel, out logLevel);
        }

        /// <summary>
        /// Converts a .NET LogLevel to an MCP log level string.
        /// </summary>
        /// <param name="logLevel">The .NET LogLevel to convert.</param>
        /// <returns>The MCP log level string.</returns>
        public static string ConvertToMcp(LogLevel logLevel)
        {
            return logLevel switch
            {
                LogLevel.Trace => "debug",
                LogLevel.Debug => "debug",
                LogLevel.Information => "info",
                LogLevel.Warning => "warning",
                LogLevel.Error => "error",
                LogLevel.Critical => "critical",
                LogLevel.None => "debug", // Default to debug for None
                _ => "info"
            };
        }
    }
}
