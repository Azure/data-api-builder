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

        private static readonly HashSet<string> ValidFunctions = new(StringComparer.OrdinalIgnoreCase) { "count", "avg", "sum", "min", "max" };

        public Tool GetToolMetadata()
        {
            return new Tool
            {
                Name = "aggregate_records",
                Description = "STEP 1: describe_entities -> find entities with READ permission and their fields. STEP 2: call this tool to compute aggregations (count, avg, sum, min, max) with optional filter, groupby, having, and orderby.",
                InputSchema = JsonSerializer.Deserialize<JsonElement>(
                    @"{
                        ""type"": ""object"",
                        ""properties"": {
                            ""entity"": {
                                ""type"": ""string"",
                                ""description"": ""Entity name with READ permission.""
                            },
                            ""function"": {
                                ""type"": ""string"",
                                ""enum"": [""count"", ""avg"", ""sum"", ""min"", ""max""],
                                ""description"": ""Aggregation function to apply.""
                            },
                            ""field"": {
                                ""type"": ""string"",
                                ""description"": ""Field to aggregate. Use '*' for count.""
                            },
                            ""distinct"": {
                                ""type"": ""boolean"",
                                ""description"": ""Apply DISTINCT before aggregating."",
                                ""default"": false
                            },
                            ""filter"": {
                                ""type"": ""string"",
                                ""description"": ""OData filter applied before aggregating (WHERE). Example: 'unitPrice lt 10'"",
                                ""default"": """"
                            },
                            ""groupby"": {
                                ""type"": ""array"",
                                ""items"": { ""type"": ""string"" },
                                ""description"": ""Fields to group by, e.g., ['category', 'region']. Grouped field values are included in the response."",
                                ""default"": []
                            },
                            ""orderby"": {
                                ""type"": ""string"",
                                ""enum"": [""asc"", ""desc""],
                                ""description"": ""Sort aggregated results by the computed value. Only applies with groupby."",
                                ""default"": ""desc""
                            },
                            ""having"": {
                                ""type"": ""object"",
                                ""description"": ""Filter applied after aggregating on the result (HAVING). Operators are AND-ed together."",
                                ""properties"": {
                                    ""eq"":  { ""type"": ""number"", ""description"": ""Aggregated value equals."" },
                                    ""neq"": { ""type"": ""number"", ""description"": ""Aggregated value not equals."" },
                                    ""gt"":  { ""type"": ""number"", ""description"": ""Aggregated value greater than."" },
                                    ""gte"": { ""type"": ""number"", ""description"": ""Aggregated value greater than or equal."" },
                                    ""lt"":  { ""type"": ""number"", ""description"": ""Aggregated value less than."" },
                                    ""lte"": { ""type"": ""number"", ""description"": ""Aggregated value less than or equal."" },
                                    ""in"":  {
                                        ""type"": ""array"",
                                        ""items"": { ""type"": ""number"" },
                                        ""description"": ""Aggregated value is in the given list.""
                                    }
                                }
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

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (arguments == null)
                {
                    return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments", "No arguments provided.", logger);
                }

                JsonElement root = arguments.RootElement;

                // Parse required arguments
                if (!McpArgumentParser.TryParseEntity(root, out string entityName, out string parseError))
                {
                    return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments", parseError, logger);
                }

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
                if (!ValidFunctions.Contains(function))
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
            catch (OperationCanceledException)
            {
                return McpResponseBuilder.BuildErrorResult(toolName, "OperationCanceled", "The aggregate operation was canceled.", logger);
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

            return string.Join("|", parts);
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
