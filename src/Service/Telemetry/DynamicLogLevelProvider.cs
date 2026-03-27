using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Service.Telemetry
{
    public class DynamicLogLevelProvider
    {
        public LogLevel CurrentLogLevel { get; private set; }
        public bool IsCliOverridden { get; private set; }

        /// <summary>
        /// Sets the initial log level, which can be passed from the CLI or default to Error if in Production mode or Debug if in Development mode.
        /// Also sets whether the log level was overridden by the CLI, which will prevent updates from runtime config changes. 
        /// </summary>
        /// <param name="logLevel">The initial log level to set.</param>
        /// <param name="isCliOverridden">Indicates whether the log level was overridden by the CLI.</param>
        public void SetInitialLogLevel(LogLevel logLevel = LogLevel.Error, bool isCliOverridden = false)
        {
            CurrentLogLevel = logLevel;
            IsCliOverridden = isCliOverridden;
        }

        /// <summary>
        /// Updates the current log level based on the runtime configuration, unless it was overridden by the CLI.
        /// </summary>
        /// <param name="runtimeConfig">The runtime configuration to use for updating the log level.</param>
        /// <param name="loggerFilter">Optional logger filter to apply when determining the log level.</param>
        public void UpdateFromRuntimeConfig(RuntimeConfig runtimeConfig, string? loggerFilter = null)
        {
            // Only update if CLI didn't override
            if (!IsCliOverridden)
            {
                if (loggerFilter is null)
                {
                    loggerFilter = string.Empty;
                }

                CurrentLogLevel = runtimeConfig.GetConfiguredLogLevel(loggerFilter);
            }
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
