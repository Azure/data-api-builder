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

                // Use common parser
                if (!McpArgumentParser.TryParseEntity(root, out entityName, out string parseError))
                {
                    return BuildErrorResult("InvalidArguments", parseError, logger);
                }

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

                if (!McpMetadataHelper.TryResolveMetadata(
                        entityName,
                        runtimeConfig,
                        serviceProvider,
                        out ISqlMetadataProvider sqlMetadataProvider,
                        out DatabaseObject dbObject,
                        out string dataSourceName,
                        out string metadataError))
                {
                    return BuildErrorResult("EntityNotFound", metadataError, logger);
                }

                // Authorization check in the existing entity
                IAuthorizationResolver authResolver = serviceProvider.GetRequiredService<IAuthorizationResolver>();
                IAuthorizationService authorizationService = serviceProvider.GetRequiredService<IAuthorizationService>();
                IHttpContextAccessor httpContextAccessor = serviceProvider.GetRequiredService<IHttpContextAccessor>();
                HttpContext? httpContext = httpContextAccessor.HttpContext;

                if (!McpAuthorizationHelper.ValidateRoleContext(httpContext, authResolver, out string roleCtxError))
                {
                    return BuildErrorResult("PermissionDenied", $"Permission denied: {roleCtxError} for read operation for entity: '{entityName}'.", logger);
                }

                if (!McpAuthorizationHelper.TryResolveAuthorizedRole(
                        httpContext!,
                        authResolver,
                        entityName,
                        EntityActionOperation.Read,
                        out string? effectiveRole,
                        out string readAuthError))
                {
                    // Provide tool-specific message rather than generic helper message.
                    string finalError = readAuthError.StartsWith("You do not have permission", StringComparison.OrdinalIgnoreCase)
                        ? $"You do not have permission to read records for entity '{entityName}'."
                        : readAuthError;
                    return BuildErrorResult("PermissionDenied", finalError, logger);
                }

                // Build and validate Find context
                RequestValidator requestValidator = new(serviceProvider.GetRequiredService<IMetadataProviderFactory>(), runtimeConfigProvider);
                FindRequestContext context = new(entityName, dbObject, true);
                httpContext!.Request.Method = "GET";

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

                if (orderby is not null && orderby.Any())
                {
                    string sortQueryString = $"?{RequestParser.SORT_URL}=" + string.Join(", ", orderby.Where(p => !string.IsNullOrWhiteSpace(p)));
                    if (sortQueryString.EndsWith(", "))
                    {
                        sortQueryString = sortQueryString[..^2];
                    }

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
                IQueryEngineFactory queryEngineFactory = serviceProvider.GetRequiredService<IQueryEngineFactory>();
                IQueryEngine queryEngine = queryEngineFactory.GetQueryEngine(sqlMetadataProvider.GetDatabaseType());
                JsonDocument? queryResult = await queryEngine.ExecuteAsync(context);
                IMetadataProviderFactory metadataProviderFactory = serviceProvider.GetRequiredService<IMetadataProviderFactory>();
                IActionResult actionResult = queryResult is null
                    ? SqlResponseHelpers.FormatFindResult(JsonDocument.Parse("[]").RootElement.Clone(), context, metadataProviderFactory.GetMetadataProvider(dataSourceName), runtimeConfigProvider.GetConfig(), httpContext, true)
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
