// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Mcp.Model;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using static Azure.DataApiBuilder.Mcp.Model.McpEnums;

namespace Azure.DataApiBuilder.Mcp.BuiltInTools
{
    public class DescribeEntitiesTool : IMcpTool
    {
        public ToolType ToolType { get; } = ToolType.BuiltIn;

        public Tool GetToolMetadata()
        {
            return new Tool
            {
                Name = "describe-entities",
                Description = "Lists and describes all entities in the database."
            };
        }

        public Task<CallToolResult> ExecuteAsync(
            JsonDocument? arguments,
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Get the runtime config provider
                RuntimeConfigProvider? runtimeConfigProvider = serviceProvider.GetService<RuntimeConfigProvider>();
                if (runtimeConfigProvider == null || !runtimeConfigProvider.TryGetConfig(out RuntimeConfig? runtimeConfig))
                {
                    return Task.FromResult(new CallToolResult
                    {
                        Content = [new TextContentBlock { Type = "text", Text = "Error: Runtime configuration not available." }]
                    });
                }

                // Extract entity information from the runtime config
                Dictionary<string, object> entities = new();

                if (runtimeConfig.Entities != null)
                {
                    foreach (KeyValuePair<string, Entity> entity in runtimeConfig.Entities)
                    {
                        entities[entity.Key] = new
                        {
                            source = entity.Value.Source,
                            permissions = entity.Value.Permissions?.Select(p => new
                            {
                                role = p.Role,
                                actions = p.Actions
                            })
                        };
                    }
                }

                string entitiesJson = JsonSerializer.Serialize(entities, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                return Task.FromResult(new CallToolResult
                {
                    Content = [new TextContentBlock { Type = "application/json", Text = entitiesJson }]
                });
            }
            catch (Exception ex)
            {
                return Task.FromResult(new CallToolResult
                {
                    Content = [new TextContentBlock { Type = "text", Text = $"Error: {ex.Message}" }]
                });
            }
        }
    }
}
