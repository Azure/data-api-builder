// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data.Common;
using System.Text.Json;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Mcp.Model;
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
                Name = "execute-entity",
                Description = "Executes a stored procedure or function, returns the results (if any)",
                InputSchema = JsonSerializer.Deserialize<JsonElement>(
                    @"{
                        ""type"": ""object"",
                        ""properties"": {
                            ""entity"": {
                                ""type"": ""string"",
                                ""description"": ""The entity name of the procedure or function to execute. Must match a stored-procedure entity as configured in dab-config. Required.""
                            },
                            ""parameters"": {
                                ""type"": ""object"",
                                ""description"": ""A dictionary of parameter names and values to pass to the procedure. Parameters must match those defined in dab-config. Optional if no parameters.""
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

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 1) Parsing & basic argument validation
                if (arguments is null)
                {
                    return BuildDabResponse(false, null, "No arguments provided.", logger);
                }

                if (!TryParseExecuteArguments(arguments.RootElement, out string entity, out Dictionary<string, object?> parameters, out string parseError))
                {
                    return BuildDabResponse(false, null, parseError, logger);
                }

                // Entity is required
                if (string.IsNullOrWhiteSpace(entity))
                {
                    return BuildDabResponse(false, null, "Entity is required", logger);
                }

                // 2) Resolve required services & configuration
                RuntimeConfigProvider runtimeConfigProvider = serviceProvider.GetRequiredService<RuntimeConfigProvider>();
                RuntimeConfig config = runtimeConfigProvider.GetConfig();

                IMetadataProviderFactory metadataProviderFactory = serviceProvider.GetRequiredService<IMetadataProviderFactory>();
                IQueryEngineFactory queryEngineFactory = serviceProvider.GetRequiredService<IQueryEngineFactory>();

                // 3) Validate entity exists and is a stored procedure
                if (!config.Entities.TryGetValue(entity, out Entity? entityConfig))
                {
                    return BuildDabResponse(false, null, $"Entity '{entity}' not found in configuration.", logger);
                }

                if (entityConfig.Source.Type != EntitySourceType.StoredProcedure)
                {
                    return BuildDabResponse(false, null, $"Entity {entity} cannot be executed.", logger);
                }

                // 4) Resolve metadata
                string dataSourceName;
                ISqlMetadataProvider sqlMetadataProvider;

                try
                {
                    dataSourceName = config.GetDataSourceNameFromEntityName(entity);
                    sqlMetadataProvider = metadataProviderFactory.GetMetadataProvider(dataSourceName);
                }
                catch (Exception)
                {
                    return BuildDabResponse(false, null, $"Entity '{entity}' is not defined in the configuration.", logger);
                }

                if (!sqlMetadataProvider.EntityToDatabaseObject.TryGetValue(entity, out DatabaseObject? dbObject) || dbObject is null)
                {
                    return BuildDabResponse(false, null, $"Entity '{entity}' is not defined in the configuration.", logger);
                }

                // 5) Authorization - Never bypass permissions
                IAuthorizationResolver authResolver = serviceProvider.GetRequiredService<IAuthorizationResolver>();
                IHttpContextAccessor httpContextAccessor = serviceProvider.GetRequiredService<IHttpContextAccessor>();
                HttpContext? httpContext = httpContextAccessor.HttpContext;

                if (httpContext is null || !authResolver.IsValidRoleContext(httpContext))
                {
                    return BuildDabResponse(false, null, "You do not have permission to execute this entity", logger);
                }

                if (!TryResolveAuthorizedRole(
                    httpContext,
                    authResolver,
                    entity,
                    EntityActionOperation.Execute,
                    out string? effectiveRole,
                    out string authError))
                {
                    return BuildDabResponse(false, null, "You do not have permission to execute this entity", logger);
                }

                // 6) Validate parameters against metadata
                if (parameters != null && entityConfig.Source.Parameters != null)
                {
                    // Validate all provided parameters exist in metadata
                    foreach (KeyValuePair<string, object?> param in parameters)
                    {
                        if (!entityConfig.Source.Parameters.ContainsKey(param.Key))
                        {
                            return BuildDabResponse(false, null, $"Invalid parameter: {param.Key}", logger);
                        }
                    }
                }

                // 7) Build request payload
                JsonElement? requestPayloadRoot = null;

                if (parameters?.Count > 0)
                {
                    string jsonPayload = JsonSerializer.Serialize(parameters);
                    using JsonDocument doc = JsonDocument.Parse(jsonPayload);
                    requestPayloadRoot = doc.RootElement.Clone();
                }

                // 8) Build stored procedure execution context
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
                if (entityConfig.Source.Parameters != null)
                {
                    foreach (KeyValuePair<string, object> param in entityConfig.Source.Parameters)
                    {
                        if (!context.FieldValuePairsInBody.ContainsKey(param.Key))
                        {
                            context.FieldValuePairsInBody[param.Key] = param.Value;
                        }
                    }
                }

                // Populate resolved parameters for stored procedure execution
                context.PopulateResolvedParameters();

                // 9) Execute stored procedure
                DatabaseType dbType = config.GetDataSourceFromDataSourceName(dataSourceName).DatabaseType;
                IQueryEngine queryEngine = queryEngineFactory.GetQueryEngine(dbType);

                IActionResult? queryResult = null;

                try
                {
                    queryResult = await queryEngine.ExecuteAsync(context, dataSourceName).ConfigureAwait(false);
                }
                catch (DataApiBuilderException dabEx)
                {
                    // Allow the database to fail and return the resulting error
                    return BuildDabResponse(false, null, dabEx.Message, logger);
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
                    return BuildDabResponse(false, null, errorMessage, logger);
                }
                catch (DbException dbEx)
                {
                    // Handle generic database exceptions (works for PostgreSQL, MySQL, etc.)
                    logger?.LogError(dbEx, "Database error executing stored procedure {StoredProcedure}", entity);
                    return BuildDabResponse(false, null, $"Database error: {dbEx.Message}", logger);
                }
                catch (InvalidOperationException ioEx) when (ioEx.Message.Contains("connection", StringComparison.OrdinalIgnoreCase))
                {
                    // Handle connection-related issues
                    logger?.LogError(ioEx, "Database connection error");
                    return BuildDabResponse(false, null, "Failed to connect to the database.", logger);
                }
                catch (TimeoutException timeoutEx)
                {
                    // Handle query timeout
                    logger?.LogError(timeoutEx, "Stored procedure execution timeout for {StoredProcedure}", entity);
                    return BuildDabResponse(false, null, "The stored procedure execution timed out.", logger);
                }
                catch (Exception ex)
                {
                    // Generic database/execution errors
                    logger?.LogError(ex, "Unexpected error executing stored procedure {StoredProcedure}", entity);
                    return BuildDabResponse(false, null, "An error occurred while executing the stored procedure.", logger);
                }

                cancellationToken.ThrowIfCancellationRequested();

                // 10) Build response with execution result
                return BuildDabResponseFromActionResult(queryResult, logger);
            }
            catch (OperationCanceledException)
            {
                return BuildDabResponse(false, null, "The execute operation was canceled.", logger);
            }
            catch (ArgumentException argEx)
            {
                return BuildDabResponse(false, null, argEx.Message, logger);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Unexpected error in ExecuteEntityTool.");
                return BuildDabResponse(false, null, "An unexpected error occurred during the execute operation.", logger);
            }
        }

        /// <summary>
        /// Parses the execute arguments from the JSON input.
        /// </summary>
        private static bool TryParseExecuteArguments(
            JsonElement rootElement,
            out string entity,
            out Dictionary<string, object?> parameters,
            out string parseError)
        {
            entity = string.Empty;
            parameters = new Dictionary<string, object?>();
            parseError = string.Empty;

            if (rootElement.ValueKind != JsonValueKind.Object)
            {
                parseError = "Arguments must be an object";
                return false;
            }

            // Extract entity name (required)
            if (!rootElement.TryGetProperty("entity", out JsonElement entityElement) ||
                entityElement.ValueKind != JsonValueKind.String)
            {
                parseError = "Missing or invalid 'entity' parameter";
                return false;
            }

            entity = entityElement.GetString() ?? string.Empty;

            // Extract parameters if provided (optional)
            if (rootElement.TryGetProperty("parameters", out JsonElement parametersElement) &&
                parametersElement.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in parametersElement.EnumerateObject())
                {
                    parameters[property.Name] = GetParameterValue(property.Value);
                }
            }

            return true;
        }

        /// <summary>
        /// Converts a JSON element to its appropriate CLR type matching GraphQL data types.
        /// </summary>
        private static object? GetParameterValue(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.TryGetInt32(out int intValue) ? intValue : element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => element.ToString()
            };
        }

        /// <summary>
        /// Ensures that the role used on the request has the necessary authorizations.
        /// </summary>
        private static bool TryResolveAuthorizedRole(
            HttpContext httpContext,
            IAuthorizationResolver authorizationResolver,
            string entityName,
            EntityActionOperation operation,
            out string? effectiveRole,
            out string error)
        {
            effectiveRole = null;
            error = string.Empty;

            string roleHeader = httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER].ToString();

            if (string.IsNullOrWhiteSpace(roleHeader))
            {
                error = $"Client role header '{AuthorizationResolver.CLIENT_ROLE_HEADER}' is missing or empty.";
                return false;
            }

            string[] roles = roleHeader
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (roles.Length == 0)
            {
                error = $"Client role header '{AuthorizationResolver.CLIENT_ROLE_HEADER}' is missing or empty.";
                return false;
            }

            foreach (string role in roles)
            {
                bool allowed = authorizationResolver.AreRoleAndOperationDefinedForEntity(
                    entityName, role, operation);

                if (allowed)
                {
                    effectiveRole = role;
                    return true;
                }
            }

            error = $"You do not have permission to execute entity '{entityName}'.";
            return false;
        }

        /// <summary>
        /// Builds a standard DAB response from an IActionResult.
        /// </summary>
        private static CallToolResult BuildDabResponseFromActionResult(
            IActionResult? result,
            ILogger? logger)
        {
            Dictionary<string, object?> response = new();

            if (result is OkObjectResult okResult && okResult.Value != null)
            {
                // Extract the actual data from the action result
                if (okResult.Value is JsonDocument jsonDoc)
                {
                    JsonElement root = jsonDoc.RootElement;
                    if (root.ValueKind == JsonValueKind.Array)
                    {
                        response["value"] = root;
                    }
                    else
                    {
                        response["value"] = JsonSerializer.SerializeToElement(new[] { root });
                    }
                }
                else if (okResult.Value is JsonElement jsonElement)
                {
                    if (jsonElement.ValueKind == JsonValueKind.Array)
                    {
                        response["value"] = jsonElement;
                    }
                    else
                    {
                        response["value"] = JsonSerializer.SerializeToElement(new[] { jsonElement });
                    }
                }
                else
                {
                    // Serialize the value directly
                    JsonElement serialized = JsonSerializer.SerializeToElement(okResult.Value);
                    response["value"] = serialized;
                }

                logger?.LogInformation("ExecuteEntityTool completed successfully.");
            }
            else if (result is BadRequestObjectResult badRequest)
            {
                response["error"] = new Dictionary<string, object?>
                {
                    ["message"] = badRequest.Value?.ToString() ?? "Bad request"
                };
            }
            else if (result is UnauthorizedObjectResult)
            {
                response["error"] = new Dictionary<string, object?>
                {
                    ["message"] = "You do not have permission to execute this entity"
                };
            }
            else
            {
                // Empty or unknown result
                response["value"] = JsonSerializer.SerializeToElement(Array.Empty<object>());
                logger?.LogInformation("ExecuteEntityTool completed with empty result.");
            }

            string output = JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });

            return new CallToolResult
            {
                Content = new List<ContentBlock>
                {
                    new TextContentBlock { Type = "text", Text = output }
                },
                IsError = response.ContainsKey("error")
            };
        }

        /// <summary>
        /// Builds a standard DAB response payload.
        /// </summary>
        private static CallToolResult BuildDabResponse(
            bool success,
            JsonDocument? queryResult,
            string? errorMessage,
            ILogger? logger)
        {
            Dictionary<string, object?> response = new();

            if (success && queryResult != null)
            {
                // Extract the first result set (DAB standard for multiple result sets)
                JsonElement root = queryResult.RootElement;

                if (root.ValueKind == JsonValueKind.Array)
                {
                    response["value"] = root;
                }
                else if (root.ValueKind == JsonValueKind.Object)
                {
                    // Check if already wrapped in value property
                    if (root.TryGetProperty("value", out JsonElement valueElement))
                    {
                        response["value"] = valueElement;
                    }
                    else
                    {
                        // Single result, wrap in array for consistency
                        response["value"] = JsonSerializer.SerializeToElement(new[] { root });
                    }
                }
                else
                {
                    // Empty result
                    response["value"] = JsonSerializer.SerializeToElement(Array.Empty<object>());
                }

                logger?.LogInformation("ExecuteEntityTool completed successfully.");
            }
            else
            {
                // Standard DAB error response
                response["error"] = new Dictionary<string, object?>
                {
                    ["message"] = errorMessage ?? "An error occurred"
                };

                if (!string.IsNullOrEmpty(errorMessage))
                {
                    logger?.LogError("ExecuteEntityTool error: {Message}", errorMessage);
                }
            }

            string output = JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });

            return new CallToolResult
            {
                Content = new List<ContentBlock>
                {
                    new TextContentBlock { Type = "text", Text = output }
                },
                IsError = !success
            };
        }
    }
}
