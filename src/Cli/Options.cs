// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Core.Telemetry.Product;
using CommandLine;

namespace Cli
{
    /// <summary>
    /// Common options for all the commands
    /// </summary>
    public class Options
    {
        public Options(string? config)
        {
            Config = config;
        }

        [Option('c', "config", Required = false, HelpText = "Path to config file. " +
            "Defaults to 'dab-config.json' unless 'dab-config.<DAB_ENVIRONMENT>.json' exists," +
            " where DAB_ENVIRONMENT is an environment variable.")]
        public string? Config { get; }

        // Invocation-owned state is passed explicitly to handlers and any helper engine launch.
        // These are not CLI options and must never be included in command-line serialization.
        internal CliTelemetrySession? ProductTelemetry { get; set; }

        internal CliTelemetryLaunchSource? ProductTelemetryLaunchSource { get; set; }

        internal CliTelemetryLaunchReservation? ProductTelemetryLaunchReservation { get; set; }

        // Per-invocation test dependencies, deliberately separate from the immutable telemetry
        // launch context. Null preserves the real service, exporter and legacy cancellation path.
        internal Func<string[], ProductTelemetryLaunchContext?, Action?, bool>? EngineLauncher { get; set; }

        internal Func<Exporter>? ExporterFactory { get; set; }

        // Caller-owned; Exporter cancels but does not dispose this optional source.
        internal CancellationTokenSource? ExportCancellationTokenSource { get; set; }
    }
}
