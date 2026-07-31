// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Core.Configurations;
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

            // Preserve compatibility for manually assembled service providers without publishing
            // disabled tools. Snapshot discovery requires the configuration used to evaluate each
            // tool's visibility, so this fallback now requires RuntimeConfigProvider as well.
            RuntimeConfigProvider runtimeConfigProvider =
                _serviceProvider.GetService<RuntimeConfigProvider>()
                ?? throw new InvalidOperationException(
                    $"{nameof(RuntimeConfigProvider)} must be registered when using the legacy " +
                    $"{nameof(McpToolRegistryInitializer)} fallback.");
            IMcpTool[] tools = _serviceProvider.GetServices<IMcpTool>().ToArray();
            foreach (DynamicCustomTool customTool in tools.OfType<DynamicCustomTool>())
            {
                customTool.InitializeMetadata(_serviceProvider);
            }

            _toolRegistry.ReplaceAll(tools, runtimeConfigProvider.GetConfig());

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
