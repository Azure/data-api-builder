// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO.Abstractions;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Product;
using CommandLine;
using Microsoft.Extensions.Logging;
using static Cli.Utils;

namespace Cli.Commands
{
    /// <summary>
    /// Telemetry command options
    /// </summary>
    [Verb("add-telemetry", isDefault: false, HelpText = "Add telemtry for Data Api builder Application", Hidden = false)]
    public class AddTelemetryOptions : Options
    {
        public AddTelemetryOptions(string appInsightsConnString, CliBool appInsightsEnabled, string? config) : base(config)
        {
            AppInsightsConnString = appInsightsConnString;
            AppInsightsEnabled = appInsightsEnabled;
        }

        // Connection string for the Application Insights resource to which telemetry data should be sent.
        // This optional is required and must be provided a valid connection string.
        [Option("app-insights-conn-string", Required = true, HelpText = "Connection string for the Application Insights resource for telemetry data")]
        public string AppInsightsConnString { get; }

        // To specifies whether Application Insights telemetry should be enabled. This flag is optional and default value is true.
        [Option("app-insights-enabled", Default = CliBool.True, Required = false, HelpText = "(Default: true) Enable/Disable Application Insights")]
        public CliBool AppInsightsEnabled { get; }

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
