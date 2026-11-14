// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Core.Telemetry.Product;

namespace Cli
{
    /// <summary>
    /// Immutable invocation-local overrides, separate from telemetry state and CLI options.
    /// Null dependencies retain the real launcher/exporter and legacy static cancellation path.
    /// The optional cancellation source remains caller-owned: export cancels, never disposes it.
    /// </summary>
    internal sealed record CliInvocationDependencies(
        Func<string[], ProductTelemetryLaunchContext?, Action?, bool>? EngineLauncher = null,
        Func<Exporter>? ExporterFactory = null,
        CancellationTokenSource? ExportCancellationTokenSource = null)
    {
        internal static CliInvocationDependencies Default { get; } = new();
    }
}
