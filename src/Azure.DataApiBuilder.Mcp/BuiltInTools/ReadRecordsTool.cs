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
using Azure.DataApiBuilder.Core.Parsers;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Mcp.Model;
using Azure.DataApiBuilder.Mcp.Utils;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using static Azure.DataApiBuilder.Mcp.Model.McpEnums;

namespace Azure.DataApiBuilder.Mcp.BuiltInTools
{
    public class ReadRecordsTool : IMcpTool
    {
        public ToolType ToolType { get; } = ToolType.BuiltIn;

        public Tool GetToolMetadata()
        {
            return new Tool
            {
                Name = "read_records",
                Description = "STEP 1: describe_entities -> find entities with READ permission and their fields. STEP 2: call this tool with select, filter, sort, or pagination options.",
                InputSchema = JsonSerializer.Deserialize<JsonElement>(
                    @"{
                        ""type"": ""object"",
                        ""properties"": {
                            ""entity"": {
                                ""type"": ""string"",
                                ""description"": ""Entity name with READ permission.""
                            },
                            ""select"": {
                                ""type"": ""string"",
                                ""description"": ""Comma-separated field names.""
                            },
                            ""filter"": {
                                ""type"": ""string"",
                                ""description"": ""OData expression: eq, ne, gt, ge, lt, le, and, or, not.""
                            },
                            ""orderby"": {
                                ""type"": ""array"",
                                ""items"": { ""type"": ""string"" },
                                ""description"": ""Sort fields and directions, e.g., ['name asc', 'year desc'].""
                            },
                            ""first"": {
                                ""type"": ""integer"",
                                ""description"": ""Max number of records (page size).""
                            },
                            ""after"": {
                                ""type"": ""string"",
                                ""description"": ""Cursor token for next page.""
                            }
                        },
                        ""required"": [""entity""]
                    }"
                )
            };
        }

        public async Task<CallToolResult> ExecuteAsync(
            JsonDocument? arguments,
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken = default)
        {
            ILogger<ReadRecordsTool>? logger = serviceProvider.GetService<ILogger<ReadRecordsTool>>();
            string toolName = GetToolMetadata().Name;

            // Get runtime config
            RuntimeConfigProvider runtimeConfigProvider = serviceProvider.GetRequiredService<RuntimeConfigProvider>();
            RuntimeConfig runtimeConfig = runtimeConfigProvider.GetConfig();

            if (runtimeConfig.McpDmlTools?.ReadRecords is not true)
            {
                return McpResponseBuilder.BuildErrorResult(
                    toolName,
                    "ToolDisabled",
                    "The read_records tool is disabled in the configuration.",
                    logger);
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                string entityName;
                string? select = null;
                string? filter = null;
                int? first = null;
                IEnumerable<string>? orderby = null;
                string? after = null;

                // Extract arguments
                if (arguments == null)
                {
                    return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments", "No arguments provided.", logger);
                }

                JsonElement root = arguments.RootElement;

                if (!root.TryGetProperty("entity", out JsonElement entityElement) || string.IsNullOrWhiteSpace(entityElement.GetString()))
                {
                    return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments", "Missing required argument 'entity'.", logger);
                }

                entityName = entityElement.GetString()!;

                if (root.TryGetProperty("select", out JsonElement selectElement))
                {
                    select = selectElement.GetString();
                }

                if (root.TryGetProperty("filter", out JsonElement filterElement))
                {
                    filter = filterElement.GetString();
                }

                if (root.TryGetProperty("first", out JsonElement firstElement))
                {
                    first = firstElement.GetInt32();
                }

                if (root.TryGetProperty("orderby", out JsonElement orderbyElement))
                {
                    orderby = (IEnumerable<string>?)orderbyElement.EnumerateArray().Select(e => e.GetString());
                }

                if (root.TryGetProperty("after", out JsonElement afterElement))
                {
                    after = afterElement.GetString();
                }

                // Get required services & configuration
                IQueryEngineFactory queryEngineFactory = serviceProvider.GetRequiredService<IQueryEngineFactory>();
                IMetadataProviderFactory metadataProviderFactory = serviceProvider.GetRequiredService<IMetadataProviderFactory>();

                // Check metadata for entity exists
                string dataSourceName;
                ISqlMetadataProvider sqlMetadataProvider;

                try
                {
                    dataSourceName = runtimeConfig.GetDataSourceNameFromEntityName(entityName);
                    sqlMetadataProvider = metadataProviderFactory.GetMetadataProvider(dataSourceName);
                }
                catch (Exception)
                {
                    return McpResponseBuilder.BuildErrorResult(toolName, "EntityNotFound", $"Entity '{entityName}' is not defined in the configuration.", logger);
                }

                if (!sqlMetadataProvider.EntityToDatabaseObject.TryGetValue(entityName, out DatabaseObject? dbObject) || dbObject is null)
                {
                    return McpResponseBuilder.BuildErrorResult(toolName, "EntityNotFound", $"Entity '{entityName}' is not defined in the configuration.", logger);
                }

                // Authorization check in the existing entity
                IAuthorizationResolver authResolver = serviceProvider.GetRequiredService<IAuthorizationResolver>();
                IAuthorizationService authorizationService = serviceProvider.GetRequiredService<IAuthorizationService>();
                IHttpContextAccessor httpContextAccessor = serviceProvider.GetRequiredService<IHttpContextAccessor>();
                HttpContext? httpContext = httpContextAccessor.HttpContext;

                if (httpContext is null || !authResolver.IsValidRoleContext(httpContext))
                {
                    return McpResponseBuilder.BuildErrorResult(toolName, "PermissionDenied", $"You do not have permission to read records for entity '{entityName}'.", logger);
                }

                if (!TryResolveAuthorizedRole(httpContext, authResolver, entityName, out string? effectiveRole, out string authError))
                {
                    return McpResponseBuilder.BuildErrorResult(toolName, "PermissionDenied", authError, logger);
                }

                // Build and validate Find context
                RequestValidator requestValidator = new(metadataProviderFactory, runtimeConfigProvider);
                FindRequestContext context = new(entityName, dbObject, true);
                httpContext.Request.Method = "GET";

                requestValidator.ValidateEntity(entityName);

                if (!string.IsNullOrWhiteSpace(select))
                {
                    // Update the context to specify which fields will be returned from the entity.
                    IEnumerable<string> fieldsReturnedForFind = select.Split(",").ToList();
                    context.UpdateReturnFields(fieldsReturnedForFind);
                }

                if (!string.IsNullOrWhiteSpace(filter))
                {
                    string filterQueryString = $"?{RequestParser.FILTER_URL}={filter}";
                    context.FilterClauseInUrl = sqlMetadataProvider.GetODataParser().GetFilterClause(filterQueryString, $"{context.EntityName}.{context.DatabaseObject.FullName}");
                }

                if (orderby is not null && orderby.Count() != 0)
                {
                    string sortQueryString = $"?{RequestParser.SORT_URL}=";
                    foreach (string param in orderby)
                    {
                        if (string.IsNullOrWhiteSpace(param))
                        {
                            return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments", "Parameters inside 'orderby' argument cannot be empty or null.", logger);
                        }

                        sortQueryString += $"{param}, ";
                    }

                    sortQueryString = sortQueryString.Substring(0, sortQueryString.Length - 2);
                    (context.OrderByClauseInUrl, context.OrderByClauseOfBackingColumns) = RequestParser.GenerateOrderByLists(context, sqlMetadataProvider, sortQueryString);
                }

                context.First = first;
                context.After = after;

                // The final authorization check on columns occurs after the request is fully parsed and validated.
                requestValidator.ValidateRequestContext(context);

                AuthorizationResult authorizationResult = await authorizationService.AuthorizeAsync(
                    user: httpContext.User,
                    resource: context,
                    requirements: new[] { new ColumnsPermissionsRequirement() });
                if (!authorizationResult.Succeeded)
                {
                    return McpResponseBuilder.BuildErrorResult(toolName, "PermissionDenied", DataApiBuilderException.AUTHORIZATION_FAILURE, logger);
                }

                // Execute
                IQueryEngine queryEngine = queryEngineFactory.GetQueryEngine(sqlMetadataProvider.GetDatabaseType());
                JsonDocument? queryResult = await queryEngine.ExecuteAsync(context);
                IActionResult actionResult = queryResult is null ? SqlResponseHelpers.FormatFindResult(JsonDocument.Parse("[]").RootElement.Clone(), context, metadataProviderFactory.GetMetadataProvider(dataSourceName), runtimeConfigProvider.GetConfig(), httpContext, true)
                                               : SqlResponseHelpers.FormatFindResult(queryResult.RootElement.Clone(), context, metadataProviderFactory.GetMetadataProvider(dataSourceName), runtimeConfigProvider.GetConfig(), httpContext, true);

                // Normalize response
                string rawPayloadJson = McpResponseBuilder.ExtractResultJson(actionResult);
                using JsonDocument result = JsonDocument.Parse(rawPayloadJson);
                JsonElement queryRoot = result.RootElement;

                return McpResponseBuilder.BuildSuccessResult(
                    new Dictionary<string, object?>
                    {
                        ["entity"] = entityName,
                        ["result"] = queryRoot.Clone(),
                        ["message"] = $"Successfully read records for entity '{entityName}'"
                    },
                    logger,
                    $"ReadRecordsTool success for entity {entityName}.");
            }
            catch (OperationCanceledException)
            {
                return McpResponseBuilder.BuildErrorResult(toolName, "OperationCanceled", "The read operation was canceled.", logger);
            }
            catch (DbException argEx)
            {
                return McpResponseBuilder.BuildErrorResult(toolName, "DatabaseOperationFailed", argEx.Message, logger);
            }
            catch (ArgumentException argEx)
            {
                return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments", argEx.Message, logger);
            }
            catch (DataApiBuilderException argEx)
            {
                return McpResponseBuilder.BuildErrorResult(toolName, argEx.StatusCode.ToString(), argEx.Message, logger);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Unexpected error in ReadRecordsTool.");
                return McpResponseBuilder.BuildErrorResult(toolName, "UnexpectedError", "Unexpected error occurred in ReadRecordsTool.", logger);
            }
        }

        /// <summary>
        /// Ensures that the role used on the request has the necessary authorizations.
        /// </summary>
        /// <param name="httpContext">Contains request headers and metadata of the user.</param>
        /// <param name="authorizationResolver">Resolver used to check if role has necessary authorizations.</param>
        /// <param name="entityName">Name of the entity used in the request.</param>
        /// <param name="effectiveRole">Role defined in client role header.</param>
        /// <param name="error">Error message given to the user.</param>
        /// <returns>True if the user role is authorized, along with the role.</returns>
        private static bool TryResolveAuthorizedRole(
            HttpContext httpContext,
            IAuthorizationResolver authorizationResolver,
            string entityName,
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
                    entityName, role, EntityActionOperation.Read);

                if (allowed)
                {
                    effectiveRole = role;
                    return true;
                }
            }

            error = $"You do not have permission to read records for entity '{entityName}'.";
            return false;
        }
    }
}
