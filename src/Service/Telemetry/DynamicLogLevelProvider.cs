using System;
using System.Collections.Generic;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Telemetry;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Service.Telemetry
{
    /// <summary>
    /// Provides dynamic log level control with support for CLI override, runtime config, and MCP.
    /// </summary>
    public class DynamicLogLevelProvider : ILogLevelController
    {
        /// <summary>
        /// Maps MCP log level strings to Microsoft.Extensions.Logging.LogLevel.
        /// MCP levels: debug, info, notice, warning, error, critical, alert, emergency.
        /// </summary>
        private static readonly Dictionary<string, LogLevel> _mcpLevelMapping = new(StringComparer.OrdinalIgnoreCase)
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

        public LogLevel CurrentLogLevel { get; private set; }

        public bool IsCliOverridden { get; private set; }

        public bool IsConfigOverridden { get; private set; }

        public void SetInitialLogLevel(LogLevel logLevel = LogLevel.Error, bool isCliOverridden = false, bool isConfigOverridden = false)
        {
            CurrentLogLevel = logLevel;
            IsCliOverridden = isCliOverridden;
            IsConfigOverridden = isConfigOverridden;
        }

        public void UpdateFromRuntimeConfig(RuntimeConfig runtimeConfig)
        {
            // Only update if CLI didn't override
            if (!IsCliOverridden)
            {
                CurrentLogLevel = runtimeConfig.GetConfiguredLogLevel();

                // Track if config explicitly set a log level (not just using defaults)
                IsConfigOverridden = !runtimeConfig.IsLogLevelNull();
            }
        }

        /// <summary>
        /// Updates the log level from an MCP logging/setLevel request.
        /// Precedence (highest to lowest):
        /// 1. CLI --LogLevel flag (IsCliOverridden = true)
        /// 2. Config runtime.telemetry.log-level (IsConfigOverridden = true)
        /// 3. MCP logging/setLevel
        /// 
        /// If CLI or Config overrode, this method accepts the request silently but does not change the level.
        /// </summary>
        /// <param name="mcpLevel">The MCP log level string (e.g., "debug", "info", "warning", "error").</param>
        /// <returns>True if the level was changed; false if CLI/Config override prevented the change or level was invalid.</returns>
        public bool UpdateFromMcp(string mcpLevel)
        {
            // If CLI overrode the log level, accept the request but don't change anything.
            // This prevents MCP clients from getting errors, but CLI wins.
            if (IsCliOverridden)
            {
                return false;
            }

            // If Config explicitly set the log level, accept the request but don't change anything.
            // Config has second precedence after CLI.
            if (IsConfigOverridden)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(mcpLevel))
            {
                return false;
            }

            if (_mcpLevelMapping.TryGetValue(mcpLevel, out LogLevel logLevel))
            {
                CurrentLogLevel = logLevel;
                return true;
            }

            // Unknown level - don't change, but don't fail either
            return false;
        }

        public bool ShouldLog(LogLevel logLevel)
        {
            return logLevel >= CurrentLogLevel;
        }
    }
}
