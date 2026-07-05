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
        /// Gets a value indicating whether the CLI is the source overriding the log level
        /// (i.e., <c>--log-level</c> was supplied). When true, runtime-config (hot-reload)
        /// updates are ignored.
        /// </summary>
        bool IsCliOverriding { get; }

        /// <summary>
        /// Gets a value indicating whether the runtime config is the source overriding the log
        /// level (i.e., <c>runtime.telemetry.log-level</c> was explicitly set).
        /// </summary>
        bool IsConfigOverriding { get; }

        /// <summary>
        /// Gets a value indicating whether the agent is the source overriding the log level via
        /// an MCP <c>logging/setLevel</c> request. When true, runtime-config (hot-reload) updates
        /// are ignored so the agent's choice remains in effect.
        /// </summary>
        bool IsAgentOverriding { get; }

        /// <summary>
        /// Updates the log level from an MCP logging/setLevel request.
        /// The MCP level string is mapped to the appropriate LogLevel.
        /// Log-level precedence (highest to lowest):
        /// 1. Agent (MCP <c>logging/setLevel</c>) — always wins.
        /// 2. CLI <c>--log-level</c> flag.
        /// 3. Config <c>runtime.telemetry.log-level</c>.
        /// 4. Defaults.
        /// </summary>
        /// <param name="mcpLevel">The MCP log level string (e.g., "debug", "info", "warning", "error").</param>
        /// <returns>True if the level was changed; false if the input was an unrecognized level.</returns>
        bool UpdateFromMcp(string mcpLevel);
    }
}
