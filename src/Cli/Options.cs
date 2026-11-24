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

        // An immutable default cannot retain overrides from another invocation.
        internal CliInvocationDependencies Dependencies { get; set; } = CliInvocationDependencies.Default;
    }
}
