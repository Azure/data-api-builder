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
using Azure.DataApiBuilder.Service.Exceptions;
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
                Description = "Creates a new record in the specified entity.",
                InputSchema = JsonSerializer.Deserialize<JsonElement>(
                    @"{
                        ""type"": ""object"",
                        ""properties"": {
                            ""entity"": {
                                ""type"": ""string"",
                                ""description"": ""The name of the entity""
                            },
                            ""data"": {
                                ""type"": ""object"",
                                ""description"": ""The data for the new record""
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
            if (arguments == null)
            {
                return BuildErrorResult("Invalid Arguments", "No arguments provided", logger);
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                JsonElement root = arguments.RootElement;

                if (!root.TryGetProperty("entity", out JsonElement entityElement) ||
                    !root.TryGetProperty("data", out JsonElement dataElement))
                {
                    return BuildErrorResult("Invalid Arguments", "Missing required arguments 'entity' or 'data'", logger);
                }

                string entityName = entityElement.GetString() ?? string.Empty;
                if (string.IsNullOrEmpty(entityName))
                {
                    return BuildErrorResult("Invalid Arguments", "Entity name cannot be empty", logger);
                }

                RuntimeConfigProvider runtimeConfigProvider = serviceProvider.GetRequiredService<RuntimeConfigProvider>();
                if (!runtimeConfigProvider.TryGetConfig(out RuntimeConfig? runtimeConfig))
                {
                    return BuildErrorResult("Invalid Configuration", "Runtime configuration not available", logger);
                }

                string dataSourceName;
                try
                {
                    dataSourceName = runtimeConfig.GetDataSourceNameFromEntityName(entityName);
                }
                catch (DataApiBuilderException)
                {
                    return BuildErrorResult("Invalid Configuration", $"Entity '{entityName}' not found in configuration", logger);
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
                    return BuildErrorResult("Invalid Configuration", $"Database object for entity '{entityName}' not found", logger);
                }
                

                // Create an HTTP context for authorization
                IHttpContextAccessor httpContextAccessor = serviceProvider.GetRequiredService<IHttpContextAccessor>();
                HttpContext httpContext = httpContextAccessor.HttpContext ?? new DefaultHttpContext();
                IAuthorizationResolver authorizationResolver = serviceProvider.GetRequiredService<IAuthorizationResolver>();

                if (httpContext is null || !authorizationResolver.IsValidRoleContext(httpContext))
                {
                    return BuildErrorResult("PermissionDenied", "Permission denied: unable to resolve a valid role context for update operation.", logger);
                }

                // Validate that we have at least one role authorized for create
                if (!TryResolveAuthorizedRole(httpContext, authorizationResolver, entityName, out string authError))
                {
                    return BuildErrorResult("PermissionDenied", authError, logger);
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
                    catch (DataApiBuilderException ex)
                    {
                        return new CallToolResult
                        {
                            Content = [new TextContentBlock { Type = "text", Text = $"Error: Request validation failed: {ex.Message}" }]
                        };
                    }
                }

                IMutationEngineFactory mutationEngineFactory = serviceProvider.GetRequiredService<IMutationEngineFactory>();
                DatabaseType databaseType = sqlMetadataProvider.GetDatabaseType();
                IMutationEngine mutationEngine = mutationEngineFactory.GetMutationEngine(databaseType);

                IActionResult? result = await mutationEngine.ExecuteAsync(insertRequestContext);

                if (result is CreatedResult createdResult)
                {
                    return new CallToolResult
                    {
                        Content = [new TextContentBlock 
                        { 
                            Type = "text", 
                            Text = $"Successfully created record in entity '{entityName}'. Result: {JsonSerializer.Serialize(createdResult.Value)}"
                        }]
                    };
                }
                else if (result is ObjectResult objectResult)
                {
                    return new CallToolResult
                    {
                        Content = [new TextContentBlock 
                        { 
                            Type = "text", 
                            Text = $"Record creation completed with status {objectResult.StatusCode}. Result: {JsonSerializer.Serialize(objectResult.Value)}"
                        }]
                    };
                }
                else
                {
                    return new CallToolResult
                    {
                        Content = [new TextContentBlock { Type = "text", Text = $"Successfully created record in entity '{entityName}'" }]
                    };
                }
            }
            catch (Exception ex)
            {
                return new CallToolResult
                {
                    Content = [new TextContentBlock { Type = "text", Text = $"Error: {ex.Message}" }]
                };
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
                    entityName, role, EntityActionOperation.Insert);

                if (allowed)
                {
                    return true;
                }
            }

            error = "You do not have permission to create records for this entity.";
            return false;
        }

        private static CallToolResult BuildErrorResult(
        string errorType,
        string message,
        ILogger? logger)
        {
            Dictionary<string, object?> errorObj = new()
            {
                ["status"] = "error",
                ["error"] = new Dictionary<string, object?>
                {
                    ["type"] = errorType,
                    ["message"] = message
                }
            };

            string output = JsonSerializer.Serialize(errorObj);

            logger?.LogWarning("UpdateRecordTool error {ErrorType}: {Message}", errorType, message);

            return new CallToolResult
            {
                Content =
                [
                    new TextContentBlock { Type = "text", Text = output }
                ]
            };
        }
    }
}
