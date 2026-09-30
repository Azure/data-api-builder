using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Telemetry;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Service.Telemetry
{
    /// <summary>
    /// Provides dynamic log level control with support for CLI override, runtime config, and MCP.
    /// Precedence (highest to lowest): Agent (MCP) > CLI > Config > defaults.
    /// </summary>
    public class DynamicLogLevelProvider : ILogLevelController
    {
        /// <summary>
        /// Guards mutations of <see cref="CurrentLogLevel"/> and the override flags so
        /// concurrent <see cref="UpdateFromMcp"/> and <see cref="UpdateFromRuntimeConfig"/>
        /// calls cannot interleave and break the Agent &gt; CLI &gt; Config precedence.
        /// </summary>
        private readonly object _stateLock = new();

        public LogLevel CurrentLogLevel { get; private set; }

        public bool IsCliOverriding { get; private set; }

        public bool IsConfigOverriding { get; private set; }

        public bool IsAgentOverriding { get; private set; }

        public bool IsLogLevelLegacy { get; private set; }

        /// <summary>
        /// Optional logger used to emit an Information line when the agent successfully overrides
        /// the log level. Wired by host startup once the logging pipeline is available; safe to
        /// leave unset (e.g. in unit tests).
        /// </summary>
        public ILogger? Logger { get; set; }

        /// <summary>
        /// Sets the initial log level from CLI args or the config file. When not specified the level
        /// defaults to None for <c>--mcp-stdio</c>, Error in Production, or Debug in Development.
        /// The CLI/Config flags drive runtime-config hot-reload behavior; they no longer block
        /// agent (MCP) overrides — see <see cref="UpdateFromMcp"/>.
        /// </summary>
        /// <param name="logLevel">The initial log level to set.</param>
        /// <param name="isCliOverriding">Indicates whether the CLI is overriding the log level.</param>
        /// <param name="isConfigOverriding">Indicates whether the runtime config is overriding the log level.</param>
        public void SetInitialLogLevel(LogLevel logLevel = LogLevel.Error, bool isCliOverriding = false, bool isConfigOverriding = false, bool isLogLevelLegacy = false)
        {
            lock (_stateLock)
            {
                CurrentLogLevel = logLevel;
                IsCliOverriding = isCliOverriding;
                IsConfigOverriding = isConfigOverriding;
                IsLogLevelLegacy = isLogLevelLegacy;
            }
        }

        /// <summary>
        /// Updates the current log level from a runtime-config (hot-reload) change.
        /// Skipped when the CLI or the agent has already overridden, so neither is overwritten.
        /// </summary>
        /// <param name="runtimeConfig">The runtime configuration to use for updating the log level.</param>
        /// <param name="loggerFilter">Optional logger filter to apply when determining the log level.</param>
        public void UpdateFromRuntimeConfig(RuntimeConfig runtimeConfig, string? loggerFilter = null)
        {
            lock (_stateLock)
            {
                // Agent override and CLI override both win over a hot-reloaded Config value.
                // The check + assignment must be inside the lock so a concurrent UpdateFromMcp
                // cannot slip in between the guard and the write.
                if (IsAgentOverriding || IsCliOverriding)
                {
                    return;
                }

                if (loggerFilter is null)
                {
                    loggerFilter = string.Empty;
                }

                CurrentLogLevel = runtimeConfig.GetConfiguredLogLevel(loggerFilter);

                // Track if config explicitly set a non-null log level value, so callers can
                // distinguish a config-pinned level from defaults.
                IsConfigOverriding = runtimeConfig.HasExplicitLogLevel();
            }
        }

        /// <summary>
        /// Updates the log level from an MCP <c>logging/setLevel</c> request.
        /// The agent always wins over CLI and Config; only an unrecognized level is rejected.
        /// </summary>
        /// <param name="mcpLevel">The MCP log level string (e.g., "debug", "info", "warning", "error").</param>
        /// <returns>True if the level was changed; false only if the input was an unrecognized level.</returns>
        public bool UpdateFromMcp(string mcpLevel)
        {
            if (!McpLogLevelConverter.TryConvertFromMcp(mcpLevel, out LogLevel logLevel))
            {
                // Unknown level - don't change, but don't fail the MCP call either.
                return false;
            }

            lock (_stateLock)
            {
                CurrentLogLevel = logLevel;
                IsAgentOverriding = true;
            }

            // Surface the override so operators can see the agent moved the level.
            // Logged outside the lock so logger sinks can't deadlock with state mutations.
            // Emitted at Information and subject to the operator's configured filter, like
            // any other log line — the JSON-RPC request/response itself is the protocol-level
            // audit, so there's no need to bypass the filter here.
            Logger?.LogInformation(
                "Log level updated to {LogLevel} via MCP logging/setLevel (agent override).",
                logLevel);

            return true;
        }

        /// <summary>
        /// Used to dynamically determine whether a log should be emitted based on the current log level.
        /// This allows for dynamic log level changes at runtime without needing to restart the application.
        /// </summary>
        /// <param name="logLevel">The log level of the log that wants to be emitted.</param>
        /// <returns>True if the log should be emitted, false otherwise.</returns>
        public bool ShouldLog(LogLevel logLevel)
        {
            return logLevel >= CurrentLogLevel;
        }
    }
}
