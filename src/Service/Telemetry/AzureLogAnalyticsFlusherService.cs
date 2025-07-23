// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.Identity;
using Azure.Monitor.Ingestion;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Azure.DataApiBuilder.Service.Telemetry
{
    public class AzureLogAnalyticsFlusherService
    {
        private readonly AzureLogAnalyticsOptions _options;
        private readonly ICustomLogCollector _customLogCollector;
        private readonly LogsIngestionClient _logsIngestionClient;
        private readonly DefaultAzureCredential _credential = new();

        public AzureLogAnalyticsFlusherService(AzureLogAnalyticsOptions options, ICustomLogCollector customLogCollector)
        {
            _options = options;
            _customLogCollector = customLogCollector;
            _logsIngestionClient = new LogsIngestionClient(new Uri(_options.Auth!.DceEndpoint!), _credential);
        }

        public async Task StartAsync()
        {
            while (_options.Enabled)
            {
                try
                {
                    List<AzureLogAnalyticsLogOptions> logs = _customLogCollector.DequeueAll();

                    if (logs.Count > 0)
                    {
                        await _logsIngestionClient.UploadAsync<AzureLogAnalyticsLogOptions>(_options.Auth!.DcrImmutableId!, _options.Auth!.WorkspaceId!, logs);
                        Console.WriteLine("Successfully uploaded logs to Azure Log Analytics");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error uploading logs to Azure Log Analytics {ex}");
                }

                await Task.Delay((int)_options.FlushIntervalSeconds!);
            }
        }
    }
}
