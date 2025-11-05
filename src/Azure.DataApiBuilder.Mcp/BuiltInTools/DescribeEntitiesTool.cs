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
        /// Gets the metadata for the delete-record tool, including its name, description, and input schema.
        /// </summary>
        /// <returns></returns>
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
                                ""description"": ""If true, only entity names and descriptions will be returned. If false, full metadata including fields, parameters etc. will be included. Default is false.""
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
        /// Executes the DescribeEntities tool, returning metadata about configured entities.
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

                RuntimeConfigProvider runtimeConfigProvider = serviceProvider.GetRequiredService<RuntimeConfigProvider>();
                RuntimeConfig runtimeConfig = runtimeConfigProvider.GetConfig();

                if (!IsToolEnabled(runtimeConfig))
                {
                    return Task.FromResult(McpResponseBuilder.BuildErrorResult(
                        "ToolDisabled",
                        $"The {GetToolMetadata().Name} tool is disabled in the configuration.",
                        logger));
                }

                (bool nameOnly, HashSet<string>? entityFilter) = ParseArguments(arguments, logger);

                List<Dictionary<string, object?>> entityList = new();

                if (runtimeConfig.Entities != null)
                {
                    foreach (KeyValuePair<string, Entity> entityEntry in runtimeConfig.Entities)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string entityName = entityEntry.Key;
                        Entity entity = entityEntry.Value;

                        if (!ShouldIncludeEntity(entityName, entityFilter))
                        {
                            continue;
                        }

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
                        }
                    }
                }

                if (entityList.Count == 0)
                {
                    if (entityFilter != null && entityFilter.Count > 0)
                    {
                        return Task.FromResult(McpResponseBuilder.BuildErrorResult(
                            "EntitiesNotFound",
                            $"No entities found matching the filter: {string.Join(", ", entityFilter)}",
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

                entityList = entityList.OrderBy(e => e["name"]?.ToString() ?? string.Empty).ToList();

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
        /// <returns>A dictionary with two keys: "name", containing the entity name, and "description", containing the entity's
        /// description or an empty string if the description is null.</returns>
        private static Dictionary<string, object?> BuildBasicEntityInfo(string entityName, Entity entity)
        {
            return new Dictionary<string, object?>
            {
                ["name"] = entityName,
                ["description"] = entity.Description ?? string.Empty
            };
        }

        /// <summary>
        /// Builds full entity info: name, description, fields, parameters (for stored procs), permissions.
        /// </summary>
        private static Dictionary<string, object?> BuildFullEntityInfo(string entityName, Entity entity)
        {
            Dictionary<string, object?> info = new()
            {
                ["name"] = entityName,
                ["description"] = entity.Description ?? string.Empty,
                ["fields"] = BuildFieldMetadataInfo(entity.Fields),
            };

            if (entity.Source.Type == EntitySourceType.StoredProcedure)
            {
                info["parameters"] = BuildParameterMetadataInfo(entity.Source.Parameters);
            }

            info["permissions"] = BuildPermissionsInfo(entity);

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
                        name = field.Name,
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
        /// <returns>A list of anonymous objects, each containing the parameter's name, whether it is required, its default
        /// value, and its description. Returns an empty list if <paramref name="parameters"/> is null.</returns>
        private static List<object> BuildParameterMetadataInfo(List<ParameterMetadata>? parameters)
        {
            List<object> result = new();

            if (parameters != null)
            {
                foreach (ParameterMetadata param in parameters)
                {
                    result.Add(new
                    {
                        name = param.Name,
                        required = param.Default == null, // required if no default
                        @default = param.Default,
                        description = param.Description ?? string.Empty
                    });
                }
            }

            return result;
        }

        /// <summary>
        /// Build a list of permission metadata info
        /// </summary>
        /// <param name="entity">The entity object</param>
        /// <returns>A list of permissions available to the entity</returns>
        private static string[] BuildPermissionsInfo(Entity entity)
        {
            if (entity.Permissions == null)
            {
                return Array.Empty<string>();
            }

            bool isStoredProcedure = entity.Source.Type == EntitySourceType.StoredProcedure;
            HashSet<EntityActionOperation> validOperations = isStoredProcedure
                ? EntityAction.ValidStoredProcedurePermissionOperations
                : EntityAction.ValidPermissionOperations;

            HashSet<string> permissions = new(StringComparer.OrdinalIgnoreCase);

            foreach (EntityPermission permission in entity.Permissions)
            {
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
