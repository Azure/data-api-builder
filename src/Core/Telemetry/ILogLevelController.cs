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
        /// When true, runtime-config (hot-reload) updates are ignored.
        /// </summary>
        bool IsCliOverridden { get; }

        /// <summary>
        /// Gets a value indicating whether the log level was explicitly set in the config file.
        /// </summary>
        bool IsConfigOverridden { get; }

        /// <summary>
        /// Gets a value indicating whether the log level has been overridden by an MCP
        /// <c>logging/setLevel</c> request from the agent. When true, runtime-config (hot-reload)
        /// updates are ignored so the agent's choice remains in effect.
        /// </summary>
        bool IsAgentOverridden { get; }

        /// <summary>
        /// Updates the log level from an MCP logging/setLevel request.
        /// The MCP level string is mapped to the appropriate LogLevel.
        /// Log-level precedence (highest to lowest):
        /// 1. Agent (MCP <c>logging/setLevel</c>) — always wins.
        /// 2. CLI <c>--LogLevel</c> flag.
        /// 3. Config <c>runtime.telemetry.log-level</c>.
        /// 4. Defaults.
        /// </summary>
        /// <param name="mcpLevel">The MCP log level string (e.g., "debug", "info", "warning", "error").</param>
        /// <returns>True if the level was changed; false if the input was an unrecognized level.</returns>
        bool UpdateFromMcp(string mcpLevel);
    }
}
