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
            // The loader's own shutdown token cancels supported external I/O. Do not detach an
            // active operation on host timeout: doing so would let it use disposed singletons.
            return configLoader.StopAsync(CancellationToken.None);
        }
    }
}