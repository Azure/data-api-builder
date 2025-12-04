// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data.Common;
using System.Text.Json;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
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

namespace Azure.DataApiBuilder.Mcp.BuiltInTools
{
    /// <summary>
    /// Tool to execute a stored procedure or function exposed as a DAB entity.
    /// Behaves most like the GraphQL flow with entity permissions enforced.
    /// </summary>
    public class ExecuteEntityTool : IMcpTool
    {
        /// <summary>
        /// Gets the type of the tool, which is BuiltIn for this implementation.
        /// </summary>
        public ToolType ToolType { get; } = ToolType.BuiltIn;

        /// <summary>
        /// Gets the metadata for the execute-entity tool, including its name, description, and input schema.
        /// </summary>
        public Tool GetToolMetadata()
        {
            return new Tool
            {
                Name = "execute_entity",
                Description = "STEP 1: describe_entities -> find entities with EXECUTE permission and their parameters. STEP 2: call this tool with matching parameter values. Used for entities that perform actions or return computed results.",
                InputSchema = JsonSerializer.Deserialize<JsonElement>(
                    @"{
                        ""type"": ""object"",
                        ""properties"": {
                            ""entity"": {
                                ""type"": ""string"",
                                ""description"": ""Entity name with EXECUTE permission.""
                            },
                            ""parameters"": {
                                ""type"": ""object"",
                                ""description"": ""Optional parameter names and values.""
                            }
                        },
                        ""required"": [""entity""]
                    }"
                )
            };
        }

        /// <summary>
        /// Executes a stored procedure or function, returns the results (if any).
        /// </summary>
        public async Task<CallToolResult> ExecuteAsync(
            JsonDocument? arguments,
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken = default)
        {
            ILogger<ExecuteEntityTool>? logger = serviceProvider.GetService<ILogger<ExecuteEntityTool>>();
            string toolName = GetToolMetadata().Name;

            try
            {
                // Cancellation check at the start
                cancellationToken.ThrowIfCancellationRequested();

                // 1) Resolve required services & configuration
                RuntimeConfigProvider runtimeConfigProvider = serviceProvider.GetRequiredService<RuntimeConfigProvider>();
                RuntimeConfig config = runtimeConfigProvider.GetConfig();

                // 2) Check if the tool is enabled in configuration before proceeding
                if (config.McpDmlTools?.ExecuteEntity != true)
                {
                    return McpErrorHelpers.ToolDisabled(this.GetToolMetadata().Name, logger);
                }

                // 3) Parsing & basic argument validation
                if (arguments is null)
                {
                    return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments", "No arguments provided.", logger);
                }

                if (!McpArgumentParser.TryParseExecuteArguments(arguments.RootElement, out string entity, out Dictionary<string, object?> parameters, out string parseError))
                {
                    return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments", parseError, logger);
                }

                // Entity is required
                if (string.IsNullOrWhiteSpace(entity))
                {
                    return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments", "Entity is required", logger);
                }

                IMetadataProviderFactory metadataProviderFactory = serviceProvider.GetRequiredService<IMetadataProviderFactory>();
                IQueryEngineFactory queryEngineFactory = serviceProvider.GetRequiredService<IQueryEngineFactory>();

                // 4) Validate entity exists and is a stored procedure
                if (!config.Entities.TryGetValue(entity, out Entity? entityConfig))
                {
                    return McpResponseBuilder.BuildErrorResult(toolName, "EntityNotFound", $"Entity '{entity}' not found in configuration.", logger);
                }

                if (entityConfig.Source.Type != EntitySourceType.StoredProcedure)
                {
                    return McpResponseBuilder.BuildErrorResult(toolName, "InvalidEntity", $"Entity {entity} cannot be executed.", logger);
                }

                // Use shared metadata helper.
                if (!McpMetadataHelper.TryResolveMetadata(
                        entity,
                        config,
                        serviceProvider,
                        out ISqlMetadataProvider sqlMetadataProvider,
                        out DatabaseObject dbObject,
                        out string dataSourceName,
                        out string metadataError))
                {
                    return McpResponseBuilder.BuildErrorResult(toolName, "EntityNotFound", metadataError, logger);
                }

                // 6) Authorization - Never bypass permissions
                IAuthorizationResolver authResolver = serviceProvider.GetRequiredService<IAuthorizationResolver>();
                IHttpContextAccessor httpContextAccessor = serviceProvider.GetRequiredService<IHttpContextAccessor>();
                HttpContext? httpContext = httpContextAccessor.HttpContext;

                if (!McpAuthorizationHelper.ValidateRoleContext(httpContext, authResolver, out string roleError))
                {
                    return McpErrorHelpers.PermissionDenied(toolName, entity, "execute", roleError, logger);
                }

                if (!McpAuthorizationHelper.TryResolveAuthorizedRole(
                    httpContext!,
                    authResolver,
                    entity,
                    EntityActionOperation.Execute,
                    out string? effectiveRole,
                    out string authError))
                {
                    return McpErrorHelpers.PermissionDenied(toolName, entity, "execute", authError, logger);
                }

                // 7) Validate parameters against metadata
                if (parameters != null && entityConfig.Source.Parameters != null)
                {
                    // Validate all provided parameters exist in metadata
                    foreach (KeyValuePair<string, object?> param in parameters)
                    {
                        if (!entityConfig.Source.Parameters.Any(p => p.Name == param.Key))
                        {
                            return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments", $"Invalid parameter: {param.Key}", logger);
                        }
                    }
                }

                // 8) Build request payload
                JsonElement? requestPayloadRoot = null;

                if (parameters?.Count > 0)
                {
                    string jsonPayload = JsonSerializer.Serialize(parameters);
                    using JsonDocument doc = JsonDocument.Parse(jsonPayload);
                    requestPayloadRoot = doc.RootElement.Clone();
                }

                // 9) Build stored procedure execution context
                StoredProcedureRequestContext context = new(
                    entityName: entity,
                    dbo: dbObject,
                    requestPayloadRoot: requestPayloadRoot,
                    operationType: EntityActionOperation.Execute);

                // First, add user-provided parameters to the context
                if (requestPayloadRoot != null)
                {
                    foreach (JsonProperty property in requestPayloadRoot.Value.EnumerateObject())
                    {
                        context.FieldValuePairsInBody[property.Name] = GetParameterValue(property.Value);
                    }
                }

                // Then, add default parameters from configuration (only if not already provided by user)
                if ((parameters == null || parameters.Count == 0) && entityConfig.Source.Parameters != null)
                {
                    foreach (ParameterMetadata param in entityConfig.Source.Parameters)
                    {
                        if (!context.FieldValuePairsInBody.ContainsKey(param.Name))
                        {
                            context.FieldValuePairsInBody[param.Name] = param.Default;
                        }
                    }
                }

                // Populate resolved parameters for stored procedure execution
                context.PopulateResolvedParameters();

                // 10) Execute stored procedure
                DatabaseType dbType = config.GetDataSourceFromDataSourceName(dataSourceName).DatabaseType;
                IQueryEngine queryEngine = queryEngineFactory.GetQueryEngine(dbType);

                IActionResult? queryResult = null;

                try
                {
                    // Cancellation check before executing
                    cancellationToken.ThrowIfCancellationRequested();
                    queryResult = await queryEngine.ExecuteAsync(context, dataSourceName).ConfigureAwait(false);
                }
                catch (DataApiBuilderException dabEx)
                {
                    // Handle specific DAB exceptions
                    logger?.LogError(dabEx, "Data API builder error executing stored procedure {StoredProcedure}", entity);

                    string message = dabEx.Message;

                    // Check for specific error patterns
                    if (message.Contains("permission", StringComparison.OrdinalIgnoreCase) ||
                        message.Contains("authorization", StringComparison.OrdinalIgnoreCase))
                    {
                        return McpResponseBuilder.BuildErrorResult(
                            toolName,
                            "PermissionDenied",
                            "You do not have permission to execute this stored procedure.",
                            logger);
                    }
                    else if (message.Contains("invalid", StringComparison.OrdinalIgnoreCase) &&
                             message.Contains("type", StringComparison.OrdinalIgnoreCase))
                    {
                        return McpResponseBuilder.BuildErrorResult(
                            toolName,
                            "InvalidArguments",
                            "Invalid data type for one or more parameters.",
                            logger);
                    }

                    // For any other DAB exceptions, return the message as-is
                    return McpResponseBuilder.BuildErrorResult(
                        toolName,
                        "DataApiBuilderError",
                        dabEx.Message,
                        logger);
                }
                catch (SqlException sqlEx)
                {
                    // Handle SQL Server specific errors
                    logger?.LogError(sqlEx, "SQL Server error executing stored procedure {StoredProcedure}", entity);
                    string errorMessage = sqlEx.Number switch
                    {
                        2812 => $"Stored procedure '{entityConfig.Source.Object}' not found in the database.",
                        8144 => $"Stored procedure '{entityConfig.Source.Object}' has too many parameters specified.",
                        201 => $"Stored procedure '{entityConfig.Source.Object}' expects parameter(s) that were not supplied.",
                        245 => "Type conversion failed when processing parameters.",
                        229 or 262 => $"Permission denied to execute stored procedure '{entityConfig.Source.Object}'.",
                        _ => $"Database error: {sqlEx.Message}"
                    };
                    return McpResponseBuilder.BuildErrorResult(toolName, "DatabaseError", errorMessage, logger);
                }
                catch (DbException dbEx)
                {
                    // Handle generic database exceptions (works for PostgreSQL, MySQL, etc.)
                    logger?.LogError(dbEx, "Database error executing stored procedure {StoredProcedure}", entity);
                    return McpResponseBuilder.BuildErrorResult(toolName, "DatabaseError", $"Database error: {dbEx.Message}", logger);
                }
                catch (InvalidOperationException ioEx) when (ioEx.Message.Contains("connection", StringComparison.OrdinalIgnoreCase))
                {
                    // Handle connection-related issues
                    logger?.LogError(ioEx, "Database connection error");
                    return McpResponseBuilder.BuildErrorResult(toolName, "ConnectionError", "Failed to connect to the database.", logger);
                }
                catch (TimeoutException timeoutEx)
                {
                    // Handle query timeout
                    logger?.LogError(timeoutEx, "Stored procedure execution timeout for {StoredProcedure}", entity);
                    return McpResponseBuilder.BuildErrorResult(toolName, "TimeoutError", "The stored procedure execution timed out.", logger);
                }
                catch (Exception ex)
                {
                    // Generic database/execution errors
                    logger?.LogError(ex, "Unexpected error executing stored procedure {StoredProcedure}", entity);
                    return McpResponseBuilder.BuildErrorResult(toolName, "DatabaseError", "An error occurred while executing the stored procedure.", logger);
                }

                // 11) Build response with execution result
                return BuildExecuteSuccessResponse(toolName, entity, parameters, queryResult, logger);
            }
            catch (OperationCanceledException)
            {
                return McpResponseBuilder.BuildErrorResult(toolName, "OperationCanceled", "The execute operation was canceled.", logger);
            }
            catch (ArgumentException argEx)
            {
                return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments", argEx.Message, logger);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Unexpected error in ExecuteEntityTool.");
                return McpResponseBuilder.BuildErrorResult(
                    toolName,
                    "UnexpectedError",
                    "An unexpected error occurred during the execute operation.",
                    logger);
            }
        }

        /// <summary>
        /// Converts a JSON element to its appropriate CLR type matching GraphQL data types.
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
                $"ExecuteEntityTool success for entity {entityName}."
            );
        }
    }
}
