// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data;
using System.Text.Json;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Services;
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

        public bool IsEnabled(RuntimeConfig config) => config.McpDmlTools?.DescribeEntities ?? true;

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

                // Get the caller's role for authorization filtering. DAB uses a single-role request
                // model: the value validated by IsValidRoleContext (via User.IsInRole) is the role
                // used to gate visibility here, matching REST, GraphQL, and the other MCP tools.
                string? currentUserRole = null;
                if (httpContext != null && authResolver.IsValidRoleContext(httpContext))
                {
                    string roleHeader = httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER].ToString();
                    if (!string.IsNullOrWhiteSpace(roleHeader))
                    {
                        currentUserRole = roleHeader;
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

                // Track how many entities were filtered out because DML tools are disabled (dml-tools: false).
                // This helps provide a more specific error message when all entities are filtered.
                int filteredDmlDisabledCount = 0;

                if (runtimeConfig.Entities != null)
                {
                    foreach (KeyValuePair<string, Entity> entityEntry in runtimeConfig.Entities)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string entityName = entityEntry.Key;
                        Entity entity = entityEntry.Value;

                        // Check entity filter first to avoid counting entities that wouldn't be included anyway
                        if (!ShouldIncludeEntity(entityName, entityFilter))
                        {
                            continue;
                        }

                        // Filter out entities when dml-tools is explicitly disabled (false).
                        // This applies to all entity types (tables, views, stored procedures).
                        // When dml-tools is false, the entity is not exposed via DML tools
                        // (read_records, create_record, etc.) and should not appear in describe_entities.
                        if (entity.Mcp?.DmlToolEnabled == false)
                        {
                            filteredDmlDisabledCount++;
                            continue;
                        }

                        // Authorization filtering: skip entities the caller's role has no permission on.
                        // This prevents information disclosure of schema metadata (entity/field/parameter names and descriptions)
                        // for entities the caller is not authorized to access, matching REST/GraphQL/OpenAPI behavior.
                        // If currentUserRole is null, no entities are visible (empty result).
                        if (!HasAnyPermissionForEntity(entityName, entity, currentUserRole, authResolver))
                        {
                            continue;
                        }

                        try
                        {
                            ISqlMetadataProvider? sqlMetadataProvider = null;
                            DatabaseObject? databaseObject = null;
                            if (entity.Source.Type == EntitySourceType.StoredProcedure)
                            {
                                databaseObject = McpMetadataHelper.TryResolveDatabaseObject(
                                    entityName,
                                    runtimeConfig,
                                    serviceProvider,
                                    out string resolveError,
                                    cancellationToken);

                                if (databaseObject is null)
                                {
                                    // Init normally populates DatabaseStoredProcedure for every SP entity
                                    // (or throws and aborts startup). Reaching here means an init invariant
                                    // regressed. Throw so the surrounding catch drops just this entity from
                                    // the response - returning the SP with no parameter info would mislead
                                    // the agent into thinking the SP takes no arguments.
                                    throw new InvalidOperationException(
                                        $"Could not resolve DB metadata for stored procedure entity '{entityName}'. Error: {resolveError}");
                                }
                            }
                            else if (!nameOnly)
                            {
                                if (McpMetadataHelper.TryResolveMetadata(
                                        entityName,
                                        runtimeConfig,
                                        serviceProvider,
                                        out ISqlMetadataProvider resolvedMetadataProvider,
                                        out DatabaseObject resolvedDbObject,
                                        out _,
                                        out string resolveError,
                                        cancellationToken))
                                {
                                    sqlMetadataProvider = resolvedMetadataProvider;
                                    databaseObject = resolvedDbObject;
                                }
                                else
                                {
                                    logger?.LogDebug(
                                        "Could not resolve DB metadata for entity '{EntityName}'; falling back to config-declared fields. Details: {ResolveError}",
                                        entityName,
                                        resolveError);
                                }
                            }

                            Dictionary<string, object?> entityInfo = nameOnly
                                ? BuildBasicEntityInfo(entityName, entity)
                                : BuildFullEntityInfo(entityName, entity, currentUserRole, sqlMetadataProvider, databaseObject, authResolver);

                            entityList.Add(entityInfo);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            logger?.LogWarning(ex, "Failed to build info for entity '{EntityName}'", entityName);
                        }
                    }
                }

                if (entityList.Count == 0)
                {
                    // No entities matched the filter criteria
                    if (entityFilter != null && entityFilter.Count > 0)
                    {
                        return Task.FromResult(McpResponseBuilder.BuildErrorResult(
                            toolName,
                            "EntitiesNotFound",
                            $"No entities found matching the filter: {string.Join(", ", entityFilter)}",
                            logger));
                    }
                    // Return a specific error when ALL configured entities have dml-tools: false.
                    // Only show this error when every entity was intentionally filtered by the dml-tools check above,
                    // not when some entities failed to build due to exceptions in BuildBasicEntityInfo() or BuildFullEntityInfo() functions.
                    else if (filteredDmlDisabledCount > 0 &&
                             runtimeConfig.Entities != null &&
                             filteredDmlDisabledCount == runtimeConfig.Entities.Entities.Count)
                    {
                        return Task.FromResult(McpResponseBuilder.BuildErrorResult(
                            toolName,
                            "AllEntitiesFilteredDmlDisabled",
                            $"All {filteredDmlDisabledCount} configured entities have DML tools disabled (dml-tools: false). Entities with dml-tools disabled do not appear in describe_entities. If the filtered entities are stored procedures with custom-tool enabled, check tools/list.",
                            logger));
                    }
                    // Truly no entities configured in the runtime config, or entities failed to build for other reasons
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

                // Log when entities were filtered due to DML tools disabled for visibility
                if (filteredDmlDisabledCount > 0)
                {
                    logger?.LogInformation(
                        "DescribeEntitiesTool: {FilteredCount} entity(ies) filtered with DML tools disabled (dml-tools: false). " +
                        "These entities are not exposed via DML tools and do not appear in describe_entities response. " +
                        "Returned {ReturnedCount} entities.",
                        filteredDmlDisabledCount,
                        finalEntityList.Count);
                }

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
        /// Determines whether the specified entity is accessible to the given role, using the
        /// authorization resolver as the source of truth. This respects role inheritance
        /// (anonymous -> authenticated -> named role) and wildcard operation expansion, matching
        /// REST/GraphQL/OpenAPI authorization behavior.
        /// </summary>
        /// <param name="entityName">The name of the entity being checked.</param>
        /// <param name="entity">The entity object (used only to select the valid operation set for its source type).</param>
        /// <param name="role">The role to check permissions for. If null or whitespace, the entity is not accessible.</param>
        /// <param name="authResolver">The authorization resolver.</param>
        /// <returns><see langword="true"/> if any valid operation is authorized on the entity for the role; otherwise, <see langword="false"/>.</returns>
        private static bool HasAnyPermissionForEntity(string entityName, Entity entity, string? role, IAuthorizationResolver authResolver)
        {
            if (string.IsNullOrWhiteSpace(role))
            {
                return false;
            }

            HashSet<EntityActionOperation> validOperations = entity.Source.Type == EntitySourceType.StoredProcedure
                ? EntityAction.ValidStoredProcedurePermissionOperations
                : EntityAction.ValidPermissionOperations;

            foreach (EntityActionOperation operation in validOperations)
            {
                if (authResolver.AreRoleAndOperationDefinedForEntity(entityName, role, operation))
                {
                    return true;
                }
            }

            return false;
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
        /// <param name="currentUserRole">The role of the current user, used to determine permissions and visible fields.</param>
        /// <param name="sqlMetadataProvider">The resolved metadata provider if available, used for authoritative exposed column name resolution.</param>
        /// <param name="databaseObject">The resolved database object metadata if available.</param>
        /// <param name="authResolver">The authorization resolver used to compute allowed exposed columns.</param>
        /// <returns>
        /// A dictionary containing the entity's name, description, fields, parameters (if applicable), and permissions.
        /// </returns>
        private static Dictionary<string, object?> BuildFullEntityInfo(
            string entityName,
            Entity entity,
            string? currentUserRole,
            ISqlMetadataProvider? sqlMetadataProvider,
            DatabaseObject? databaseObject,
            IAuthorizationResolver authResolver)
        {
            // Use GraphQL singular name as alias if available, otherwise use entity name
            string displayName = !string.IsNullOrWhiteSpace(entity.GraphQL?.Singular)
                ? entity.GraphQL.Singular
                : entityName;

            // Column-level authorization: filter fields by the columns the caller's role is allowed
            // to see across every valid operation on this entity. Without this filter, describe_entities
            // would leak the names and descriptions of columns restricted by fields.include /
            // fields.exclude, extending the MSRC info-disclosure (CWE-285 -> CWE-200) from the entity
            // level down to the column level.
            HashSet<string>? allowedFieldNames = ComputeAllowedFieldNames(
                entityName, entity, currentUserRole, authResolver);

            Dictionary<string, object?> info = new()
            {
                ["name"] = displayName,
                ["description"] = entity.Description ?? string.Empty,
                ["fields"] = BuildFieldMetadataInfo(entityName, entity, sqlMetadataProvider, databaseObject, allowedFieldNames),
            };

            if (entity.Source.Type == EntitySourceType.StoredProcedure)
            {
                info["parameters"] = BuildParameterMetadataInfo(databaseObject);
            }

            info["permissions"] = BuildPermissionsInfo(entityName, entity, currentUserRole, authResolver);

            return info;
        }

        /// <summary>
        /// Builds field metadata for an entity.
        /// For tables and views, merges DB columns (<see cref="SourceDefinition.Columns"/> and
        /// <see cref="SourceDefinition.PrimaryKey"/>) with config overrides (<see cref="Entity.Fields"/>
        /// and <see cref="Entity.Mappings"/>), filtered by <paramref name="allowedFieldNames"/>.
        /// Falls back to config-declared fields when DB metadata is unavailable.
        /// For stored procedures, projects config-declared result fields without column-level filtering.
        /// </summary>
        private static List<object> BuildFieldMetadataInfo(
            string entityName,
            Entity entity,
            ISqlMetadataProvider? sqlMetadataProvider,
            DatabaseObject? databaseObject,
            HashSet<string>? allowedFieldNames)
        {
            if (entity.Source.Type == EntitySourceType.StoredProcedure)
            {
                return BuildStoredProcedureFieldMetadataInfo(entity.Fields);
            }

            List<object> result = new();
            SourceDefinition? sourceDefinition = TryGetTableOrViewSourceDefinition(databaseObject);
            bool hasDbColumns = sourceDefinition?.Columns is { Count: > 0 };
            HashSet<string> primaryKeys = ResolveEffectivePrimaryKeyColumns(
                entity,
                hasDbColumns ? sourceDefinition : null);
            Dictionary<string, string>? mappingsByColumn = BuildMappingsLookup(entity.Mappings);

            if (hasDbColumns)
            {
                foreach ((string columnName, ColumnDefinition columnDefinition) in sourceDefinition!.Columns)
                {
                    FieldMetadata? fieldMetadata = FindConfigFieldMetadata(entity.Fields, columnName);
                    string exposedName = ResolveExposedFieldName(
                        entityName,
                        columnName,
                        fieldMetadata,
                        mappingsByColumn,
                        sqlMetadataProvider);

                    if (allowedFieldNames != null && !allowedFieldNames.Contains(exposedName))
                    {
                        continue;
                    }

                    result.Add(BuildTableOrViewFieldEntry(
                        name: exposedName,
                        description: fieldMetadata?.Description ?? string.Empty,
                        primaryKey: primaryKeys.Contains(columnName),
                        dataType: MapColumnDataType(columnDefinition)));
                }

                return result;
            }

            // Degraded fallback when DB metadata is unavailable (e.g., non-SQL provider or unpopulated metadata).
            if (entity.Fields == null)
            {
                return result;
            }

            foreach (FieldMetadata field in entity.Fields)
            {
                string exposedName = ResolveExposedFieldNameFromConfig(field.Name, field, mappingsByColumn);

                if (allowedFieldNames != null && !allowedFieldNames.Contains(exposedName))
                {
                    continue;
                }

                result.Add(BuildTableOrViewFieldEntry(
                    name: exposedName,
                    description: field.Description ?? string.Empty,
                    primaryKey: primaryKeys.Contains(field.Name),
                    dataType: null));
            }

            return result;
        }

        private static List<object> BuildStoredProcedureFieldMetadataInfo(List<FieldMetadata>? fields)
        {
            List<object> result = new();

            if (fields == null)
            {
                return result;
            }

            foreach (FieldMetadata field in fields)
            {
                result.Add(new
                {
                    name = field.Alias ?? field.Name,
                    description = field.Description ?? string.Empty
                });
            }

            return result;
        }

        private static Dictionary<string, object?> BuildTableOrViewFieldEntry(
            string name,
            string description,
            bool primaryKey,
            string? dataType) => new()
            {
                ["name"] = name,
                ["description"] = description,
                ["primaryKey"] = primaryKey,
                ["dataType"] = dataType
            };

        private static SourceDefinition? TryGetTableOrViewSourceDefinition(DatabaseObject? databaseObject) =>
            databaseObject switch
            {
                DatabaseTable table => table.TableDefinition,
                DatabaseView view => view.ViewDefinition,
                _ => null
            };

        /// <summary>
        /// Resolves primary key backing column names.
        /// When DB metadata is available, <see cref="SourceDefinition.PrimaryKey"/> is already populated
        /// upstream by <see cref="Core.Services.MetadataProviders.SqlMetadataProvider"/> (applying
        /// <c>fields[].primary-key</c> -> <c>source.key-fields</c> -> DB PK) and is the exact key set
        /// enforced by <see cref="RequestValidator.ValidatePrimaryKey"/> in <c>update_record</c> and
        /// <c>delete_record</c>. Config fallback is used only when DB metadata is unavailable.
        /// </summary>
        private static HashSet<string> ResolveEffectivePrimaryKeyColumns(Entity entity, SourceDefinition? sourceDefinition)
        {
            HashSet<string> primaryKeys = new(StringComparer.OrdinalIgnoreCase);

            if (sourceDefinition != null)
            {
                if (sourceDefinition.PrimaryKey is { Count: > 0 })
                {
                    foreach (string pk in sourceDefinition.PrimaryKey)
                    {
                        if (!string.IsNullOrWhiteSpace(pk))
                        {
                            primaryKeys.Add(pk);
                        }
                    }
                }

                return primaryKeys;
            }

            if (entity.Fields != null)
            {
                foreach (FieldMetadata field in entity.Fields)
                {
                    if (field.PrimaryKey && !string.IsNullOrWhiteSpace(field.Name))
                    {
                        primaryKeys.Add(field.Name);
                    }
                }

                if (primaryKeys.Count > 0)
                {
                    return primaryKeys;
                }
            }

            if (entity.Source.KeyFields is { Length: > 0 })
            {
                foreach (string keyField in entity.Source.KeyFields)
                {
                    if (!string.IsNullOrWhiteSpace(keyField))
                    {
                        primaryKeys.Add(keyField);
                    }
                }
            }

            return primaryKeys;
        }

        /// <summary>
        /// Finds config <see cref="FieldMetadata"/> for a DB column, preferring an exact case-sensitive
        /// (<see cref="StringComparison.Ordinal"/>) match before falling back to case-insensitive matching.
        /// </summary>
        private static FieldMetadata? FindConfigFieldMetadata(List<FieldMetadata>? fields, string columnName)
        {
            if (fields is not { Count: > 0 })
            {
                return null;
            }

            FieldMetadata? caseInsensitiveMatch = null;
            foreach (FieldMetadata field in fields)
            {
                if (string.IsNullOrWhiteSpace(field.Name))
                {
                    continue;
                }

                if (string.Equals(field.Name, columnName, StringComparison.Ordinal))
                {
                    return field;
                }

                if (caseInsensitiveMatch is null &&
                    string.Equals(field.Name, columnName, StringComparison.OrdinalIgnoreCase))
                {
                    caseInsensitiveMatch = field;
                }
            }

            return caseInsensitiveMatch;
        }

        private static Dictionary<string, string>? BuildMappingsLookup(Dictionary<string, string>? mappings)
        {
            if (mappings is not { Count: > 0 })
            {
                return null;
            }

            Dictionary<string, string> lookup = new(StringComparer.OrdinalIgnoreCase);
            foreach ((string backingColumn, string alias) in mappings)
            {
                if (!string.IsNullOrWhiteSpace(backingColumn) && !string.IsNullOrWhiteSpace(alias))
                {
                    lookup.TryAdd(backingColumn, alias);
                }
            }

            return lookup;
        }

        /// <summary>
        /// Resolves the exposed name for a backing column, preferring <see cref="ISqlMetadataProvider.TryGetExposedColumnName"/>
        /// so advertised names match <see cref="AuthorizationResolver"/> and REST/GraphQL projections,
        /// and falling back to config rules when the provider mapping is unavailable (e.g., unit-test stubs).
        /// </summary>
        private static string ResolveExposedFieldName(
            string entityName,
            string backingColumnName,
            FieldMetadata? fieldMetadata,
            Dictionary<string, string>? mappingsByColumn,
            ISqlMetadataProvider? sqlMetadataProvider)
        {
            if (sqlMetadataProvider != null)
            {
                try
                {
                    if (sqlMetadataProvider.TryGetExposedColumnName(entityName, backingColumnName, out string? providerExposedName) &&
                        !string.IsNullOrWhiteSpace(providerExposedName))
                    {
                        return providerExposedName;
                    }
                }
                catch (KeyNotFoundException)
                {
                    // Fall back to config-derived mapping if provider column maps were not initialized.
                }
            }

            return ResolveExposedFieldNameFromConfig(backingColumnName, fieldMetadata, mappingsByColumn);
        }

        /// <summary>
        /// Mirrors <c>SqlMetadataProvider.GenerateExposedToBackingColumnMapUtil</c>:
        /// any entry in <c>entity.Fields</c> claims the column (using <c>Alias</c> if non-whitespace,
        /// else <c>backingColumnName</c>), and <c>entity.Mappings</c> applies only to columns not
        /// already claimed by <c>entity.Fields</c>.
        /// </summary>
        private static string ResolveExposedFieldNameFromConfig(
            string backingColumnName,
            FieldMetadata? fieldMetadata,
            Dictionary<string, string>? mappingsByColumn)
        {
            if (fieldMetadata != null)
            {
                return !string.IsNullOrWhiteSpace(fieldMetadata.Alias)
                    ? fieldMetadata.Alias
                    : backingColumnName;
            }

            if (mappingsByColumn != null && mappingsByColumn.TryGetValue(backingColumnName, out string? mappedAlias))
            {
                return mappedAlias;
            }

            return backingColumnName;
        }

        private static string? MapColumnDataType(ColumnDefinition column)
        {
            if (column.IsArrayType)
            {
                return "array";
            }

            Type systemType = Nullable.GetUnderlyingType(column.SystemType) ?? column.SystemType;

            if (systemType == typeof(int) || systemType == typeof(long) ||
                systemType == typeof(short) || systemType == typeof(byte) ||
                systemType == typeof(sbyte) || systemType == typeof(uint) ||
                systemType == typeof(ulong) || systemType == typeof(ushort))
            {
                return "integer";
            }

            if (systemType == typeof(float) || systemType == typeof(double) ||
                systemType == typeof(decimal))
            {
                return "number";
            }

            if (systemType == typeof(bool))
            {
                return "boolean";
            }

            if (systemType == typeof(string) || systemType == typeof(char) ||
                systemType == typeof(Guid) || systemType == typeof(DateTime) ||
                systemType == typeof(DateTimeOffset) || systemType == typeof(DateOnly) ||
                systemType == typeof(TimeOnly) || systemType == typeof(TimeSpan) ||
                systemType == typeof(byte[]))
            {
                return "string";
            }

            return column.DbType switch
            {
                DbType.Byte or DbType.SByte or DbType.Int16 or DbType.UInt16
                    or DbType.Int32 or DbType.UInt32 or DbType.Int64 or DbType.UInt64 => "integer",
                DbType.Single or DbType.Double or DbType.Decimal or DbType.Currency
                    or DbType.VarNumeric => "number",
                DbType.Boolean => "boolean",
                DbType.AnsiString or DbType.AnsiStringFixedLength or DbType.String
                    or DbType.StringFixedLength or DbType.Xml or DbType.Guid
                    or DbType.Date or DbType.DateTime or DbType.DateTime2
                    or DbType.DateTimeOffset or DbType.Time or DbType.Binary => "string",
                _ => null
            };
        }

        /// <summary>
        /// Returns the set of exposed field names (aliased where applicable) the caller is
        /// allowed to see on the given entity, computed across every valid operation the caller's
        /// role is authorized for.
        /// </summary>
        /// <remarks>
        /// Uses <see cref="IAuthorizationResolver.GetAllowedExposedColumns"/>, the same source
        /// of truth REST uses when materializing a response's column projection. Stored procedures
        /// return null because SP result-set columns are not governed by fields.include/exclude
        /// (SP permissions are Execute-only); returning null signals "no filter" to the projection.
        /// </remarks>
        /// <returns>
        /// Null for stored procedures (do not filter). An empty set when the caller has no role,
        /// which produces an empty fields[] projection while leaving the entity entry intact
        /// (entity-level authorization has already passed by the time this runs).
        /// </returns>
        private static HashSet<string>? ComputeAllowedFieldNames(
            string entityName,
            Entity entity,
            string? currentUserRole,
            IAuthorizationResolver authResolver)
        {
            if (entity.Source.Type == EntitySourceType.StoredProcedure)
            {
                return null;
            }

            HashSet<string> allowed = new(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(currentUserRole))
            {
                return allowed;
            }

            foreach (EntityActionOperation operation in EntityAction.ValidPermissionOperations)
            {
                if (!authResolver.AreRoleAndOperationDefinedForEntity(entityName, currentUserRole, operation))
                {
                    continue;
                }

                foreach (string column in authResolver.GetAllowedExposedColumns(entityName, currentUserRole, operation))
                {
                    allowed.Add(column);
                }
            }

            return allowed;
        }

        /// <summary>
        /// Builds the parameter list for a stored procedure entity.
        /// Each entry has: name, required, default, description.
        ///
        /// The per-field rules are agreed in issue #3400:
        ///   name        - DB metadata is the source of truth; config cannot override.
        ///   required    - defaults to true when not set in config.
        ///                 (SQL Server's is_nullable describes the type, not whether the
        ///                  parameter must be supplied at call time, so it is unreliable.)
        ///   default     - config-only. T-SQL parameter defaults are not exposed as
        ///                 structured metadata, so they cannot be discovered from the DB.
        ///   description - config-only. SQL Server has no description column for parameters.
        ///
        /// The merge of config onto DB metadata is already performed upstream by
        /// <see cref="Core.Services.MetadataProviders.SqlMetadataProvider"/> /
        /// <see cref="Core.Services.MetadataProviders.MsSqlMetadataProvider"/> when populating
        /// <see cref="DatabaseStoredProcedure"/>. Each <see cref="ParameterDefinition"/> therefore
        /// already reflects the config overlay; we just project it.
        ///
        /// For an SP entity that successfully initialized, the metadata provider always has a
        /// populated <see cref="DatabaseStoredProcedure"/>: init throws otherwise (e.g.
        /// SqlMetadataProvider.FillSchemaForStoredProcedureAsync raises via HandleOrRecordException
        /// when config declares a parameter the DB doesn't have, and startup aborts). If this
        /// invariant ever regresses we throw rather than fabricate empty parameter info, so the
        /// surrounding per-entity catch drops just this entity from the response.
        /// </summary>
        /// <param name="databaseObject">DB metadata for the entity. Must be a populated <see cref="DatabaseStoredProcedure"/>.</param>
        /// <returns>A list whose elements are dictionaries (one per parameter), each with the keys
        /// <c>name</c>, <c>required</c>, <c>default</c>, and <c>description</c>.</returns>
        /// <exception cref="InvalidOperationException">Thrown when <paramref name="databaseObject"/> is not a <see cref="DatabaseStoredProcedure"/> with a populated <see cref="StoredProcedureDefinition"/>.</exception>
        private static List<object> BuildParameterMetadataInfo(DatabaseObject? databaseObject)
        {
            IReadOnlyDictionary<string, ParameterDefinition>? dbParameters =
                (databaseObject as DatabaseStoredProcedure)?.StoredProcedureDefinition?.Parameters
                ?? throw new InvalidOperationException(
                    "Stored-procedure metadata is missing at describe_entities time. " +
                    "SqlMetadataProvider.FillSchemaForStoredProcedureAsync should have populated this during init.");

            List<object> result = new(dbParameters.Count);
            foreach ((string parameterName, ParameterDefinition definition) in dbParameters)
            {
                result.Add(BuildParameterEntry(parameterName, definition));
            }

            return result;
        }

        private static Dictionary<string, object?> BuildParameterEntry(
            string name,
            ParameterDefinition definition) => new()
            {
                ["name"] = name,
                ["required"] = definition.Required ?? true,
                ["default"] = definition.Default,
                ["description"] = definition.Description ?? string.Empty
            };

        /// <summary>
        /// Builds the sorted list of operation permissions the caller has on the given entity,
        /// using the authorization resolver as the source of truth so role inheritance
        /// (anonymous -> authenticated -> named role) and wildcard operation expansion are applied
        /// consistently with REST/GraphQL/OpenAPI.
        /// </summary>
        /// <param name="entityName">The name of the entity being described.</param>
        /// <param name="entity">The entity object (used only to select the valid operation set for its source type).</param>
        /// <param name="currentUserRole">The current user's role - if null or whitespace, returns empty permissions.</param>
        /// <param name="authResolver">The authorization resolver.</param>
        /// <returns>A sorted list of operation names (uppercased) authorized on the entity for the caller's role.</returns>
        private static string[] BuildPermissionsInfo(string entityName, Entity entity, string? currentUserRole, IAuthorizationResolver authResolver)
        {
            if (string.IsNullOrWhiteSpace(currentUserRole))
            {
                return Array.Empty<string>();
            }

            HashSet<EntityActionOperation> validOperations = entity.Source.Type == EntitySourceType.StoredProcedure
                ? EntityAction.ValidStoredProcedurePermissionOperations
                : EntityAction.ValidPermissionOperations;

            HashSet<string> permissions = new(StringComparer.OrdinalIgnoreCase);

            foreach (EntityActionOperation operation in validOperations)
            {
                if (authResolver.AreRoleAndOperationDefinedForEntity(entityName, currentUserRole, operation))
                {
                    permissions.Add(operation.ToString().ToUpperInvariant());
                }
            }

            return permissions.OrderBy(p => p, StringComparer.Ordinal).ToArray();
        }
    }
}
