// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.DataApiBuilder.Mcp.Utils
{
    /// <summary>
    /// Utility class for MCP telemetry operations.
    /// </summary>
    internal static class McpTelemetryHelper
    {
        /// <summary>
        /// Infers the operation type from the tool name.
        /// </summary>
        /// <param name="toolName">The name of the tool.</param>
        /// <returns>The inferred operation type.</returns>
        public static string InferOperationFromToolName(string toolName)
        {
            return toolName.ToLowerInvariant() switch
            {
                string s when s.Contains("read") || s.Contains("get") || s.Contains("list") || s.Contains("describe") => "read",
                string s when s.Contains("create") || s.Contains("insert") => "create",
                string s when s.Contains("update") || s.Contains("modify") => "update",
                string s when s.Contains("delete") || s.Contains("remove") => "delete",
                string s when s.Contains("execute") => "execute",
                _ => "execute"
            };
        }

        /// <summary>
        /// Extracts metadata from a custom tool for telemetry purposes.
        /// </summary>
        /// <param name="customTool">The custom tool instance.</param>
        /// <param name="serviceProvider">The service provider.</param>
        /// <returns>A tuple containing the entity name and database procedure name.</returns>
        public static (string? entityName, string? dbProcedure) ExtractCustomToolMetadata(Core.DynamicCustomTool customTool, IServiceProvider serviceProvider)
        {
            try
            {
                // Access public properties instead of reflection
                string? entityName = customTool.EntityName;

                if (entityName != null)
                {
                    // Try to get the stored procedure name from the runtime configuration
                    RuntimeConfigProvider? runtimeConfigProvider = serviceProvider.GetService<RuntimeConfigProvider>();
                    if (runtimeConfigProvider != null)
                    {
                        RuntimeConfig config = runtimeConfigProvider.GetConfig();
                        if (config.Entities.TryGetValue(entityName, out Entity? entityConfig))
                        {
                            string? dbProcedure = entityConfig.Source.Object;
                            return (entityName, dbProcedure);
                        }
                    }
                }

                return (entityName, null);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
            {
                // If configuration access fails due to invalid state or arguments, return null values
                // This is expected during startup or configuration changes
                return (null, null);
            }
        }
    }
}
