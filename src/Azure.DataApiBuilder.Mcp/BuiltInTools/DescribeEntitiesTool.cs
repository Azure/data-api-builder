// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Mcp.Model;
using Azure.DataApiBuilder.Mcp.Utils;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.AspNetCore.Http;
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
        /// Gets the metadata for the describe-entities tool, including its name, description, and input schema.
        /// </summary>
        /// <returns></returns>
        public Tool GetToolMetadata()
        {
            return new Tool
            {
                Name = "describe_entities",
                Description = "Lists all entities and metadata. ALWAYS CALL FIRST. Each entity includes: name, type, fields, parameters, and permissions. The permissions array defines which tools are allowed. 'ALL' expands by type: data->CREATE, READ, UPDATE, DELETE.",
                InputSchema = JsonSerializer.Deserialize<JsonElement>(
                    @"{
                        ""type"": ""object"",
                        ""properties"": {
                            ""nameOnly"": {
                                ""type"": ""boolean"",
                                ""description"": ""If true, the response includes only entity names and short summaries, omitting detailed metadata such as fields, parameters, and permissions. Use this when the database contains many entities and the full payload would be too large. The usual strategy is: first call describe_entities with nameOnly=true to get a lightweight list, then call describe_entities again with nameOnly=false for specific entities that require full metadata. This flag is meant for discovery, not execution planning. The model must not assume that nameOnly=true provides enough detail for CRUD or EXECUTE operations.""
                            },
                            ""entities"": {
                                ""type"": ""array"",
                                ""items"": {
                                    ""type"": ""string""
                                },
                                ""description"": ""Optional list of entity names to describe in full detail. Use this to reduce payload size when only certain entities are relevant. Do NOT pass both entities[] and nameOnly=true together, as that combination is nonsensical: nameOnly=true ignores detailed metadata, while entities[] explicitly requests it. Choose one approach—broad discovery with nameOnly=true OR targeted metadata with entities[].""
                            }
                        }
                    }"
                )
            };
        }

        /// <summary>
        /// Executes the DescribeEntities tool, returning metadata about configured entities.
        /// </summary>
        public Task<CallToolResult> ExecuteAsync(
            JsonDocument? arguments,
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken = default)
        {
            ILogger<DescribeEntitiesTool>? logger = serviceProvider.GetService<ILogger<DescribeEntitiesTool>>();
            string toolName = GetToolMetadata().Name;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                RuntimeConfigProvider runtimeConfigProvider = serviceProvider.GetRequiredService<RuntimeConfigProvider>();
                RuntimeConfig runtimeConfig = runtimeConfigProvider.GetConfig();

                if (!IsToolEnabled(runtimeConfig))
                {
                    return Task.FromResult(McpErrorHelpers.ToolDisabled(GetToolMetadata().Name, logger));
                }

                // Get authorization services to determine current user's role
                IAuthorizationResolver authResolver = serviceProvider.GetRequiredService<IAuthorizationResolver>();
                IHttpContextAccessor httpContextAccessor = serviceProvider.GetRequiredService<IHttpContextAccessor>();
                HttpContext? httpContext = httpContextAccessor.HttpContext;

                // Get current user's role for permission filtering
                // For discovery tools like describe_entities, we use the first valid role from the header
                // This differs from operation-specific tools that check permissions per entity per operation
                string? currentUserRole = null;
                if (httpContext != null && authResolver.IsValidRoleContext(httpContext))
                {
                    string roleHeader = httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER].ToString();
                    if (!string.IsNullOrWhiteSpace(roleHeader))
                    {
                        string[] roles = roleHeader
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                        if (roles.Length > 1)
                        {
                            logger?.LogWarning("Multiple roles detected in request header: [{Roles}]. Using first role '{FirstRole}' for entity discovery. " +
                                "Consider using a single role for consistent permission reporting.",
                                string.Join(", ", roles), roles[0]);
                        }

                        // For discovery operations, take the first role from comma-separated list
                        // This provides a consistent view of available entities for the primary role
                        currentUserRole = roles.FirstOrDefault();
                    }
                }

                // Get current user's role for permission filtering
                // For discovery tools like describe_entities, we use the first valid role from the header
                // This differs from operation-specific tools that check permissions per entity per operation
                if (httpContext != null && authResolver.IsValidRoleContext(httpContext))
                {
                    string roleHeader = httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER].ToString();
                    if (!string.IsNullOrWhiteSpace(roleHeader))
                    {
                        string[] roles = roleHeader
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                        if (roles.Length > 1)
                        {
                            logger?.LogWarning("Multiple roles detected in request header: [{Roles}]. Using first role '{FirstRole}' for entity discovery. " +
                                "Consider using a single role for consistent permission reporting.",
                                string.Join(", ", roles), roles[0]);
                        }

                        // For discovery operations, take the first role from comma-separated list
                        // This provides a consistent view of available entities for the primary role
                        currentUserRole = roles.FirstOrDefault();
                    }
                }

                (bool nameOnly, HashSet<string>? entityFilter) = ParseArguments(arguments, logger);

                if (currentUserRole == null)
                {
                    logger?.LogWarning("Current user role could not be determined from HTTP context or role header. " +
                        "Entity permissions will be empty (no permissions shown) rather than using anonymous permissions. " +
                        "Ensure the '{RoleHeader}' header is properly set.", AuthorizationResolver.CLIENT_ROLE_HEADER);
                }

                List<Dictionary<string, object?>> entityList = new();

                if (runtimeConfig.Entities != null)
                {
                    foreach (KeyValuePair<string, Entity> entityEntry in runtimeConfig.Entities)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string entityName = entityEntry.Key;
                        Entity entity = entityEntry.Value;

                        // Skip stored procedures exposed as custom tools - they appear in tools/list instead
                        if (entity.Source.Type == EntitySourceType.StoredProcedure &&
                            entity.Mcp?.CustomToolEnabled == true)
                        {
                            continue;
                        }

                        if (!ShouldIncludeEntity(entityName, entityFilter))
                        {
                            continue;
                        }

                        try
                        {
                            Dictionary<string, object?> entityInfo = nameOnly
                                ? BuildBasicEntityInfo(entityName, entity)
                                : BuildFullEntityInfo(entityName, entity, currentUserRole);

                            entityList.Add(entityInfo);
                        }
                        catch (Exception ex)
                        {
                            logger?.LogWarning(ex, "Failed to build info for entity {EntityName}", entityName);
                        }
                    }
                }

                if (entityList.Count == 0)
                {
                    if (entityFilter != null && entityFilter.Count > 0)
                    {
                        return Task.FromResult(McpResponseBuilder.BuildErrorResult(
                            toolName,
                            "EntitiesNotFound",
                            $"No entities found matching the filter: {string.Join(", ", entityFilter)}",
                            logger));
                    }
                    else
                    {
                        return Task.FromResult(McpResponseBuilder.BuildErrorResult(
                            toolName,
                            "NoEntitiesConfigured",
                            "No entities are configured in the runtime configuration.",
                            logger));
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();

                entityList = entityList.OrderBy(e => e["name"]?.ToString() ?? string.Empty).ToList();

                List<object> finalEntityList = entityList.Cast<object>().ToList();

                Dictionary<string, object?> responseData = new()
                {
                    ["entities"] = finalEntityList,
                    ["count"] = finalEntityList.Count
                };

                logger?.LogInformation(
                    "DescribeEntitiesTool returned {EntityCount} entities. Response type: {ResponseType} (nameOnly={NameOnly}).",
                    finalEntityList.Count,
                    nameOnly ? "lightweight summary (names and descriptions only)" : "full metadata with fields, parameters, and permissions",
                    nameOnly);

                return Task.FromResult(McpResponseBuilder.BuildSuccessResult(
                    responseData,
                    logger,
                    $"DescribeEntitiesTool success: {finalEntityList.Count} entities returned."));
            }
            catch (OperationCanceledException)
            {
                return Task.FromResult(McpResponseBuilder.BuildErrorResult(
                    toolName,
                    "OperationCanceled",
                    "The describe operation was canceled.",
                    logger));
            }
            catch (DataApiBuilderException dabEx)
            {
                logger?.LogError(dabEx, "Data API Builder error in DescribeEntitiesTool");
                return Task.FromResult(McpResponseBuilder.BuildErrorResult(
                    toolName,
                    "DataApiBuilderError",
                    dabEx.Message,
                    logger));
            }
            catch (ArgumentException argEx)
            {
                return Task.FromResult(McpResponseBuilder.BuildErrorResult(
                    toolName,
                    "InvalidArguments",
                    argEx.Message,
                    logger));
            }
            catch (InvalidOperationException ioEx)
            {
                logger?.LogError(ioEx, "Invalid operation in DescribeEntitiesTool");
                return Task.FromResult(McpResponseBuilder.BuildErrorResult(
                    toolName,
                    "InvalidOperation",
                    "Failed to retrieve entity metadata: " + ioEx.Message,
                    logger));
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Unexpected error in DescribeEntitiesTool");
                return Task.FromResult(McpResponseBuilder.BuildErrorResult(
                    toolName,
                    "UnexpectedError",
                    "An unexpected error occurred while describing entities.",
                    logger));
            }
        }

        /// <summary>
        /// Determines whether the tool is enabled based on the specified runtime configuration.
        /// </summary>
        /// <param name="runtimeConfig">The runtime configuration to evaluate. Must not be null.</param>
        /// <returns><see langword="true"/> if the tool is enabled and the <c>DescribeEntities</c> property of <c>McpDmlTools</c>
        /// is set to <see langword="true"/>; otherwise, <see langword="false"/>.</returns>
        private static bool IsToolEnabled(RuntimeConfig runtimeConfig)
        {
            return runtimeConfig.McpDmlTools?.DescribeEntities == true;
        }

        /// <summary>
        /// Parses the input arguments to extract the 'nameOnly' flag and the optional entity filter list.
        /// </summary>
        /// <param name="arguments">The arguments to parse</param>
        /// <param name="logger">The logger</param>
        /// <returns>A tuple containing the parsed 'nameOnly' flag and the optional entity filter list.</returns>
        private static (bool nameOnly, HashSet<string>? entityFilter) ParseArguments(JsonDocument? arguments, ILogger? logger)
        {
            bool nameOnly = false;
            HashSet<string>? entityFilter = null;

            if (arguments?.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (arguments.RootElement.TryGetProperty("nameOnly", out JsonElement nameOnlyElement))
                {
                    if (nameOnlyElement.ValueKind == JsonValueKind.True || nameOnlyElement.ValueKind == JsonValueKind.False)
                    {
                        nameOnly = nameOnlyElement.GetBoolean();
                    }
                }

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
        /// Determines whether the specified entity should be included based on the provided entity filter.
        /// </summary>
        /// <param name="entityName">The name of the entity to evaluate.</param>
        /// <param name="entityFilter">A set of entity names to include. If <see langword="null"/> or empty, all entities are included.</param>
        /// <returns><see langword="true"/> if the entity should be included; otherwise, <see langword="false"/>.</returns>
        private static bool ShouldIncludeEntity(string entityName, HashSet<string>? entityFilter)
        {
            return entityFilter == null || entityFilter.Count == 0 || entityFilter.Contains(entityName);
        }

        /// <summary>
        /// Creates a dictionary containing basic information about an entity.
        /// </summary>
        /// <param name="entityName">The name of the entity to include in the dictionary.</param>
        /// <param name="entity">The entity object from which to extract additional information.</param>
        /// <returns>A dictionary with two keys: "name", containing the entity alias (or name if no alias), and "description", containing the entity's
        /// description or an empty string if the description is null.</returns>
        private static Dictionary<string, object?> BuildBasicEntityInfo(string entityName, Entity entity)
        {
            // Use GraphQL singular name as alias if available, otherwise use entity name
            string displayName = !string.IsNullOrWhiteSpace(entity.GraphQL?.Singular)
                ? entity.GraphQL.Singular
                : entityName;

            return new Dictionary<string, object?>
            {
                ["name"] = displayName,
                ["description"] = entity.Description ?? string.Empty
            };
        }

        /// <summary>
        /// Builds full entity info: name, description, fields, parameters (for stored procs), permissions.
        /// </summary>
        /// <param name="entityName">The name of the entity to include in the dictionary.</param>
        /// <param name="entity">The entity object from which to extract additional information.</param>
        /// <param name="currentUserRole">The role of the current user, used to determine permissions.</param>
        /// <returns>
        /// A dictionary containing the entity's name, description, fields, parameters (if applicable), and permissions.
        /// </returns>
        private static Dictionary<string, object?> BuildFullEntityInfo(string entityName, Entity entity, string? currentUserRole)
        {
            // Use GraphQL singular name as alias if available, otherwise use entity name
            string displayName = !string.IsNullOrWhiteSpace(entity.GraphQL?.Singular)
                ? entity.GraphQL.Singular
                : entityName;

            Dictionary<string, object?> info = new()
            {
                ["name"] = displayName,
                ["description"] = entity.Description ?? string.Empty,
                ["fields"] = BuildFieldMetadataInfo(entity.Fields),
            };

            if (entity.Source.Type == EntitySourceType.StoredProcedure)
            {
                info["parameters"] = BuildParameterMetadataInfo(entity.Source.Parameters);
            }

            info["permissions"] = BuildPermissionsInfo(entity, currentUserRole);

            return info;
        }

        /// <summary>
        /// Builds a list of metadata information objects from the provided collection of fields.
        /// </summary>
        /// <param name="fields">A list of <see cref="FieldMetadata"/> objects representing the fields to process. Can be null.</param>
        /// <returns>A list of objects, each containing the name and description of a field. If <paramref name="fields"/> is
        /// null, an empty list is returned.</returns>
        private static List<object> BuildFieldMetadataInfo(List<FieldMetadata>? fields)
        {
            List<object> result = new();

            if (fields != null)
            {
                foreach (FieldMetadata field in fields)
                {
                    result.Add(new
                    {
                        name = field.Alias ?? field.Name,
                        description = field.Description ?? string.Empty
                    });
                }
            }

            return result;
        }

        /// <summary>
        /// Builds a list of parameter metadata objects containing information about each parameter.
        /// </summary>
        /// <param name="parameters">A list of <see cref="ParameterMetadata"/> objects representing the parameters to process. Can be null.</param>
        /// <returns>A list of dictionaries, each containing the parameter's name, whether it is required, its default
        /// value, and its description. Returns an empty list if <paramref name="parameters"/> is null.</returns>
        private static List<object> BuildParameterMetadataInfo(List<ParameterMetadata>? parameters)
        {
            List<object> result = new();

            if (parameters != null)
            {
                foreach (ParameterMetadata param in parameters)
                {
                    Dictionary<string, object?> paramInfo = new()
                    {
                        ["name"] = param.Name,
                        ["required"] = param.Required,
                        ["default"] = param.Default,
                        ["description"] = param.Description ?? string.Empty
                    };
                    result.Add(paramInfo);
                }
            }

            return result;
        }

        /// <summary>
        /// Build a list of permission metadata info for the current user's role
        /// </summary>
        /// <param name="entity">The entity object</param>
        /// <param name="currentUserRole">The current user's role - if null, returns empty permissions</param>
        /// <returns>A list of permissions available to the current user's role for this entity</returns>
        private static string[] BuildPermissionsInfo(Entity entity, string? currentUserRole)
        {
            if (entity.Permissions == null || string.IsNullOrWhiteSpace(currentUserRole))
            {
                return Array.Empty<string>();
            }

            bool isStoredProcedure = entity.Source.Type == EntitySourceType.StoredProcedure;
            HashSet<EntityActionOperation> validOperations = isStoredProcedure
                ? EntityAction.ValidStoredProcedurePermissionOperations
                : EntityAction.ValidPermissionOperations;

            HashSet<string> permissions = new(StringComparer.OrdinalIgnoreCase);

            // Only include permissions for the current user's role
            foreach (EntityPermission permission in entity.Permissions)
            {
                // Check if this permission applies to the current user's role
                if (!string.Equals(permission.Role, currentUserRole, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (EntityAction action in permission.Actions)
                {
                    if (action.Action == EntityActionOperation.All)
                    {
                        foreach (EntityActionOperation op in validOperations)
                        {
                            permissions.Add(op.ToString().ToUpperInvariant());
                        }
                    }
                    else
                    {
                        permissions.Add(action.Action.ToString().ToUpperInvariant());
                    }
                }
            }

            return permissions.OrderBy(p => p).ToArray();
        }
    }
}
