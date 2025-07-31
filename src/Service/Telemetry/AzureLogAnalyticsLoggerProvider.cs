// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Service.Telemetry;

/// <summary>
/// Adds an Azure Log Analytics logger named 'AzureLogAnalyticsLogger' to the <see cref="ILoggerFactory"/>.
/// </summary>
public class AzureLogAnalyticsLoggerProvider : ILoggerProvider
{
    private readonly ICustomLogCollector _customLogCollector;

    public AzureLogAnalyticsLoggerProvider(ICustomLogCollector customLogCollector)
    {
        _customLogCollector = customLogCollector;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new AzureLogAnalyticsLogger(categoryName, _customLogCollector);
    }

    public void Dispose() { }
}
