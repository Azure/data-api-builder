// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data.Common;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Mcp.Model;
using Azure.DataApiBuilder.Mcp.Utils;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using static Azure.DataApiBuilder.Mcp.Model.McpEnums;

namespace Azure.DataApiBuilder.Mcp.Core
{
    /// <summary>
    /// Dynamic custom MCP tool generated from stored procedure entity configuration.
    /// Each custom tool represents a single stored procedure exposed as a dedicated MCP tool.
    /// 
    /// Note: The entity configuration is captured at tool construction time. If the RuntimeConfig
    /// is hot-reloaded, GetToolMetadata() will return cached metadata (name, description, parameters)
    /// from the original configuration. This is acceptable because:
    /// 1. MCP clients typically call tools/list once at startup
    /// 2. ExecuteAsync always validates against the current runtime configuration
    /// 3. Cached metadata improves performance for repeated metadata requests
    /// </summary>
    public class DynamicCustomTool : IMcpTool
    {
        private readonly Entity _entity;

        /// <summary>
        /// Initializes a new instance of DynamicCustomTool.
        /// </summary>
        /// <param name="entityName">The entity name from configuration.</param>
        /// <param name="entity">The entity configuration object.</param>
        public DynamicCustomTool(string entityName, Entity entity)
        {
            EntityName = entityName ?? throw new ArgumentNullException(nameof(entityName));
            _entity = entity ?? throw new ArgumentNullException(nameof(entity));

            // Validate that this is a stored procedure
            if (_entity.Source.Type != EntitySourceType.StoredProcedure)
            {
                throw new ArgumentException(
                    $"Custom tools can only be created for stored procedures. Entity '{entityName}' is of type '{_entity.Source.Type}'.",
                    nameof(entity));
            }
        }

        /// <summary>
        /// Gets the type of the tool, which is Custom for dynamically generated tools.
        /// </summary>
        public ToolType ToolType { get; } = ToolType.Custom;

        /// <summary>
        /// Gets the entity name associated with this custom tool.
        /// </summary>
        public string EntityName { get; }

        /// <summary>
        /// Gets the metadata for this custom tool, including name, description, and input schema.
        /// </summary>
        public Tool GetToolMetadata()
        {
            string toolName = ConvertToToolName(EntityName);
            string description = _entity.Description ?? $"Executes the {toolName} stored procedure";

            // Build input schema based on parameters
            JsonElement inputSchema = BuildInputSchema();

            return new Tool
            {
                Name = toolName,
                Description = description,
                InputSchema = inputSchema
            };
        }

        /// <summary>
        /// Executes the stored procedure represented by this custom tool.
        /// </summary>
        public async Task<CallToolResult> ExecuteAsync(
            JsonDocument? arguments,
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken = default)
        {
            ILogger<DynamicCustomTool>? logger = serviceProvider.GetService<ILogger<DynamicCustomTool>>();
            string toolName = GetToolMetadata().Name;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 1) Resolve required services & configuration
                RuntimeConfigProvider runtimeConfigProvider = serviceProvider.GetRequiredService<RuntimeConfigProvider>();
                RuntimeConfig config = runtimeConfigProvider.GetConfig();

                // 2) Parse arguments from the request
                Dictionary<string, object?> parameters = new();
                if (arguments != null)
                {
                    foreach (JsonProperty property in arguments.RootElement.EnumerateObject())
                    {
                        parameters[property.Name] = GetParameterValue(property.Value);
                    }
                }

                // 3) Validate entity still exists in configuration
                if (!config.Entities.TryGetValue(EntityName, out Entity? entityConfig))
                {
                    return McpResponseBuilder.BuildErrorResult(toolName, "EntityNotFound", $"Entity '{EntityName}' not found in configuration.", logger);
                }

                if (entityConfig.Source.Type != EntitySourceType.StoredProcedure)
                {
                    return McpResponseBuilder.BuildErrorResult(toolName, "InvalidEntity", $"Entity {EntityName} is not a stored procedure.", logger);
                }

                // Check if custom tool is still enabled for this entity
                if (entityConfig.Mcp?.CustomToolEnabled != true)
                {
                    return McpErrorHelpers.ToolDisabled(toolName, logger, $"Custom tool is disabled for entity '{EntityName}'.");
                }

                // 4) Resolve metadata
                if (!McpMetadataHelper.TryResolveMetadata(
                        EntityName,
                        config,
                        serviceProvider,
                        out ISqlMetadataProvider sqlMetadataProvider,
                        out DatabaseObject dbObject,
                        out string dataSourceName,
                        out string metadataError))
                {
                    return McpResponseBuilder.BuildErrorResult(toolName, "EntityNotFound", metadataError, logger);
                }

                // 5) Authorization check
                IAuthorizationResolver authResolver = serviceProvider.GetRequiredService<IAuthorizationResolver>();
                IHttpContextAccessor httpContextAccessor = serviceProvider.GetRequiredService<IHttpContextAccessor>();
                HttpContext? httpContext = httpContextAccessor.HttpContext;

                if (!McpAuthorizationHelper.ValidateRoleContext(httpContext, authResolver, out string roleError))
                {
                    return McpErrorHelpers.PermissionDenied(toolName, EntityName, "execute", roleError, logger);
                }

                if (!McpAuthorizationHelper.TryResolveAuthorizedRole(
                    httpContext!,
                    authResolver,
                    EntityName,
                    EntityActionOperation.Execute,
                    out string? effectiveRole,
                    out string authError))
                {
                    return McpErrorHelpers.PermissionDenied(toolName, EntityName, "execute", authError, logger);
                }

                // 6) Build request payload
                JsonElement? requestPayloadRoot = null;
                if (parameters.Count > 0)
                {
                    string jsonPayload = JsonSerializer.Serialize(parameters);
                    using JsonDocument doc = JsonDocument.Parse(jsonPayload);
                    requestPayloadRoot = doc.RootElement.Clone();
                }

                // 7) Build stored procedure execution context
                StoredProcedureRequestContext context = new(
                    entityName: EntityName,
                    dbo: dbObject,
                    requestPayloadRoot: requestPayloadRoot,
                    operationType: EntityActionOperation.Execute);

                // Add user-provided parameters
                if (requestPayloadRoot != null)
                {
                    foreach (JsonProperty property in requestPayloadRoot.Value.EnumerateObject())
                    {
                        context.FieldValuePairsInBody[property.Name] = GetParameterValue(property.Value);
                    }
                }

                // Add default parameters from configuration if not provided
                if (entityConfig.Source.Parameters != null)
                {
                    foreach (ParameterMetadata param in entityConfig.Source.Parameters)
                    {
                        if (!context.FieldValuePairsInBody.ContainsKey(param.Name))
                        {
                            context.FieldValuePairsInBody[param.Name] = param.Default;
                        }
                    }
                }

                // Populate resolved parameters
                context.PopulateResolvedParameters();

                // 8) Execute stored procedure
                DatabaseType dbType = config.GetDataSourceFromDataSourceName(dataSourceName).DatabaseType;
                IQueryEngineFactory queryEngineFactory = serviceProvider.GetRequiredService<IQueryEngineFactory>();
                IQueryEngine queryEngine = queryEngineFactory.GetQueryEngine(dbType);

                IActionResult? queryResult = null;

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    queryResult = await queryEngine.ExecuteAsync(context, dataSourceName).ConfigureAwait(false);
                }
                catch (DataApiBuilderException dabEx)
                {
                    logger?.LogError(dabEx, "Error executing custom tool {ToolName} for entity {Entity}", toolName, EntityName);
                    return McpResponseBuilder.BuildErrorResult(toolName, "ExecutionError", dabEx.Message, logger);
                }
                catch (SqlException sqlEx)
                {
                    logger?.LogError(sqlEx, "SQL error executing custom tool {ToolName}", toolName);
                    return McpResponseBuilder.BuildErrorResult(toolName, "DatabaseError", $"Database error: {sqlEx.Message}", logger);
                }
                catch (DbException dbEx)
                {
                    logger?.LogError(dbEx, "Database error executing custom tool {ToolName}", toolName);
                    return McpResponseBuilder.BuildErrorResult(toolName, "DatabaseError", $"Database error: {dbEx.Message}", logger);
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Unexpected error executing custom tool {ToolName}", toolName);
                    return McpResponseBuilder.BuildErrorResult(toolName, "UnexpectedError", "An error occurred during execution.", logger);
                }

                // 9) Build success response
                return BuildExecuteSuccessResponse(toolName, EntityName, parameters, queryResult, logger);
            }
            catch (OperationCanceledException)
            {
                return McpResponseBuilder.BuildErrorResult(toolName, "OperationCanceled", "The operation was canceled.", logger);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Unexpected error in DynamicCustomTool for {EntityName}", EntityName);
                return McpResponseBuilder.BuildErrorResult(toolName, "UnexpectedError", "An unexpected error occurred.", logger);
            }
        }

        /// <summary>
        /// Converts entity name to tool name format (lowercase with underscores).
        /// </summary>
        private static string ConvertToToolName(string entityName)
        {
            // Convert PascalCase to snake_case
            string result = Regex.Replace(entityName, "([a-z0-9])([A-Z])", "$1_$2");
            return result.ToLowerInvariant();
        }

        /// <summary>
        /// Builds the input schema for the tool based on entity parameters.
        /// </summary>
        private JsonElement BuildInputSchema()
        {
            Dictionary<string, object> schema = new()
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>()
            };

            if (_entity.Source.Parameters != null && _entity.Source.Parameters.Any())
            {
                Dictionary<string, object> properties = (Dictionary<string, object>)schema["properties"];

                foreach (ParameterMetadata param in _entity.Source.Parameters)
                {
                    // Note: Parameter type information is not available in ParameterMetadata,
                    // so we allow multiple JSON types to match the behavior of GetParameterValue
                    // that handles string, number, boolean, and null values.
                    properties[param.Name] = new Dictionary<string, object>
                    {
                        ["type"] = new[] { "string", "number", "boolean", "null" },
                        ["description"] = param.Description ?? $"Parameter {param.Name}"
                    };
                }
            }

            return JsonSerializer.SerializeToElement(schema);
        }

        /// <summary>
        /// Converts a JSON element to its appropriate CLR type.
        /// </summary>
        private static object? GetParameterValue(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number =>
                    element.TryGetInt64(out long longValue) ? longValue :
                    element.TryGetDecimal(out decimal decimalValue) ? decimalValue :
                    element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => element.ToString()
            };
        }

        /// <summary>
        /// Builds a successful response for the execute operation.
        /// </summary>
        private static CallToolResult BuildExecuteSuccessResponse(
            string toolName,
            string entityName,
            Dictionary<string, object?>? parameters,
            IActionResult? queryResult,
            ILogger? logger)
        {
            Dictionary<string, object?> responseData = new()
            {
                ["entity"] = entityName,
                ["message"] = "Stored procedure executed successfully"
            };

            // Include parameters if any were provided
            if (parameters?.Count > 0)
            {
                responseData["parameters"] = parameters;
            }

            // Handle different result types
            if (queryResult is OkObjectResult okResult && okResult.Value != null)
            {
                // Extract the actual data from the action result
                if (okResult.Value is JsonDocument jsonDoc)
                {
                    JsonElement root = jsonDoc.RootElement;
                    responseData["value"] = root.ValueKind == JsonValueKind.Array ? root : JsonSerializer.SerializeToElement(new[] { root });
                }
                else if (okResult.Value is JsonElement jsonElement)
                {
                    responseData["value"] = jsonElement.ValueKind == JsonValueKind.Array ? jsonElement : JsonSerializer.SerializeToElement(new[] { jsonElement });
                }
                else
                {
                    // Serialize the value directly
                    JsonElement serialized = JsonSerializer.SerializeToElement(okResult.Value);
                    responseData["value"] = serialized;
                }
            }
            else if (queryResult is BadRequestObjectResult badRequest)
            {
                return McpResponseBuilder.BuildErrorResult(
                    toolName,
                    "BadRequest",
                    badRequest.Value?.ToString() ?? "Bad request",
                    logger);
            }
            else if (queryResult is UnauthorizedObjectResult)
            {
                return McpErrorHelpers.PermissionDenied(toolName, entityName, "execute", "You do not have permission to execute this entity", logger);
            }
            else
            {
                // Empty or unknown result
                responseData["value"] = JsonSerializer.SerializeToElement(Array.Empty<object>());
            }

            return McpResponseBuilder.BuildSuccessResult(
                responseData,
                logger,
                $"Custom tool {toolName} executed successfully for entity {entityName}."
            );
        }
    }
}
