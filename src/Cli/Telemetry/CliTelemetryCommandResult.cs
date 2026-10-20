// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Core.Telemetry.Product;

namespace Cli.Telemetry
{
    /// <summary>
    /// A terminal command observation, separate from its legacy exit code and recoverable
    /// attempt failures. Contains only closed categories, never exception or argument data.
    /// </summary>
    internal readonly record struct CliTelemetryCommandResult(
        CliTelemetryOutcome Outcome,
        CliTelemetryFailureCategory FailureCategory);
}
