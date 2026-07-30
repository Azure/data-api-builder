// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Mcp.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Azure.DataApiBuilder.Mcp.Core
{
    /// <summary>
    /// Compatibility hosted service for callers that previously constructed the registry initializer
    /// directly. DAB startup uses <see cref="McpToolRegistryRefreshService"/>.
    /// </summary>
    [Obsolete($"Use {nameof(McpToolRegistryRefreshService)} instead.")]
    public class McpToolRegistryInitializer : IHostedService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly McpToolRegistry _toolRegistry;

        public McpToolRegistryInitializer(
            IServiceProvider serviceProvider,
            McpToolRegistry toolRegistry)
        {
            _serviceProvider = serviceProvider;
            _toolRegistry = toolRegistry;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IMcpToolRegistryRefreshService? refreshService =
                _serviceProvider.GetService<IMcpToolRegistryRefreshService>();
            if (refreshService is not null)
            {
                refreshService.EnsureInitialized();
                return Task.CompletedTask;
            }

            // Preserve the legacy behavior for manually assembled service providers that have not
            // registered the refresh service.
            foreach (IMcpTool tool in _serviceProvider.GetServices<IMcpTool>())
            {
                if (tool is DynamicCustomTool customTool)
                {
                    customTool.InitializeMetadata(_serviceProvider);
                }

                _toolRegistry.RegisterTool(tool);
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
