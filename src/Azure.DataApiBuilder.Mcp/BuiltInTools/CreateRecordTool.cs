// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using static Azure.DataApiBuilder.Mcp.Model.McpEnums;

namespace Azure.DataApiBuilder.Mcp.BuiltInTools
{
    public class CreateRecordTool : IMcpTool
    {
        public ToolType ToolType { get; } = ToolType.BuiltIn;

        public Tool GetToolMetadata()
        {
            return new Tool
            {
                Name = "create_record",
                Description = "STEP 1: describe_entities -> find entities with CREATE permission and their fields. STEP 2: call this tool with matching field names and values.",
                InputSchema = JsonSerializer.Deserialize<JsonElement>(
                    @"{
                        ""type"": ""object"",
                        ""properties"": {
                            ""entity"": {
                                ""type"": ""string"",
                                ""description"": ""Entity name with CREATE permission.""
                            },
                            ""data"": {
                                ""type"": ""object"",
                                ""description"": ""Required fields and values for the new record.""
                            }
                        },
                        ""required"": [""entity"", ""data""]
                    }"
                )
            };
        }

        public async Task<CallToolResult> ExecuteAsync(
            JsonDocument? arguments,
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken = default)
        {
            ILogger<CreateRecordTool>? logger = serviceProvider.GetService<ILogger<CreateRecordTool>>();
            string toolName = GetToolMetadata().Name;
            if (arguments == null)
            {
                return Utils.McpResponseBuilder.BuildErrorResult(toolName, "Invalid Arguments", "No arguments provided", logger);
            }

            RuntimeConfigProvider runtimeConfigProvider = serviceProvider.GetRequiredService<RuntimeConfigProvider>();
            if (!runtimeConfigProvider.TryGetConfig(out RuntimeConfig? runtimeConfig))
            {
                return Utils.McpResponseBuilder.BuildErrorResult(toolName, "Invalid Configuration", "Runtime configuration not available", logger);
            }

            if (runtimeConfig.McpDmlTools?.CreateRecord != true)
            {
                return Utils.McpResponseBuilder.BuildErrorResult(
                    toolName,
                    "ToolDisabled",
                    "The create_record tool is disabled in the configuration.",
                    logger);
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                JsonElement root = arguments.RootElement;

                if (!root.TryGetProperty("entity", out JsonElement entityElement) ||
                    !root.TryGetProperty("data", out JsonElement dataElement))
                {
                    return Utils.McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments", "Missing required arguments 'entity' or 'data'", logger);
                }

                string entityName = entityElement.GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(entityName))
                {
                    return Utils.McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments", "Entity name cannot be empty", logger);
                }

                string dataSourceName;
                try
                {
                    dataSourceName = runtimeConfig.GetDataSourceNameFromEntityName(entityName);
                }
                catch (Exception)
                {
                    return Utils.McpResponseBuilder.BuildErrorResult(toolName, "InvalidConfiguration", $"Entity '{entityName}' not found in configuration", logger);
                }

                IMetadataProviderFactory metadataProviderFactory = serviceProvider.GetRequiredService<IMetadataProviderFactory>();
                ISqlMetadataProvider sqlMetadataProvider = metadataProviderFactory.GetMetadataProvider(dataSourceName);

                DatabaseObject dbObject;
                try
                {
                    dbObject = sqlMetadataProvider.GetDatabaseObjectByKey(entityName);
                }
                catch (Exception)
                {
                    return Utils.McpResponseBuilder.BuildErrorResult(toolName, "InvalidConfiguration", $"Database object for entity '{entityName}' not found", logger);
                }

                // Create an HTTP context for authorization
                IHttpContextAccessor httpContextAccessor = serviceProvider.GetRequiredService<IHttpContextAccessor>();
                HttpContext httpContext = httpContextAccessor.HttpContext ?? new DefaultHttpContext();
                IAuthorizationResolver authorizationResolver = serviceProvider.GetRequiredService<IAuthorizationResolver>();

                if (httpContext is null || !authorizationResolver.IsValidRoleContext(httpContext))
                {
                    return Utils.McpResponseBuilder.BuildErrorResult(toolName, "PermissionDenied", "Permission denied: Unable to resolve a valid role context for update operation.", logger);
                }

                // Validate that we have at least one role authorized for create
                if (!TryResolveAuthorizedRole(httpContext, authorizationResolver, entityName, out string authError))
                {
                    return Utils.McpResponseBuilder.BuildErrorResult(toolName, "PermissionDenied", authError, logger);
                }

                JsonElement insertPayloadRoot = dataElement.Clone();
                InsertRequestContext insertRequestContext = new(
                    entityName,
                    dbObject,
                    insertPayloadRoot,
                    EntityActionOperation.Insert);

                RequestValidator requestValidator = serviceProvider.GetRequiredService<RequestValidator>();

                // Only validate tables
                if (dbObject.SourceType is EntitySourceType.Table)
                {
                    try
                    {
                        requestValidator.ValidateInsertRequestContext(insertRequestContext);
                    }
                    catch (Exception ex)
                    {
                        return Utils.McpResponseBuilder.BuildErrorResult(toolName, "ValidationFailed", $"Request validation failed: {ex.Message}", logger);
                    }
                }
                else
                {
                    return Utils.McpResponseBuilder.BuildErrorResult(
                        toolName,
                        "InvalidCreateTarget",
                        "The create_record tool is only available for tables.",
                        logger);
                }

                IMutationEngineFactory mutationEngineFactory = serviceProvider.GetRequiredService<IMutationEngineFactory>();
                DatabaseType databaseType = sqlMetadataProvider.GetDatabaseType();
                IMutationEngine mutationEngine = mutationEngineFactory.GetMutationEngine(databaseType);

                IActionResult? result = await mutationEngine.ExecuteAsync(insertRequestContext);

                if (result is CreatedResult createdResult)
                {
                    return Utils.McpResponseBuilder.BuildSuccessResult(
                        new Dictionary<string, object?>
                        {
                            ["entity"] = entityName,
                            ["result"] = createdResult.Value,
                            ["message"] = $"Successfully created record in entity '{entityName}'"
                        },
                        logger,
                        $"Successfully created record in entity '{entityName}'");
                }
                else if (result is ObjectResult objectResult)
                {
                    bool isError = objectResult.StatusCode.HasValue && objectResult.StatusCode.Value >= 400 && objectResult.StatusCode.Value != 403;
                    if (isError)
                    {
                        return Utils.McpResponseBuilder.BuildErrorResult(
                            toolName,
                            "CreateFailed",
                            $"Failed to create record in entity '{entityName}'. Error: {JsonSerializer.Serialize(objectResult.Value)}",
                            logger);
                    }
                    else
                    {
                        return Utils.McpResponseBuilder.BuildSuccessResult(
                            new Dictionary<string, object?>
                            {
                                ["entity"] = entityName,
                                ["result"] = objectResult.Value,
                                ["message"] = $"Successfully created record in entity '{entityName}'. Unable to perform read-back of inserted records."
                            },
                            logger,
                            $"Successfully created record in entity '{entityName}'. Unable to perform read-back of inserted records.");
                    }
                }
                else
                {
                    if (result is null)
                    {
                        return Utils.McpResponseBuilder.BuildErrorResult(
                            toolName,
                            "UnexpectedError",
                            $"Mutation engine returned null result for entity '{entityName}'",
                            logger);
                    }
                    else
                    {
                        return Utils.McpResponseBuilder.BuildSuccessResult(
                            new Dictionary<string, object?>
                            {
                                ["entity"] = entityName,
                                ["message"] = $"Create operation completed with unexpected result type: {result.GetType().Name}"
                            },
                            logger,
                            $"Create operation completed for entity '{entityName}' with unexpected result type: {result.GetType().Name}");
                    }
                }
            }
            catch (Exception ex)
            {
                return Utils.McpResponseBuilder.BuildErrorResult(toolName, "Error", $"Error: {ex.Message}", logger);
            }
        }

        private static bool TryResolveAuthorizedRole(
            HttpContext httpContext,
            IAuthorizationResolver authorizationResolver,
            string entityName,
            out string error)
        {
            error = string.Empty;

            string roleHeader = httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER].ToString();

            if (string.IsNullOrWhiteSpace(roleHeader))
            {
                error = "Client role header is missing or empty.";
                return false;
            }

            string[] roles = roleHeader
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (roles.Length == 0)
            {
                error = "Client role header is missing or empty.";
                return false;
            }

            foreach (string role in roles)
            {
                bool allowed = authorizationResolver.AreRoleAndOperationDefinedForEntity(
                    entityName, role, EntityActionOperation.Create);

                if (allowed)
                {
                    return true;
                }
            }

            error = "You do not have permission to create records for this entity.";
            return false;
        }
    }
}
