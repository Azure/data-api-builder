// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Microsoft.Extensions.Hosting;

namespace Azure.DataApiBuilder.Service.Utilities
{
    /// <summary>
    /// Stops and drains serialized runtime configuration work during the hosted-service shutdown
    /// phase, before the root service provider disposes any hot-reload subscriber dependencies.
    /// </summary>
    internal sealed class RuntimeConfigLoaderShutdownService(
        FileSystemRuntimeConfigLoader configLoader) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            // The loader first requests cancellation through its own token, then this host token
            // bounds the drain according to HostOptions.ShutdownTimeout.
            return configLoader.StopAsync(cancellationToken);
        }
    }
}