// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Mcp.Model;
using Azure.DataApiBuilder.Mcp.Utils;
using Azure.DataApiBuilder.Service.Exceptions;
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
                Description = "Lists and describes all entities in the database, including their types and available operations.",
                InputSchema = JsonSerializer.Deserialize<JsonElement>(
                    @"{
                        ""type"": ""object"",
                        ""properties"": {
                            ""nameOnly"": {
                                ""type"": ""boolean"",
                                ""description"": ""If true, only entity names and descriptions will be returned. If false, full metadata including fields, permissions, etc. will be included. Default is false.""
                            },
                            ""entities"": {
                                ""type"": ""array"",
                                ""items"": {
                                    ""type"": ""string""
                                },
                                ""description"": ""Optional list of specific entity names to filter by. If empty, all entities will be described.""
                            }
                        }
                    }"
                )
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
                // Cancellation check at the start
                cancellationToken.ThrowIfCancellationRequested();

                // 1) Resolve required services & configuration
                RuntimeConfigProvider runtimeConfigProvider = serviceProvider.GetRequiredService<RuntimeConfigProvider>();
                RuntimeConfig runtimeConfig = runtimeConfigProvider.GetConfig();

                // 2) Check if the tool is enabled in configuration before proceeding
                if (!IsToolEnabled(runtimeConfig))
                {
                    return Task.FromResult(McpResponseBuilder.BuildErrorResult(
                        "ToolDisabled",
                        $"The {GetToolMetadata().Name} tool is disabled in the configuration.",
                        logger));
                }

                // 3) Parse arguments
                (bool nameOnly, HashSet<string>? entityFilter) = ParseArguments(arguments, logger);

                // 4) Build entity list
                List<Dictionary<string, object?>> entityList = new();
                int skippedEntities = 0;

                if (runtimeConfig.Entities != null)
                {
                    foreach (KeyValuePair<string, Entity> entityEntry in runtimeConfig.Entities)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string entityName = entityEntry.Key;
                        Entity entity = entityEntry.Value;

                        // Apply entity filter if specified
                        if (!ShouldIncludeEntity(entityName, entityFilter))
                        {
                            continue;
                        }

                        // Skip if entity MCP DML tools are disabled (when property is available)
                        if (!IsEntityMcpEnabled(entityEntry, logger))
                        {
                            skippedEntities++;
                            continue;
                        }

                        // TODO: Apply role-based filtering when available
                        // Skip entities where the current role has no permissions

                        // Build entity information based on nameOnly flag
                        try
                        {
                            Dictionary<string, object?> entityInfo = nameOnly
                                ? BuildBasicEntityInfo(entityName, entity)
                                : BuildFullEntityInfo(entityName, entity);

                            entityList.Add(entityInfo);
                        }
                        catch (Exception ex)
                        {
                            logger?.LogWarning(ex, "Failed to build info for entity {EntityName}", entityName);
                            // Continue with other entities
                        }
                    }
                }

                // Check if any entities were found
                if (entityList.Count == 0)
                {
                    if (entityFilter != null && entityFilter.Count > 0)
                    {
                        return Task.FromResult(McpResponseBuilder.BuildErrorResult(
                            "EntitiesNotFound",
                            $"No entities found matching the filter: {string.Join(", ", entityFilter)}",
                            logger));
                    }
                    else if (skippedEntities > 0)
                    {
                        return Task.FromResult(McpResponseBuilder.BuildErrorResult(
                            "NoAccessibleEntities",
                            "No entities are accessible with current permissions or MCP configuration.",
                            logger));
                    }
                    else
                    {
                        return Task.FromResult(McpResponseBuilder.BuildErrorResult(
                            "NoEntitiesConfigured",
                            "No entities are configured in the runtime configuration.",
                            logger));
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();

                // Sort entities by name for consistent output
                entityList = entityList.OrderBy(e => e["name"]?.ToString() ?? string.Empty).ToList();

                // 5) Build response - convert back to List<object> for serialization
                List<object> finalEntityList = entityList.Cast<object>().ToList();

                Dictionary<string, object?> responseData = new()
                {
                    ["entities"] = finalEntityList,
                    ["count"] = finalEntityList.Count,
                    ["mode"] = nameOnly ? "basic" : "full"
                };

                if (entityFilter != null && entityFilter.Count > 0)
                {
                    responseData["filter"] = entityFilter.ToArray();
                }

                logger?.LogInformation(
                    "DescribeEntitiesTool returned {EntityCount} entities in {Mode} mode.",
                    finalEntityList.Count,
                    nameOnly ? "basic" : "full");

                return Task.FromResult(McpResponseBuilder.BuildSuccessResult(
                    responseData,
                    logger,
                    $"DescribeEntitiesTool success: {finalEntityList.Count} entities returned."));
            }
            catch (OperationCanceledException)
            {
                return Task.FromResult(McpResponseBuilder.BuildErrorResult(
                    "OperationCanceled",
                    "The describe operation was canceled.",
                    logger));
            }
            catch (DataApiBuilderException dabEx)
            {
                logger?.LogError(dabEx, "Data API Builder error in DescribeEntitiesTool");
                return Task.FromResult(McpResponseBuilder.BuildErrorResult(
                    "DataApiBuilderError",
                    dabEx.Message,
                    logger));
            }
            catch (ArgumentException argEx)
            {
                return Task.FromResult(McpResponseBuilder.BuildErrorResult(
                    "InvalidArguments",
                    argEx.Message,
                    logger));
            }
            catch (InvalidOperationException ioEx)
            {
                logger?.LogError(ioEx, "Invalid operation in DescribeEntitiesTool");
                return Task.FromResult(McpResponseBuilder.BuildErrorResult(
                    "InvalidOperation",
                    "Failed to retrieve entity metadata: " + ioEx.Message,
                    logger));
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Unexpected error in DescribeEntitiesTool");
                return Task.FromResult(McpResponseBuilder.BuildErrorResult(
                    "UnexpectedError",
                    "An unexpected error occurred while describing entities.",
                    logger));
            }
        }

        /// <summary>
        /// Checks if the tool is enabled in the configuration.
        /// </summary>
        private static bool IsToolEnabled(RuntimeConfig runtimeConfig)
        {
            return runtimeConfig.McpDmlTools?.DescribeEntities == true;
        }

        /// <summary>
        /// Parses the tool arguments to extract nameOnly flag and entity filter.
        /// </summary>
        private static (bool nameOnly, HashSet<string>? entityFilter) ParseArguments(JsonDocument? arguments, ILogger? logger)
        {
            bool nameOnly = false;
            HashSet<string>? entityFilter = null;

            if (arguments?.RootElement.ValueKind == JsonValueKind.Object)
            {
                // Parse nameOnly flag
                if (arguments.RootElement.TryGetProperty("nameOnly", out JsonElement nameOnlyElement))
                {
                    if (nameOnlyElement.ValueKind == JsonValueKind.True || nameOnlyElement.ValueKind == JsonValueKind.False)
                    {
                        nameOnly = nameOnlyElement.GetBoolean();
                    }
                }

                // Parse entities filter
                if (arguments.RootElement.TryGetProperty("entities", out JsonElement entitiesElement) &&
                    entitiesElement.ValueKind == JsonValueKind.Array)
                {
                    entityFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (JsonElement entityElement in entitiesElement.EnumerateArray())
                    {
                        if (entityElement.ValueKind == JsonValueKind.String)
                        {
                            string? entityName = entityElement.GetString();
                            if (!string.IsNullOrWhiteSpace(entityName))
                            {
                                entityFilter.Add(entityName);
                            }
                        }
                    }

                    if (entityFilter.Count == 0)
                    {
                        entityFilter = null;
                    }
                }
            }

            logger?.LogDebug("Parsed arguments - nameOnly: {NameOnly}, entityFilter: {EntityFilter}",
                nameOnly, entityFilter != null ? string.Join(", ", entityFilter) : "none");

            return (nameOnly, entityFilter);
        }

        /// <summary>
        /// Determines if an entity should be included based on the filter.
        /// </summary>
        private static bool ShouldIncludeEntity(string entityName, HashSet<string>? entityFilter)
        {
            if (entityFilter == null || entityFilter.Count == 0)
            {
                return true;
            }

            return entityFilter.Contains(entityName);
        }

        /// <summary>
        /// Checks if MCP DML tools are enabled for a specific entity.
        /// </summary>
        private static bool IsEntityMcpEnabled(KeyValuePair<string, Entity> entityEntry, ILogger? logger)
        {
            // TODO: Implement when entity.Mcp.DmlTools property is available
            logger?.LogInformation($"Entity {entityEntry.Key} not enabled for MCP.");

            // For now, include all entities
            return true;
        }

        /// <summary>
        /// Builds basic entity information (name and description only).
        /// </summary>
        private static Dictionary<string, object?> BuildBasicEntityInfo(string entityName, Entity entity)
        {
            return new Dictionary<string, object?>
            {
                ["name"] = entityName,
                ["description"] = entity.Description ?? string.Empty
            };
        }

        /// <summary>
        /// Builds full entity information including fields, permissions, etc.
        /// </summary>
        private static Dictionary<string, object?> BuildFullEntityInfo(string entityName, Entity entity)
        {
            Dictionary<string, object?> entityInfo = BuildBasicEntityInfo(entityName, entity);

            // Add entity type
            entityInfo["type"] = entity.Source.Type.ToString();

            // Add fields/parameters based on entity type
            entityInfo["fields"] = BuildFieldsInfo(entity);

            // Add permissions
            entityInfo["permissions"] = BuildPermissionsInfo(entity);

            // Add primary key information if available
            if (entity.Source.KeyFields?.Length > 0)
            {
                entityInfo["primaryKey"] = entity.Source.KeyFields;
            }

            // Add relationships if available
            if (entity.Relationships?.Count > 0)
            {
                entityInfo["relationships"] = BuildRelationshipsInfo(entity.Relationships);
            }

            // Add mappings if available
            if (entity.Mappings?.Count > 0)
            {
                entityInfo["mappings"] = entity.Mappings;
            }

            return entityInfo;
        }

        /// <summary>
        /// Builds field information for the entity.
        /// </summary>
        private static List<object> BuildFieldsInfo(Entity entity)
        {
            List<object> fields = new();

            // For stored procedures, return parameters
            if (entity.Source.Type == EntitySourceType.StoredProcedure && entity.Source.Parameters != null)
            {
                foreach (KeyValuePair<string, object> parameter in entity.Source.Parameters)
                {
                    fields.Add(new
                    {
                        name = parameter.Key,
                        parameterType = "stored_procedure_parameter",
                        // TODO: Add type and description when available from metadata
                        type = "unknown",
                        value = parameter.Value?.ToString()
                    });
                }
            }
            else if (entity.Source.Type == EntitySourceType.Table || entity.Source.Type == EntitySourceType.View)
            {
                // TODO: Add actual field information when metadata query is available
                // For now, add a placeholder
                fields.Add(new
                {
                    name = "_placeholder",
                    description = "Field information will be available when metadata query is implemented"
                });
            }

            return fields;
        }

        /// <summary>
        /// Builds permissions information for the entity.
        /// </summary>
        private static string[] BuildPermissionsInfo(Entity entity)
        {
            HashSet<string> permissions = new(StringComparer.OrdinalIgnoreCase);

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

            return permissions.OrderBy(p => p).ToArray();
        }

        /// <summary>
        /// Builds relationships information for the entity.
        /// </summary>
        private static List<object> BuildRelationshipsInfo(IDictionary<string, EntityRelationship> relationships)
        {
            List<object> relationshipList = new();

            foreach (KeyValuePair<string, EntityRelationship> rel in relationships)
            {
                relationshipList.Add(new
                {
                    name = rel.Key,
                    targetEntity = rel.Value.TargetEntity,
                    cardinality = rel.Value.Cardinality.ToString() ?? "unknown",
                    sourceFields = rel.Value.SourceFields ?? Array.Empty<string>(),
                    targetFields = rel.Value.TargetFields ?? Array.Empty<string>()
                });
            }

            return relationshipList;
        }
    }
}
