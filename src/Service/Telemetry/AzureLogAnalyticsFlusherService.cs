// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.Monitor.Ingestion;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Azure.DataApiBuilder.Service.Telemetry;

/// <summary>
/// Service used to periodically flush logs to Azure Log Analytics
/// </summary>
public class AzureLogAnalyticsFlusherService
{
    private readonly AzureLogAnalyticsOptions _options;
    private readonly ICustomLogCollector _customLogCollector;
    private readonly LogsIngestionClient _logsIngestionClient;

    public AzureLogAnalyticsFlusherService(AzureLogAnalyticsOptions options, ICustomLogCollector customLogCollector, LogsIngestionClient logsIngestionClient)
    {
        _options = options;
        _customLogCollector = customLogCollector;
        _logsIngestionClient = logsIngestionClient;
    }

    // Function that will keep periodically flushing data logs as long as Azure Log Analytics is enabled
    public async Task StartAsync()
    {
        while (_options.Enabled)
        {
            try
            {
                List<AzureLogAnalyticsLogs> logs = _customLogCollector.DequeueAll();

                if (logs.Count > 0)
                {
                    await _logsIngestionClient.UploadAsync<AzureLogAnalyticsLogs>(_options.Auth!.DcrImmutableId!, _options.Auth!.CustomTableName!, logs);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error uploading logs to Azure Log Analytics: {ex}");
            }

            await Task.Delay(((int)_options.FlushIntervalSeconds!) * 1000);
        }
    }
}
