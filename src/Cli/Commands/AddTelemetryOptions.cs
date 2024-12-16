// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO.Abstractions;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Product;
using Cli.Constants;
using CommandLine;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Exporter;
using static Cli.Utils;

namespace Cli.Commands
{
    /// <summary>
    /// Telemetry command options
    /// </summary>
    [Verb("add-telemetry", isDefault: false, HelpText = "Add telemetry for Data Api builder Application", Hidden = false)]
    public class AddTelemetryOptions : Options
    {
        public AddTelemetryOptions(
            string? appInsightsConnString = null,
            CliBool? appInsightsEnabled = null,
            string? openTelemetryEndpoint = null,
            CliBool? openTelemetryEnabled = null,
            string? openTelemetryHeaders = null,
            OtlpExportProtocol? openTelemetryExportProtocol = null,
            string? openTelemetryServiceName = null,
            string? config = null) : base(config)
        {
            AppInsightsConnString = appInsightsConnString;
            AppInsightsEnabled = appInsightsEnabled;
            OpenTelemetryEndpoint = openTelemetryEndpoint;
            OpenTelemetryEnabled = openTelemetryEnabled;
            OpenTelemetryHeaders = openTelemetryHeaders;
            OpenTelemetryExportProtocol = openTelemetryExportProtocol;
            OpenTelemetryServiceName = openTelemetryServiceName;
        }

        // Connection string for the Application Insights resource to which telemetry data should be sent.
        // This option is required and must be provided with a valid connection string when using app insights.
        [Option("app-insights-conn-string", Required = false, HelpText = "Connection string for the Application Insights resource for telemetry data")]
        public string? AppInsightsConnString { get; }

        // To specify whether Application Insights telemetry should be enabled. This flag is optional and default value is false.
        [Option("app-insights-enabled", Default = CliBool.False, Required = false, HelpText = "(Default: false) Enable/Disable Application Insights")]
        public CliBool? AppInsightsEnabled { get; }

        // Connection string for the Open Telemetry resource to which telemetry data should be sent.
        // This option is required and must be provided with a valid connection string when using open telemetry.
        [Option("otel-endpoint", Required = false, HelpText = "Endpoint for Open Telemetry for telemetry data")]
        public string? OpenTelemetryEndpoint { get; }

        // To specify whether Open Telemetry telemetry should be enabled. This flag is optional and default value is false.
        [Option("otel-enabled", Default = CliBool.False, Required = false, HelpText = "(Default: false) Enable/Disable OTEL")]
        public CliBool? OpenTelemetryEnabled { get; }

        // Headers for the Open Telemetry resource to which telemetry data should be sent.
        [Option("otel-headers", Required = false, HelpText = "Headers for Open Telemetry for telemetry data")]
        public string? OpenTelemetryHeaders { get; }

        // Specify the Open Telemetry protocol. This flag is optional and default value is grpc.
        [Option("otel-protocol", Default = OtlpExportProtocol.Grpc, Required = false, HelpText = "(Default: grpc) Accepted: grpc/httpprotobuf")]
        public OtlpExportProtocol? OpenTelemetryExportProtocol { get; }

        // Service Name for the Open Telemetry resource to which telemetry data should be sent. This flag is optional and default value is dab.
        [Option("otel-service-name", Default = "dab", Required = false, HelpText = "(Default: dab) Headers for Open Telemetry for telemetry data")]
        public string? OpenTelemetryServiceName { get; }

        public int Handler(ILogger logger, FileSystemRuntimeConfigLoader loader, IFileSystem fileSystem)
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

            return isSuccess ? CliReturnCode.SUCCESS : CliReturnCode.GENERAL_ERROR;
        }
    }
}
