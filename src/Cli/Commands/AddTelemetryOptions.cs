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
            CliBool? fileSinkEnabled = null,
            string? fileSinkPath = null,
            RollingIntervalMode? fileSinkRollingInterval = null,
            int? fileSinkRetainedFileCountLimit = null,
            int? fileSinkFileSizeLimitBytes = null,
            string? config = null) : base(config)
        {
            AppInsightsConnString = appInsightsConnString;
            AppInsightsEnabled = appInsightsEnabled;
            OpenTelemetryEndpoint = openTelemetryEndpoint;
            OpenTelemetryEnabled = openTelemetryEnabled;
            OpenTelemetryHeaders = openTelemetryHeaders;
            OpenTelemetryExportProtocol = openTelemetryExportProtocol;
            OpenTelemetryServiceName = openTelemetryServiceName;
            FileSinkEnabled = fileSinkEnabled;
            FileSinkPath = fileSinkPath;
            FileSinkRollingInterval = fileSinkRollingInterval;
            FileSinkRetainedFileCountLimit = fileSinkRetainedFileCountLimit;
            FileSinkFileSizeLimitBytes = fileSinkFileSizeLimitBytes;
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

        // To specify whether File Sink telemetry should be enabled. This flag is optional and default value is false.
        [Option("file-sink-enabled", Default = CliBool.False, Required = false, HelpText = "Enable/disable file sink telemetry logging. Default: false (boolean).")]
        public CliBool? FileSinkEnabled { get; }

        // Path for the File Sink resource to which telemetry data should be sent. This flag is optional and default value is '/logs/dab-log.txt'.
        [Option("file-sink-path", Default = "/logs/dab-log.txt", Required = false, HelpText = "File path for telemetry logs. Default: '/logs/dab-log.txt' (string).")]
        public string? FileSinkPath { get; }

        // Rolling Interval for the File Sink resource to which telemetry data should be sent. This flag is optional and default value is new interval per Day.
        [Option("file-sink-rolling-interval", Default = RollingIntervalMode.Day, Required = false, HelpText = "Rolling interval for log files. Default: Day. Allowed values: Hour, Day, Week.")]
        public RollingIntervalMode? FileSinkRollingInterval { get; }

        // Retained File Count Limit for the File Sink resource to which telemetry data should be sent. This flag is optional and default value is 1.
        [Option("file-sink-retained-file-count-limit", Default = 1, Required = false, HelpText = "Maximum number of retained log files. Default: 1 (integer, minimum: 1).")]
        public int? FileSinkRetainedFileCountLimit { get; }

        // File Size Limit Bytes for the File Sink resource to which telemetry data should be sent. This flag is optional and default value is 1048576.
        [Option("file-sink-file-size-limit-bytes", Default = 1048576, Required = false, HelpText = "Maximum file size in bytes before rolling. Default: 1048576 (integer, minimum: 1).")]
        public int? FileSinkFileSizeLimitBytes { get; }

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
