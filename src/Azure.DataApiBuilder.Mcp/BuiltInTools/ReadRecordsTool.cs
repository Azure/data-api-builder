// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
        // private readonly IMetadataProviderFactory _metadataProviderFactory;
        // private readonly IQueryEngineFactory _queryEngineFactory;

        public ToolType ToolType { get; } = ToolType.BuiltIn;

        public Tool GetToolMetadata()
        {
            return new Tool
            {
                Name = "read_records",
                Description = "Reads the records from the specified entity.",
                InputSchema = JsonSerializer.Deserialize<JsonElement>(
                    @"{
                        ""type"": ""object"",
                        ""properties"": {
                            ""entity"": {
                                ""type"": ""string"",
                                ""description"": ""The entity name to read from. Required.""
                            },
                            ""select"": {
                                ""type"": ""string"",
                                ""description"": ""A CSV of field names to include in the response. If not provided, all fields are returned. Optional.""
                            },
                            ""filter"": {
                                ""type"": ""string"",
                                ""description"": ""A filter expression string to restrict results. Optional.""
                            },
                            ""first"": {
                                ""type"": ""integer"",
                                ""description"": ""The maximum number of records to return in this page. Optional.""
                            },
                            ""orderby"": {
                                ""type"": ""array"",
                                ""items"": { ""type"": ""string"" },
                                ""description"": ""A list of field names and directions for sorting (e.g., \""name asc\""). Optional.""
                            },
                            ""after"": {
                                ""type"": ""string"",
                                ""description"": ""A cursor token for retrieving the next page of results. Optional.""
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

            if (arguments == null)
            {
                return BuildErrorResult("InvalidArguments", "No arguments provided.", logger);
            }

            try
            {
                string entityName;
                string? select = null;
                string? filter = null;
                int? first = null;
                IEnumerable<string>? orderby = null;
                string? after = null;

                // Extract arguments
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
                RuntimeConfigProvider runtimeConfigProvider = serviceProvider.GetRequiredService<RuntimeConfigProvider>();
                RuntimeConfig runtimeConfig = runtimeConfigProvider.GetConfig();

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
                IHttpContextAccessor httpContextAccessor = serviceProvider.GetRequiredService<IHttpContextAccessor>();
                HttpContext? httpContext = httpContextAccessor.HttpContext;

                if (httpContext is null || !authResolver.IsValidRoleContext(httpContext))
                {
                    return BuildErrorResult("PermissionDenied", "You do not have permission to read records for this entity.", logger);
                }

                if (!TryResolveAuthorizedRole(httpContext, authResolver, entityName, out string? effectiveRole, out string authError))
                {
                    return BuildErrorResult("PermissionDenied", authError, logger);
                }

                // Build and validate Find context
                RequestValidator requestValidator = new(metadataProviderFactory, runtimeConfigProvider);
                FindRequestContext context = new(entityName, dbObject, true);

                requestValidator.ValidateEntity(entityName);

                IEnumerable<string> fieldsReturnedForFind;
                if (!string.IsNullOrWhiteSpace(select))
                {
                    fieldsReturnedForFind = select.Split(",").ToList();
                }
                else
                {
                    fieldsReturnedForFind = authResolver.GetAllowedExposedColumns(context.EntityName, effectiveRole!, context.OperationType);
                }

                context.UpdateReturnFields(fieldsReturnedForFind);

                if (!string.IsNullOrWhiteSpace(filter))
                {
                    string filterQueryString = $"?{RequestParser.FILTER_URL}={filter}";
                    context.FilterClauseInUrl = sqlMetadataProvider.GetODataParser().GetFilterClause(filterQueryString, $"{context.EntityName}.{context.DatabaseObject.FullName}");
                }

                if (orderby is not null)
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

                // Execute
                IQueryEngine queryEngine = queryEngineFactory.GetQueryEngine(sqlMetadataProvider.GetDatabaseType());
                JsonDocument? queryResult = await queryEngine.ExecuteAsync(context);
                IActionResult actionResult = queryResult is null ? SqlResponseHelpers.FormatFindResult(JsonDocument.Parse("[]").RootElement.Clone(), context, metadataProviderFactory.GetMetadataProvider(dataSourceName), runtimeConfigProvider.GetConfig(), httpContext, true)
                                               : SqlResponseHelpers.FormatFindResult(queryResult.RootElement.Clone(), context, metadataProviderFactory.GetMetadataProvider(dataSourceName), runtimeConfigProvider.GetConfig(), httpContext, true);

                cancellationToken.ThrowIfCancellationRequested();

                // Normalize response
                string rawPayloadJson = ExtractResultJson(actionResult);
                using JsonDocument result = JsonDocument.Parse(rawPayloadJson);
                JsonElement queryRoot = result.RootElement;

                return BuildSuccessResult(
                    entityName,
                    queryRoot.Clone(),
                    logger);
            }
            catch (OperationCanceledException)
            {
                return BuildErrorResult("OperationCanceled", "The read operation was canceled.", logger: null);
            }
            catch (ArgumentException argEx)
            {
                return BuildErrorResult("InvalidArguments", argEx.Message, logger);
            }
            catch (Exception ex)
            {
                ILogger<ReadRecordsTool>? innerLogger = serviceProvider.GetService<ILogger<ReadRecordsTool>>();
                innerLogger?.LogError(ex, "Unexpected error in ReadRecordsTool.");

                return BuildErrorResult(
                    "UnexpectedError",
                    "An unexpected error occurred while reading the record.",
                    logger: null);
            }
        }

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
                    entityName, role, EntityActionOperation.Read);

                if (allowed)
                {
                    effectiveRole = role;
                    return true;
                }
            }

            error = "You do not have permission to read records for this entity.";
            return false;
        }

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

            logger?.LogWarning("ReadRecordsTool error {ErrorType}: {Message}", errorType, message);

            return new CallToolResult
            {
                Content =
                [
                    new TextContentBlock { Type = "text", Text = output }
                ]
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
