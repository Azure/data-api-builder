// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Mcp.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Azure.DataApiBuilder.Mcp.Core
{
    /// <summary>
    /// Hosted service to initialize the MCP tool registry
    /// </summary>
    public class McpToolRegistryInitializer : IHostedService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly McpToolRegistry _toolRegistry;

        public McpToolRegistryInitializer(IServiceProvider serviceProvider, McpToolRegistry toolRegistry)
        {
            _serviceProvider = serviceProvider;
            _toolRegistry = toolRegistry;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            IEnumerable<IMcpTool> tools = _serviceProvider.GetServices<IMcpTool>();
            McpToolRegistry.InitializeAndRegisterTools(tools, _toolRegistry, _serviceProvider);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
