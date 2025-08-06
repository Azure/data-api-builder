// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.Monitor.Ingestion;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Service.Telemetry;

/// <summary>
/// Service used to periodically flush logs to Azure Log Analytics
/// </summary>
public class AzureLogAnalyticsFlusherService : BackgroundService
{
    private readonly AzureLogAnalyticsOptions _options;
    private readonly ICustomLogCollector _customLogCollector;
    private readonly LogsIngestionClient _logsIngestionClient;
    private readonly ILogger<Startup> _logger;

    public AzureLogAnalyticsFlusherService(AzureLogAnalyticsOptions options, ICustomLogCollector customLogCollector, LogsIngestionClient logsIngestionClient, ILogger<Startup> logger)
    {
        _options = options;
        _customLogCollector = customLogCollector;
        _logsIngestionClient = logsIngestionClient;
        _logger = logger;
    }

    /// <summary>
    /// Function that will keep periodically flushing data logs as long as Azure Log Analytics is enabled.
    /// </summary>
    /// <param name="stoppingToken">Token used to stop running service when program is shut down.</param>
    protected async override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (true)
        {
            try
            {
                List<AzureLogAnalyticsLogs> logs = await _customLogCollector.DequeueAllAsync(_options.DabIdentifier!, (int)_options.FlushIntervalSeconds!);

                if (logs.Count > 0)
                {
                    await _logsIngestionClient.UploadAsync<AzureLogAnalyticsLogs>(_options.Auth!.DcrImmutableId!, _options.Auth!.CustomTableName!, logs);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error uploading logs to Azure Log Analytics: {ex}");
            }
        }
    }
}
