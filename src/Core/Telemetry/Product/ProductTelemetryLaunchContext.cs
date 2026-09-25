// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.Telemetry.Product
{
    /// <summary>The three actual CLI-to-engine handoffs, not command-line intent.</summary>
    internal enum CliTelemetryLaunchSource
    {
        StartWeb,
        StartStdio,
        ExportGraphQL
    }

    /// <summary>
    /// Immutable, explicitly passed correlation for one engine launch. Contains no configuration
    /// paths, arguments, callbacks, ambient state or lifetime ownership. The engine independently
    /// gates collection and owns its session; stopping the CLI cannot stop that engine session.
    /// </summary>
    internal sealed record ProductTelemetryLaunchContext(
        Guid EngineSessionId,
        Guid ParentCliSessionId,
        CliTelemetryInstallation Installation,
        EngineTelemetryIdentity? ApiIdentity,
        CliTelemetryLaunchSource Source);
}
