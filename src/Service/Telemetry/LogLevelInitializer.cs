// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Microsoft.Extensions.Logging;
using static Azure.DataApiBuilder.Config.DabConfigEvents;

namespace Azure.DataApiBuilder.Service.Telemetry
{
    public class LogLevelInitializer
    {
        private RuntimeConfigProvider _runtimeConfigProvider;
        private string _loggerFilter;

        public LogLevel MinLogLevel { get; private set; }

        public LogLevelInitializer(LogLevel logLevel, string? loggerFilter, RuntimeConfigProvider configProvider, HotReloadEventHandler<HotReloadEventArgs>? handler)
        {
            handler?.Subscribe(LOG_LEVEL_INITIALIZER_ON_CONFIG_CHANGE, OnConfigChanged);
            MinLogLevel = logLevel;
            _runtimeConfigProvider = configProvider;

            if (loggerFilter is not null)
            {
                _loggerFilter = loggerFilter;
            }
            else
            {
                _loggerFilter = string.Empty;
            }
        }

        public void SetLogLevel()
        {
            if (_runtimeConfigProvider!.TryGetConfig(out RuntimeConfig? runtimeConfig))
            {
                MinLogLevel = runtimeConfig.GetConfiguredLogLevel(_loggerFilter);
            }
        }

        private void OnConfigChanged(object? sender, HotReloadEventArgs args)
        {
            SetLogLevel();
        }
    }
}
