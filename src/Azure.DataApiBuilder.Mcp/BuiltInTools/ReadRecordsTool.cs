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
                Description = "Retrieves records from a given entity.",
                InputSchema = JsonSerializer.Deserialize<JsonElement>(
                    @"{
                        ""type"": ""object"",
                        ""properties"": {
                            ""entity"": {
                                ""type"": ""string"",
                                ""description"": ""The name of the entity to read, as provided by the describe_entities tool. Required.""
                            },
                            ""select"": {
                                ""type"": ""string"",
                                ""description"": ""A comma-separated list of field names to include in the response. If omitted, all fields are returned. Optional.""
                            },
                            ""filter"": {
                                ""type"": ""string"",
                                ""description"": ""A case-insensitive OData-like expression that defines a query predicate. Supports logical grouping with parentheses and the operators eq, ne, gt, ge, lt, le, and, or, not. Examples: year ge 1990, date lt 2025-01-01T00:00:00Z, (title eq 'Foundation') and (available ne false). Optional.""
                            },
                            ""first"": {
                                ""type"": ""integer"",
                                ""description"": ""The maximum number of records to return in the current page. Optional.""
                            },
                            ""orderby"": {
                                ""type"": ""array"",
                                ""items"": { ""type"": ""string"" },
                                ""description"": ""A list of field names and directions for sorting, for example 'name asc' or 'year desc'. Optional.""
                            },
                            ""after"": {
                                ""type"": ""string"",
                                ""description"": ""A cursor token for retrieving the next page of results. Returned as 'after' in the previous response. Optional.""
                            }
                        }
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

            // Get runtime config
            RuntimeConfigProvider runtimeConfigProvider = serviceProvider.GetRequiredService<RuntimeConfigProvider>();
            RuntimeConfig runtimeConfig = runtimeConfigProvider.GetConfig();

            if (runtimeConfig.McpDmlTools?.ReadRecords is not true)
            {
                return BuildErrorResult(
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
                    return BuildErrorResult("InvalidArguments", "No arguments provided.", logger);
                }

                JsonElement root = arguments.RootElement;

                if (!root.TryGetProperty("entity", out JsonElement entityElement) || string.IsNullOrWhiteSpace(entityElement.GetString()))
                {
                    return BuildErrorResult("InvalidArguments", "Missing required argument 'entity'.", logger);
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
                    return BuildErrorResult("EntityNotFound", $"Entity '{entityName}' is not defined in the configuration.", logger);
                }

                if (!sqlMetadataProvider.EntityToDatabaseObject.TryGetValue(entityName, out DatabaseObject? dbObject) || dbObject is null)
                {
                    return BuildErrorResult("EntityNotFound", $"Entity '{entityName}' is not defined in the configuration.", logger);
                }

                // Authorization check in the existing entity
                IAuthorizationResolver authResolver = serviceProvider.GetRequiredService<IAuthorizationResolver>();
                IAuthorizationService authorizationService = serviceProvider.GetRequiredService<IAuthorizationService>();
                IHttpContextAccessor httpContextAccessor = serviceProvider.GetRequiredService<IHttpContextAccessor>();
                HttpContext? httpContext = httpContextAccessor.HttpContext;

                if (httpContext is null || !authResolver.IsValidRoleContext(httpContext))
                {
                    return BuildErrorResult("PermissionDenied", $"You do not have permission to read records for entity '{entityName}'.", logger);
                }

                if (!TryResolveAuthorizedRole(httpContext, authResolver, entityName, out string? effectiveRole, out string authError))
                {
                    return BuildErrorResult("PermissionDenied", authError, logger);
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
                            return BuildErrorResult("InvalidArguments", "Parameters inside 'orderby' argument cannot be empty or null.", logger);
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
                    return BuildErrorResult("PermissionDenied", DataApiBuilderException.AUTHORIZATION_FAILURE, logger);
                }

                // Execute
                IQueryEngine queryEngine = queryEngineFactory.GetQueryEngine(sqlMetadataProvider.GetDatabaseType());
                JsonDocument? queryResult = await queryEngine.ExecuteAsync(context);
                IActionResult actionResult = queryResult is null ? SqlResponseHelpers.FormatFindResult(JsonDocument.Parse("[]").RootElement.Clone(), context, metadataProviderFactory.GetMetadataProvider(dataSourceName), runtimeConfigProvider.GetConfig(), httpContext, true)
                                               : SqlResponseHelpers.FormatFindResult(queryResult.RootElement.Clone(), context, metadataProviderFactory.GetMetadataProvider(dataSourceName), runtimeConfigProvider.GetConfig(), httpContext, true);

                // Normalize response
                string rawPayloadJson = ExtractResultJson(actionResult);
                JsonDocument result = JsonDocument.Parse(rawPayloadJson);
                JsonElement queryRoot = result.RootElement;

                return BuildSuccessResult(
                    entityName,
                    queryRoot.Clone(),
                    logger);
            }
            catch (OperationCanceledException)
            {
                return BuildErrorResult("OperationCanceled", "The read operation was canceled.", logger);
            }
            catch (DbException argEx)
            {
                return BuildErrorResult("DatabaseOperationFailed", argEx.Message, logger);
            }
            catch (ArgumentException argEx)
            {
                return BuildErrorResult("InvalidArguments", argEx.Message, logger);
            }
            catch (DataApiBuilderException argEx)
            {
                return BuildErrorResult(argEx.StatusCode.ToString(), argEx.Message, logger);
            }
            catch (Exception)
            {
                return BuildErrorResult("UnexpectedError", "Unexpected error occurred in ReadRecordsTool.", logger);
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

        /// <summary>
        /// Returns a result from the query in the case that it was successfully ran.
        /// </summary>
        /// <param name="entityName">Name of the entity used in the request.</param>
        /// <param name="engineRootElement">Query result from engine.</param>
        /// <param name="logger">MCP logger that returns all logged events.</param>
        private static CallToolResult BuildSuccessResult(
            string entityName,
            JsonElement engineRootElement,
            ILogger? logger)
        {
            // Build normalized response
            Dictionary<string, object?> normalized = new()
            {
                ["status"] = "success",
                ["result"] = engineRootElement // only requested values
            };

            string output = JsonSerializer.Serialize(normalized, new JsonSerializerOptions { WriteIndented = true });

            logger?.LogInformation("ReadRecordsTool success for entity {Entity}.", entityName);

            return new CallToolResult
            {
                Content = new List<ContentBlock>
                {
                    new TextContentBlock { Type = "text", Text = output }
                }
            };
        }

        /// <summary>
        /// Returns an error if the query failed to run at any point.
        /// </summary>
        /// <param name="errorType">Type of error that is encountered.</param>
        /// <param name="message">Error message given to the user.</param>
        /// <param name="logger">MCP logger that returns all logged events.</param>
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

            logger?.LogError("ReadRecordsTool error {ErrorType}: {Message}", errorType, message);

            return new CallToolResult
            {
                Content =
                [
                    new TextContentBlock { Type = "text", Text = output }
                ],
                IsError = true
            };
        }

        /// <summary>
        /// Extracts a JSON string from a typical IActionResult.
        /// Falls back to "{}" for unsupported/empty cases to avoid leaking internals.
        /// </summary>
        private static string ExtractResultJson(IActionResult? result)
        {
            switch (result)
            {
                case ObjectResult obj:
                    if (obj.Value is JsonElement je)
                    {
                        return je.GetRawText();
                    }

                    if (obj.Value is JsonDocument jd)
                    {
                        return jd.RootElement.GetRawText();
                    }

                    return JsonSerializer.Serialize(obj.Value ?? new object());

                case ContentResult content:
                    return string.IsNullOrWhiteSpace(content.Content) ? "{}" : content.Content;

                default:
                    return "{}";
            }
        }
    }
}
