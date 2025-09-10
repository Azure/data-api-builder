// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;

namespace Azure.DataApiBuilder.Mcp.Tools
{
    public class DescribeEntitiesTool : IMcpTool
    {
        public Tool GetToolMetadata()
        {
            return new Tool
            {
                Name = "describe_entities",
                Description = "Lists all entities in the database."
            };
        }

        public async Task<CallToolResult> ExecuteAsync(
            JsonDocument? arguments,
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken = default)
        {
            // Create a scope to resolve scoped services
            using IServiceScope scope = serviceProvider.CreateScope();
            IServiceProvider scopedProvider = scope.ServiceProvider;

            // Set the service provider for DmlTools
            Extensions.ServiceProvider = scopedProvider;

            // Call the DescribeEntities tool method
            string entitiesJson = await DmlTools.DescribeEntities();

            return new CallToolResult
            {
                Content = [new TextContentBlock { Type = "application/json", Text = entitiesJson }]
            };
        }
    }
}
