// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Service.Telemetry
{
    /// <summary>
    /// Provides a runtime-updatable log level that can be shared across logging pipelines.
    /// Initialized from CLI args before host builds, then optionally updated from the loaded
    /// RuntimeConfig (unless the CLI has already overridden the level).
    /// </summary>
    public class DynamicLogLevelProvider
    {
        /// <summary>
        /// The current minimum log level. Messages below this level are suppressed.
        /// </summary>
        public LogLevel CurrentLogLevel { get; private set; } = LogLevel.Error;

        /// <summary>
        /// Whether the log level was explicitly set via CLI args and should not be
        /// overridden by the config file.
        /// </summary>
        public bool IsCliOverridden { get; private set; }

        /// <summary>
        /// Sets the initial log level, typically from parsed CLI args.
        /// Must be called before the host is built so that the log filters are wired correctly.
        /// </summary>
        /// <param name="logLevel">Minimum log level to apply.</param>
        /// <param name="isCliOverridden">
        /// True when the caller explicitly provided <paramref name="logLevel"/> via a CLI flag;
        /// false when falling back to a default.
        /// </param>
        public void SetInitialLogLevel(LogLevel logLevel = LogLevel.Error, bool isCliOverridden = false)
        {
            CurrentLogLevel = logLevel;
            IsCliOverridden = isCliOverridden;
        }

        /// <summary>
        /// Updates the current log level from the loaded <see cref="RuntimeConfig"/>.
        /// Has no effect when the log level was already set via the CLI.
        /// </summary>
        /// <param name="runtimeConfig">The runtime configuration to read the configured log level from.</param>
        public void UpdateFromRuntimeConfig(RuntimeConfig runtimeConfig)
        {
            if (!IsCliOverridden)
            {
                CurrentLogLevel = runtimeConfig.GetConfiguredLogLevel();
            }
        }

        /// <summary>
        /// Returns true when the given <paramref name="logLevel"/> meets or exceeds
        /// <see cref="CurrentLogLevel"/>.
        /// </summary>
        public bool ShouldLog(LogLevel logLevel)
        {
            return logLevel >= CurrentLogLevel;
        }
    }
}
