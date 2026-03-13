using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Service.Telemetry
{
    public class DynamicLogLevelProvider
    {
        public LogLevel CurrentLogLevel { get; private set; }
        public bool IsCliOverridden { get; private set; }

        public void SetInitialLogLevel(LogLevel logLevel = LogLevel.Error, bool isCliOverridden = false)
        {
            CurrentLogLevel = logLevel;
            IsCliOverridden = isCliOverridden;
        }

        public void UpdateFromRuntimeConfig(RuntimeConfig runtimeConfig)
        {
            // Only update if CLI didn't override
            if (!IsCliOverridden)
            {
                CurrentLogLevel = runtimeConfig.GetConfiguredLogLevel();
            }
        }

        public bool ShouldLog(LogLevel logLevel)
        {
            return logLevel >= CurrentLogLevel;
        }
    }
}
