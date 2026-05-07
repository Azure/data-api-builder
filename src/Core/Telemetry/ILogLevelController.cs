// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.Telemetry
{
    /// <summary>
    /// Interface for controlling log levels dynamically at runtime.
    /// This allows MCP and other components to adjust logging without
    /// direct coupling to the concrete implementation.
    /// </summary>
    public interface ILogLevelController
    {
        /// <summary>
        /// Gets a value indicating whether the log level was overridden by CLI arguments.
        /// When true, MCP and config-based log level changes are ignored.
        /// </summary>
        bool IsCliOverridden { get; }

        /// <summary>
        /// Gets a value indicating whether the log level was explicitly set in the config file.
        /// When true along with IsCliOverridden being false, MCP log level changes are ignored.
        /// </summary>
        bool IsConfigOverridden { get; }

        /// <summary>
        /// Updates the log level from an MCP logging/setLevel request.
        /// The MCP level string is mapped to the appropriate LogLevel.
        /// Log level precedence (highest to lowest):
        /// 1. CLI --LogLevel flag (IsCliOverridden = true)
        /// 2. Config runtime.telemetry.log-level (IsConfigOverridden = true)
        /// 3. MCP logging/setLevel (only works if neither CLI nor Config set a level)
        /// </summary>
        /// <param name="mcpLevel">The MCP log level string (e.g., "debug", "info", "warning", "error").</param>
        /// <returns>True if the level was changed; false if CLI or Config override prevented the change.</returns>
        bool UpdateFromMcp(string mcpLevel);
    }
}
