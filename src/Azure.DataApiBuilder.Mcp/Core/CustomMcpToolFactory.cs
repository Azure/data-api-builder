// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Mcp.Model;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Mcp.Core
{
    /// <summary>
    /// Factory for creating custom MCP tools from stored procedure entity configurations.
    /// Scans runtime configuration and generates dynamic tools for entities marked with custom-tool enabled.
    /// </summary>
    public class CustomMcpToolFactory
    {
        /// <summary>
        /// Creates custom MCP tools from entities configured with "mcp": { "custom-tool": true }.
        /// </summary>
        /// <param name="config">The runtime configuration containing entity definitions.</param>
        /// <param name="logger">Optional logger for diagnostic information.</param>
        /// <returns>Enumerable of custom tools generated from configuration.</returns>
        public static IEnumerable<IMcpTool> CreateCustomTools(RuntimeConfig config, ILogger? logger = null)
        {
            if (config?.Entities == null)
            {
                logger?.LogWarning("No entities found in runtime configuration for custom tool generation.");
                return Enumerable.Empty<IMcpTool>();
            }

            List<IMcpTool> customTools = new();

            foreach ((string entityName, Entity entity) in config.Entities)
            {
                // Filter: Only stored procedures with custom-tool enabled
                if (entity.Source.Type == EntitySourceType.StoredProcedure &&
                    entity.Mcp?.CustomToolEnabled == true)
                {
                    try
                    {
                        DynamicCustomTool tool = new(entityName, entity);

                        logger?.LogInformation(
                            "Created custom MCP tool '{ToolName}' for stored procedure entity '{EntityName}'",
                            tool.GetToolMetadata().Name,
                            entityName);

                        customTools.Add(tool);
                    }
                    catch (Exception ex)
                    {
                        logger?.LogError(
                            ex,
                            "Failed to create custom tool for entity '{EntityName}'. Skipping.",
                            entityName);
                    }
                }
            }

            logger?.LogInformation("Custom MCP tool generation complete. Created {Count} custom tools.", customTools.Count);
            return customTools;
        }
    }
}
