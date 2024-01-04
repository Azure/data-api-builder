// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO.Abstractions;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Product;
using CommandLine;
using Microsoft.Extensions.Logging;
using static Cli.Utils;

namespace Cli.Commands
{
    /// <summary>
    /// Telemetry command options
    /// </summary>
    [Verb("add-telemetry", isDefault: false, HelpText = "Add telemtry for Data Api Builder Application", Hidden = false)]
    public class AddTelemetryOptions : Options
    {
        public AddTelemetryOptions(bool appInsightsEnabled, string appInsightsConnString, string? config) : base(config)
        {
            AppInsightsEnabled = appInsightsEnabled;
            AppInsightsConnString = appInsightsConnString;
        }

        [Option("app-insights-enabled", Default = false, HelpText = "Enable/Disable Application Insights")]
        public bool AppInsightsEnabled { get; }

        [Option("app-insights-conn-string", HelpText = "Connection string for the Application Insights resource for telemetry data", Required = true)]
        public string AppInsightsConnString { get; }

        public void Handler(ILogger logger, FileSystemRuntimeConfigLoader loader, IFileSystem fileSystem)
        {
            logger.LogInformation("{productName} {version}", PRODUCT_NAME, ProductInfo.GetProductVersion());

            bool isSuccess = ConfigGenerator.TryAddTelemetry(this, loader, fileSystem);

            if (isSuccess)
            {
                logger.LogInformation("Successfully added telemetry to the configuration file.");
            }
            else
            {
                logger.LogError("Failed to add telemetry to the configuration file.");
            }
        }
    }
}
