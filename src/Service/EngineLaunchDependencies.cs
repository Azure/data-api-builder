// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Azure.DataApiBuilder.Service.Telemetry;
using Microsoft.Extensions.Hosting;

namespace Azure.DataApiBuilder.Service
{
    /// <summary>
    /// Immutable per-launch bootstrap overrides. The shared default contains no invocation state;
    /// tests can replace the session factory and host configuration without mutable static seams.
    /// </summary>
    internal sealed record EngineLaunchDependencies(
        Func<bool, ProductTelemetryLaunchContext?, string?, EngineTelemetrySession> SessionFactory,
        Func<IHostBuilder, IHostBuilder>? ConfigureHost = null)
    {
        internal static EngineLaunchDependencies Default { get; } = new(EngineTelemetryHosting.CreateStandalone);
    }
}
