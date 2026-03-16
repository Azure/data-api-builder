using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Service.Telemetry
{
    public class DynamicLogLevelProvider
    {
        public LogLevel CurrentLogLevel { get; private set; }
        public bool IsCliOverridden { get; private set; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="logLevel"></param>
        /// <param name="isCliOverridden"></param>
        public void SetInitialLogLevel(LogLevel logLevel = LogLevel.Error, bool isCliOverridden = false)
        {
            CurrentLogLevel = logLevel;
            IsCliOverridden = isCliOverridden;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="runtimeConfig"></param>
        /// <param name="loggerFilter"></param>
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
        /// 
        /// </summary>
        /// <param name="logLevel"></param>
        /// <returns></returns>
        public bool ShouldLog(LogLevel logLevel)
        {
            return logLevel >= CurrentLogLevel;
        }
    }
}
