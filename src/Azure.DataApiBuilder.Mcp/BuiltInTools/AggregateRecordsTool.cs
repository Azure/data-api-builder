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
    /// <summary>
    /// Tool to aggregate records from a table/view entity configured in DAB.
    /// Supports count, avg, sum, min, max with optional distinct, filter, groupby, having, orderby.
    /// </summary>
    public class AggregateRecordsTool : IMcpTool
    {
        public ToolType ToolType { get; } = ToolType.BuiltIn;

        private static readonly HashSet<string> _validFunctions = new(StringComparer.OrdinalIgnoreCase) { "count", "avg", "sum", "min", "max" };

        public Tool GetToolMetadata()
        {
            return new Tool
            {
                Name = "aggregate_records",
                Description = "Computes aggregations (count, avg, sum, min, max) on entity data. "
                    + "STEP 1: Call describe_entities to discover entities with READ permission and their field names. "
                    + "STEP 2: Call this tool with the exact entity name, an aggregation function, and a field name from STEP 1. "
                    + "REQUIRED: entity (exact entity name), function (one of: count, avg, sum, min, max), field (exact field name, or '*' ONLY for count). "
                    + "OPTIONAL: filter (OData WHERE clause applied before aggregating, e.g. 'unitPrice lt 10'), "
                    + "distinct (true to deduplicate values before aggregating), "
                    + "groupby (array of field names to group results by, e.g. ['categoryName']), "
                    + "orderby ('asc' or 'desc' to sort grouped results by aggregated value; requires groupby), "
                    + "having (object to filter groups after aggregating, operators: eq, neq, gt, gte, lt, lte, in; requires groupby), "
                    + "first (integer >= 1, maximum grouped results to return; requires groupby), "
                    + "after (opaque cursor string from a previous response's endCursor; requires first and groupby). "
                    + "RESPONSE: The aggregated value is aliased as '{function}_{field}' (e.g. avg_unitPrice, sum_revenue). "
                    + "For count with field '*', the alias is 'count'. "
                    + "When first is used with groupby, response contains: items (array), endCursor (string), hasNextPage (boolean). "
                    + "RULES: 1) ALWAYS call describe_entities first to get valid entity and field names. "
                    + "2) Use field '*' ONLY with function 'count'. "
                    + "3) For avg, sum, min, max: field MUST be a numeric field name from describe_entities. "
                    + "4) orderby, having, first, and after ONLY apply when groupby is provided. "
                    + "5) after REQUIRES first to also be set. "
                    + "6) Use first and after for paginating large grouped result sets.",
                InputSchema = JsonSerializer.Deserialize<JsonElement>(
                    @"{
                        ""type"": ""object"",
                        ""properties"": {
                            ""entity"": {
                                ""type"": ""string"",
                                ""description"": ""Exact entity name from describe_entities that has READ permission. Must match exactly (case-sensitive).""
                            },
                            ""function"": {
                                ""type"": ""string"",
                                ""enum"": [""count"", ""avg"", ""sum"", ""min"", ""max""],
                                ""description"": ""Aggregation function to apply. Use 'count' to count records, 'avg' for average, 'sum' for total, 'min' for minimum, 'max' for maximum. For count use field '*' or a specific field name. For avg, sum, min, max the field must be numeric.""
                            },
                            ""field"": {
                                ""type"": ""string"",
                                ""description"": ""Exact field name from describe_entities to aggregate. Use '*' ONLY with function 'count' to count all records. For avg, sum, min, max, provide a numeric field name.""
                            },
                            ""distinct"": {
                                ""type"": ""boolean"",
                                ""description"": ""When true, removes duplicate values before applying the aggregation function. For example, count with distinct counts unique values only. Default is false."",
                                ""default"": false
                            },
                            ""filter"": {
                                ""type"": ""string"",
                                ""description"": ""OData filter expression applied before aggregating (acts as a WHERE clause). Supported operators: eq, ne, gt, ge, lt, le, and, or, not. Example: 'unitPrice lt 10' filters to rows where unitPrice is less than 10 before aggregating. Example: 'discontinued eq true and categoryName eq ''Seafood''' filters discontinued seafood products."",
                                ""default"": """"
                            },
                            ""groupby"": {
                                ""type"": ""array"",
                                ""items"": { ""type"": ""string"" },
                                ""description"": ""Array of exact field names from describe_entities to group results by. Each unique combination of grouped field values produces one aggregated row. Grouped field values are included in the response alongside the aggregated value. Example: ['categoryName'] groups by category. Example: ['categoryName', 'region'] groups by both fields."",
                                ""default"": []
                            },
                            ""orderby"": {
                                ""type"": ""string"",
                                ""enum"": [""asc"", ""desc""],
                                ""description"": ""Sort direction for grouped results by the computed aggregated value. 'desc' returns highest values first, 'asc' returns lowest first. ONLY applies when groupby is provided. Default is 'desc'."",
                                ""default"": ""desc""
                            },
                            ""having"": {
                                ""type"": ""object"",
                                ""description"": ""Filter applied AFTER aggregating to filter grouped results by the computed aggregated value (acts as a HAVING clause). ONLY applies when groupby is provided. Multiple operators are AND-ed together. For example, use gt with value 20 to keep groups where the aggregated value exceeds 20. Combine gte and lte to define a range."",
                                ""properties"": {
                                    ""eq"":  { ""type"": ""number"", ""description"": ""Keep groups where the aggregated value equals this number."" },
                                    ""neq"": { ""type"": ""number"", ""description"": ""Keep groups where the aggregated value does not equal this number."" },
                                    ""gt"":  { ""type"": ""number"", ""description"": ""Keep groups where the aggregated value is greater than this number."" },
                                    ""gte"": { ""type"": ""number"", ""description"": ""Keep groups where the aggregated value is greater than or equal to this number."" },
                                    ""lt"":  { ""type"": ""number"", ""description"": ""Keep groups where the aggregated value is less than this number."" },
                                    ""lte"": { ""type"": ""number"", ""description"": ""Keep groups where the aggregated value is less than or equal to this number."" },
                                    ""in"":  {
                                        ""type"": ""array"",
                                        ""items"": { ""type"": ""number"" },
                                        ""description"": ""Keep groups where the aggregated value matches any number in this list. Example: [5, 10] keeps groups with aggregated value 5 or 10.""
                                    }
                                }
                            },
                            ""first"": {
                                ""type"": ""integer"",
                                ""description"": ""Maximum number of grouped results to return. Used for pagination of grouped results. ONLY applies when groupby is provided. Must be >= 1. When set, the response includes 'items', 'endCursor', and 'hasNextPage' fields for pagination."",
                                ""minimum"": 1
                            },
                            ""after"": {
                                ""type"": ""string"",
                                ""description"": ""Opaque cursor string for pagination. Pass the 'endCursor' value from a previous response to get the next page of results. REQUIRES both groupby and first to be set. Do not construct this value manually; always use the endCursor from a previous response.""
                            }
                        },
                        ""required"": [""entity"", ""function"", ""field""]
                    }"
                )
            };
        }

        public async Task<CallToolResult> ExecuteAsync(
            JsonDocument? arguments,
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken = default)
        {
            ILogger<AggregateRecordsTool>? logger = serviceProvider.GetService<ILogger<AggregateRecordsTool>>();
            string toolName = GetToolMetadata().Name;

            RuntimeConfigProvider runtimeConfigProvider = serviceProvider.GetRequiredService<RuntimeConfigProvider>();
            RuntimeConfig runtimeConfig = runtimeConfigProvider.GetConfig();

            if (runtimeConfig.McpDmlTools?.AggregateRecords is not true)
            {
                return McpErrorHelpers.ToolDisabled(toolName, logger);
            }

            string entityName = string.Empty;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (arguments == null)
                {
                    return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments", "No arguments provided.", logger);
                }

                JsonElement root = arguments.RootElement;

                // Parse required arguments
                if (!McpArgumentParser.TryParseEntity(root, out string parsedEntityName, out string parseError))
                {
                    return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments", parseError, logger);
                }

                entityName = parsedEntityName;

                if (runtimeConfig.Entities?.TryGetValue(entityName, out Entity? entity) == true &&
                    entity.Mcp?.DmlToolEnabled == false)
                {
                    return McpErrorHelpers.ToolDisabled(toolName, logger, $"DML tools are disabled for entity '{entityName}'.");
                }

                if (!root.TryGetProperty("function", out JsonElement funcEl) || string.IsNullOrWhiteSpace(funcEl.GetString()))
                {
                    return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments", "Missing required argument 'function'.", logger);
                }

                string function = funcEl.GetString()!.ToLowerInvariant();
                if (!_validFunctions.Contains(function))
                {
                    return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments", $"Invalid function '{function}'. Must be one of: count, avg, sum, min, max.", logger);
                }

                if (!root.TryGetProperty("field", out JsonElement fieldEl) || string.IsNullOrWhiteSpace(fieldEl.GetString()))
                {
                    return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments", "Missing required argument 'field'.", logger);
                }

                string field = fieldEl.GetString()!;
                bool distinct = root.TryGetProperty("distinct", out JsonElement distinctEl) && distinctEl.GetBoolean();
                string? filter = root.TryGetProperty("filter", out JsonElement filterEl) ? filterEl.GetString() : null;
                string orderby = root.TryGetProperty("orderby", out JsonElement orderbyEl) ? (orderbyEl.GetString() ?? "desc") : "desc";

                int? first = null;
                if (root.TryGetProperty("first", out JsonElement firstEl) && firstEl.ValueKind == JsonValueKind.Number)
                {
                    first = firstEl.GetInt32();
                    if (first < 1)
                    {
                        return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments", "Argument 'first' must be at least 1.", logger);
                    }
                }

                string? after = root.TryGetProperty("after", out JsonElement afterEl) ? afterEl.GetString() : null;

                List<string> groupby = new();
                if (root.TryGetProperty("groupby", out JsonElement groupbyEl) && groupbyEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement g in groupbyEl.EnumerateArray())
                    {
                        string? gVal = g.GetString();
                        if (!string.IsNullOrWhiteSpace(gVal))
                        {
                            groupby.Add(gVal);
                        }
                    }
                }

                Dictionary<string, double>? havingOps = null;
                List<double>? havingIn = null;
                if (root.TryGetProperty("having", out JsonElement havingEl) && havingEl.ValueKind == JsonValueKind.Object)
                {
                    havingOps = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                    foreach (JsonProperty prop in havingEl.EnumerateObject())
                    {
                        if (prop.Name.Equals("in", StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.Array)
                        {
                            havingIn = new List<double>();
                            foreach (JsonElement item in prop.Value.EnumerateArray())
                            {
                                havingIn.Add(item.GetDouble());
                            }
                        }
                        else if (prop.Value.ValueKind == JsonValueKind.Number)
                        {
                            havingOps[prop.Name] = prop.Value.GetDouble();
                        }
                    }
                }

                // Resolve metadata
                if (!McpMetadataHelper.TryResolveMetadata(
                        entityName,
                        runtimeConfig,
                        serviceProvider,
                        out ISqlMetadataProvider sqlMetadataProvider,
                        out DatabaseObject dbObject,
                        out string dataSourceName,
                        out string metadataError))
                {
                    return McpResponseBuilder.BuildErrorResult(toolName, "EntityNotFound", metadataError, logger);
                }

                // Authorization
                IAuthorizationResolver authResolver = serviceProvider.GetRequiredService<IAuthorizationResolver>();
                IAuthorizationService authorizationService = serviceProvider.GetRequiredService<IAuthorizationService>();
                IHttpContextAccessor httpContextAccessor = serviceProvider.GetRequiredService<IHttpContextAccessor>();
                HttpContext? httpContext = httpContextAccessor.HttpContext;

                if (!McpAuthorizationHelper.ValidateRoleContext(httpContext, authResolver, out string roleCtxError))
                {
                    return McpErrorHelpers.PermissionDenied(toolName, entityName, "read", roleCtxError, logger);
                }

                if (!McpAuthorizationHelper.TryResolveAuthorizedRole(
                        httpContext!,
                        authResolver,
                        entityName,
                        EntityActionOperation.Read,
                        out string? effectiveRole,
                        out string readAuthError))
                {
                    string finalError = readAuthError.StartsWith("You do not have permission", StringComparison.OrdinalIgnoreCase)
                        ? $"You do not have permission to read records for entity '{entityName}'."
                        : readAuthError;
                    return McpErrorHelpers.PermissionDenied(toolName, entityName, "read", finalError, logger);
                }

                // Build select list: groupby fields + aggregation field
                List<string> selectFields = new(groupby);
                bool isCountStar = function == "count" && field == "*";
                if (!isCountStar && !selectFields.Contains(field, StringComparer.OrdinalIgnoreCase))
                {
                    selectFields.Add(field);
                }

                // Build and validate Find context
                RequestValidator requestValidator = new(serviceProvider.GetRequiredService<IMetadataProviderFactory>(), runtimeConfigProvider);
                FindRequestContext context = new(entityName, dbObject, true);
                httpContext!.Request.Method = "GET";

                requestValidator.ValidateEntity(entityName);

                if (selectFields.Count > 0)
                {
                    context.UpdateReturnFields(selectFields);
                }

                if (!string.IsNullOrWhiteSpace(filter))
                {
                    string filterQueryString = $"?{RequestParser.FILTER_URL}={filter}";
                    context.FilterClauseInUrl = sqlMetadataProvider.GetODataParser().GetFilterClause(filterQueryString, $"{context.EntityName}.{context.DatabaseObject.FullName}");
                }

                requestValidator.ValidateRequestContext(context);

                AuthorizationResult authorizationResult = await authorizationService.AuthorizeAsync(
                    user: httpContext.User,
                    resource: context,
                    requirements: new[] { new ColumnsPermissionsRequirement() });
                if (!authorizationResult.Succeeded)
                {
                    return McpErrorHelpers.PermissionDenied(toolName, entityName, "read", DataApiBuilderException.AUTHORIZATION_FAILURE, logger);
                }

                // Execute query to get records
                IQueryEngineFactory queryEngineFactory = serviceProvider.GetRequiredService<IQueryEngineFactory>();
                IQueryEngine queryEngine = queryEngineFactory.GetQueryEngine(sqlMetadataProvider.GetDatabaseType());
                JsonDocument? queryResult = await queryEngine.ExecuteAsync(context);

                IActionResult actionResult = queryResult is null
                    ? SqlResponseHelpers.FormatFindResult(JsonDocument.Parse("[]").RootElement.Clone(), context, sqlMetadataProvider, runtimeConfig, httpContext, true)
                    : SqlResponseHelpers.FormatFindResult(queryResult.RootElement.Clone(), context, sqlMetadataProvider, runtimeConfig, httpContext, true);

                string rawPayloadJson = McpResponseBuilder.ExtractResultJson(actionResult);
                using JsonDocument resultDoc = JsonDocument.Parse(rawPayloadJson);
                JsonElement resultRoot = resultDoc.RootElement;

                // Extract the records array from the response
                JsonElement records;
                if (resultRoot.TryGetProperty("value", out JsonElement valueArray))
                {
                    records = valueArray;
                }
                else if (resultRoot.ValueKind == JsonValueKind.Array)
                {
                    records = resultRoot;
                }
                else
                {
                    records = resultRoot;
                }

                // Compute alias for the response
                string alias = ComputeAlias(function, field);

                // Perform in-memory aggregation
                List<Dictionary<string, object?>> aggregatedResults = PerformAggregation(
                    records, function, field, distinct, groupby, havingOps, havingIn, orderby, alias);

                // Apply pagination if first is specified with groupby
                if (first.HasValue && groupby.Count > 0)
                {
                    PaginationResult paginatedResult = ApplyPagination(aggregatedResults, first.Value, after);
                    return McpResponseBuilder.BuildSuccessResult(
                        new Dictionary<string, object?>
                        {
                            ["entity"] = entityName,
                            ["result"] = new Dictionary<string, object?>
                            {
                                ["items"] = paginatedResult.Items,
                                ["endCursor"] = paginatedResult.EndCursor,
                                ["hasNextPage"] = paginatedResult.HasNextPage
                            },
                            ["message"] = $"Successfully aggregated records for entity '{entityName}'"
                        },
                        logger,
                        $"AggregateRecordsTool success for entity {entityName}.");
                }

                return McpResponseBuilder.BuildSuccessResult(
                    new Dictionary<string, object?>
                    {
                        ["entity"] = entityName,
                        ["result"] = aggregatedResults,
                        ["message"] = $"Successfully aggregated records for entity '{entityName}'"
                    },
                    logger,
                    $"AggregateRecordsTool success for entity {entityName}.");
            }
            catch (TimeoutException timeoutEx)
            {
                logger?.LogError(timeoutEx, "Aggregation operation timed out for entity {Entity}.", entityName);
                return McpResponseBuilder.BuildErrorResult(
                    toolName,
                    "TimeoutError",
                    $"The aggregation query for entity '{entityName}' timed out. "
                    + "This is NOT a tool error. The database did not respond in time. "
                    + "This may occur with large datasets or complex aggregations. "
                    + "Try narrowing results with a 'filter', reducing 'groupby' fields, or adding 'first' for pagination.",
                    logger);
            }
            catch (TaskCanceledException taskEx)
            {
                logger?.LogError(taskEx, "Aggregation task was canceled for entity {Entity}.", entityName);
                return McpResponseBuilder.BuildErrorResult(
                    toolName,
                    "TimeoutError",
                    $"The aggregation query for entity '{entityName}' was canceled, likely due to a timeout. "
                    + "This is NOT a tool error. The database did not respond in time. "
                    + "Try narrowing results with a 'filter', reducing 'groupby' fields, or adding 'first' for pagination.",
                    logger);
            }
            catch (OperationCanceledException)
            {
                logger?.LogWarning("Aggregation operation was canceled for entity {Entity}.", entityName);
                return McpResponseBuilder.BuildErrorResult(
                    toolName,
                    "OperationCanceled",
                    $"The aggregation query for entity '{entityName}' was canceled before completion. "
                    + "This is NOT a tool error. The operation was interrupted, possibly due to a timeout or client disconnect. "
                    + "No results were returned. You may retry the same request.",
                    logger);
            }
            catch (DbException dbEx)
            {
                logger?.LogError(dbEx, "Database error during aggregation for entity {Entity}.", entityName);
                return McpResponseBuilder.BuildErrorResult(toolName, "DatabaseOperationFailed", dbEx.Message, logger);
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
                logger?.LogError(ex, "Unexpected error in AggregateRecordsTool.");
                return McpResponseBuilder.BuildErrorResult(toolName, "UnexpectedError", "Unexpected error occurred in AggregateRecordsTool.", logger);
            }
        }

        /// <summary>
        /// Computes the response alias for the aggregation result.
        /// For count with "*", the alias is "count". Otherwise it's "{function}_{field}".
        /// </summary>
        internal static string ComputeAlias(string function, string field)
        {
            if (function == "count" && field == "*")
            {
                return "count";
            }

            return $"{function}_{field}";
        }

        /// <summary>
        /// Performs in-memory aggregation over a JSON array of records.
        /// </summary>
        internal static List<Dictionary<string, object?>> PerformAggregation(
            JsonElement records,
            string function,
            string field,
            bool distinct,
            List<string> groupby,
            Dictionary<string, double>? havingOps,
            List<double>? havingIn,
            string orderby,
            string alias)
        {
            if (records.ValueKind != JsonValueKind.Array)
            {
                return new List<Dictionary<string, object?>> { new() { [alias] = null } };
            }

            bool isCountStar = function == "count" && field == "*";

            if (groupby.Count == 0)
            {
                // No groupby - single result
                List<JsonElement> items = new();
                foreach (JsonElement record in records.EnumerateArray())
                {
                    items.Add(record);
                }

                double? aggregatedValue = ComputeAggregateValue(items, function, field, distinct, isCountStar);

                // Apply having
                if (!PassesHavingFilter(aggregatedValue, havingOps, havingIn))
                {
                    return new List<Dictionary<string, object?>>();
                }

                return new List<Dictionary<string, object?>>
                {
                    new() { [alias] = aggregatedValue }
                };
            }
            else
            {
                // Group by
                Dictionary<string, List<JsonElement>> groups = new();
                Dictionary<string, Dictionary<string, object?>> groupKeys = new();

                foreach (JsonElement record in records.EnumerateArray())
                {
                    string key = BuildGroupKey(record, groupby);
                    if (!groups.ContainsKey(key))
                    {
                        groups[key] = new List<JsonElement>();
                        groupKeys[key] = ExtractGroupFields(record, groupby);
                    }

                    groups[key].Add(record);
                }

                List<Dictionary<string, object?>> results = new();
                foreach (KeyValuePair<string, List<JsonElement>> group in groups)
                {
                    double? aggregatedValue = ComputeAggregateValue(group.Value, function, field, distinct, isCountStar);

                    if (!PassesHavingFilter(aggregatedValue, havingOps, havingIn))
                    {
                        continue;
                    }

                    Dictionary<string, object?> row = new(groupKeys[group.Key])
                    {
                        [alias] = aggregatedValue
                    };
                    results.Add(row);
                }

                // Apply orderby
                if (orderby.Equals("asc", StringComparison.OrdinalIgnoreCase))
                {
                    results.Sort((a, b) => CompareNullableDoubles(a[alias] as double?, b[alias] as double?));
                }
                else
                {
                    results.Sort((a, b) => CompareNullableDoubles(b[alias] as double?, a[alias] as double?));
                }

                return results;
            }
        }

        /// <summary>
        /// Represents the result of applying pagination to aggregated results.
        /// </summary>
        internal sealed class PaginationResult
        {
            public List<Dictionary<string, object?>> Items { get; set; } = new();
            public string? EndCursor { get; set; }
            public bool HasNextPage { get; set; }
        }

        /// <summary>
        /// Applies cursor-based pagination to aggregated results.
        /// The cursor is an opaque base64-encoded offset integer.
        /// </summary>
        internal static PaginationResult ApplyPagination(
            List<Dictionary<string, object?>> allResults,
            int first,
            string? after)
        {
            int startIndex = 0;

            if (!string.IsNullOrWhiteSpace(after))
            {
                try
                {
                    byte[] bytes = Convert.FromBase64String(after);
                    string decoded = System.Text.Encoding.UTF8.GetString(bytes);
                    if (int.TryParse(decoded, out int cursorOffset))
                    {
                        startIndex = cursorOffset;
                    }
                }
                catch (FormatException)
                {
                    // Invalid cursor format; start from beginning
                }
            }

            List<Dictionary<string, object?>> pageItems = allResults
                .Skip(startIndex)
                .Take(first)
                .ToList();

            bool hasNextPage = startIndex + first < allResults.Count;
            string? endCursor = null;

            if (pageItems.Count > 0)
            {
                int lastItemIndex = startIndex + pageItems.Count;
                endCursor = Convert.ToBase64String(
                    System.Text.Encoding.UTF8.GetBytes(lastItemIndex.ToString()));
            }

            return new PaginationResult
            {
                Items = pageItems,
                EndCursor = endCursor,
                HasNextPage = hasNextPage
            };
        }

        private static double? ComputeAggregateValue(List<JsonElement> records, string function, string field, bool distinct, bool isCountStar)
        {
            if (isCountStar)
            {
                return distinct ? 0 : records.Count;
            }

            List<double> values = new();
            foreach (JsonElement record in records)
            {
                if (record.TryGetProperty(field, out JsonElement val) && val.ValueKind == JsonValueKind.Number)
                {
                    values.Add(val.GetDouble());
                }
            }

            if (distinct)
            {
                values = values.Distinct().ToList();
            }

            if (function == "count")
            {
                return values.Count;
            }

            if (values.Count == 0)
            {
                return null;
            }

            return function switch
            {
                "avg" => Math.Round(values.Average(), 2),
                "sum" => values.Sum(),
                "min" => values.Min(),
                "max" => values.Max(),
                _ => null
            };
        }

        private static bool PassesHavingFilter(double? value, Dictionary<string, double>? havingOps, List<double>? havingIn)
        {
            if (havingOps == null && havingIn == null)
            {
                return true;
            }

            if (value == null)
            {
                return false;
            }

            double v = value.Value;

            if (havingOps != null)
            {
                foreach (KeyValuePair<string, double> op in havingOps)
                {
                    bool passes = op.Key.ToLowerInvariant() switch
                    {
                        "eq" => v == op.Value,
                        "neq" => v != op.Value,
                        "gt" => v > op.Value,
                        "gte" => v >= op.Value,
                        "lt" => v < op.Value,
                        "lte" => v <= op.Value,
                        _ => true
                    };

                    if (!passes)
                    {
                        return false;
                    }
                }
            }

            if (havingIn != null && !havingIn.Contains(v))
            {
                return false;
            }

            return true;
        }

        private static string BuildGroupKey(JsonElement record, List<string> groupby)
        {
            List<string> parts = new();
            foreach (string g in groupby)
            {
                if (record.TryGetProperty(g, out JsonElement val))
                {
                    parts.Add(val.ToString());
                }
                else
                {
                    parts.Add("__null__");
                }
            }

            // Use null character (\0) as delimiter to avoid collisions with
            // field values that may contain printable characters like '|'.
            return string.Join("\0", parts);
        }

        private static Dictionary<string, object?> ExtractGroupFields(JsonElement record, List<string> groupby)
        {
            Dictionary<string, object?> result = new();
            foreach (string g in groupby)
            {
                if (record.TryGetProperty(g, out JsonElement val))
                {
                    result[g] = McpResponseBuilder.GetJsonValue(val);
                }
                else
                {
                    result[g] = null;
                }
            }

            return result;
        }

        private static int CompareNullableDoubles(double? a, double? b)
        {
            if (a == null && b == null)
            {
                return 0;
            }

            if (a == null)
            {
                return -1;
            }

            if (b == null)
            {
                return 1;
            }

            return a.Value.CompareTo(b.Value);
        }
    }
}
