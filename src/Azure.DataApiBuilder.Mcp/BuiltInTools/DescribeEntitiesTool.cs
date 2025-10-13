// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Mcp.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using static Azure.DataApiBuilder.Mcp.Model.McpEnums;

namespace Azure.DataApiBuilder.Mcp.BuiltInTools
{
    /// <summary>
    /// Tool to describe all entities configured in DAB, including their types and metadata.
    /// </summary>
    public class DescribeEntitiesTool : IMcpTool
    {
        /// <summary>
        /// Gets the type of the tool, which is BuiltIn for this implementation.
        /// </summary>
        public ToolType ToolType { get; } = ToolType.BuiltIn;

        /// <summary>
        /// Gets the metadata for the describe_entities tool.
        /// </summary>
        public Tool GetToolMetadata()
        {
            return new Tool
            {
                Name = "describe_entities",
                Description = "Lists and describes all entities in the database, including their types and available operations."
            };
        }

        /// <summary>
        /// Executes the describe_entities tool, returning metadata about all configured entities.
        /// </summary>
        public Task<CallToolResult> ExecuteAsync(
            JsonDocument? arguments,
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken = default)
        {
            ILogger<DescribeEntitiesTool>? logger = serviceProvider.GetService<ILogger<DescribeEntitiesTool>>();

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

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
                List<object> entityList = new();

                if (runtimeConfig.Entities != null)
                {
                    foreach (KeyValuePair<string, Entity> entityEntry in runtimeConfig.Entities)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        Entity entity = entityEntry.Value;

                        Dictionary<string, object?> entityInfo = new()
                        {
                            ["name"] = entityEntry.Key,
                            ["description"] = entity.Description ?? string.Empty
                        };

                        // Add fields for stored procedure entity type only
                        if (entity.Source.Type == EntitySourceType.StoredProcedure)
                        {
                            List<object> fields = new();

                            // Try to get stored procedure parameters if available
                            if (entity.Source.Parameters != null)
                            {
                                foreach (KeyValuePair<string, object> parameter in entity.Source.Parameters)
                                {
                                    fields.Add(new
                                    {
                                        name = parameter.Key,
                                        // TODO: "type" and "description"
                                    });
                                }
                            }

                            entityInfo["fields"] = fields;
                        }

                        // Extract permissions from entity configuration
                        List<string> permissions = new();

                        if (entity.Permissions != null)
                        {
                            foreach (EntityPermission permission in entity.Permissions)
                            {
                                foreach (EntityAction action in permission.Actions)
                                {
                                    permissions.Add(action.Action.ToString().ToUpperInvariant());
                                }
                            }
                        }

                        entityInfo["permissions"] = permissions.Distinct().ToArray();
                        entityList.Add(entityInfo);
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();

                // Return the entities directly as an array
                logger?.LogInformation("DescribeEntitiesTool returned {EntityCount} entities.", entityList.Count);

                return Task.FromResult(new CallToolResult
                {
                    Content = [new TextContentBlock {
                        Type = "text",
                        Text = JsonSerializer.Serialize(entityList, new JsonSerializerOptions { WriteIndented = true })
                    }]
                });
            }
            catch (OperationCanceledException)
            {
                return Task.FromResult(new CallToolResult
                {
                    Content = [new TextContentBlock { Type = "text", Text = "Error: The describe operation was canceled." }]
                });
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Unexpected error in DescribeEntitiesTool.");

                return Task.FromResult(new CallToolResult
                {
                    Content = [new TextContentBlock { Type = "text", Text = $"Error: {ex.Message}" }]
                });
            }
        }
    }
}
