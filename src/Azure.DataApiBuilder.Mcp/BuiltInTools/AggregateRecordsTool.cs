// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data.Common;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using static Azure.DataApiBuilder.Mcp.Model.McpEnums;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.Sql.SchemaConverter;

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
                    + "after (opaque cursor string from a previous response's endCursor for pagination). "
                    + "RESPONSE: The aggregated value is aliased as '{function}_{field}' (e.g. avg_unitPrice, sum_revenue). "
                    + "For count with field '*', the alias is 'count'. "
                    + "When first is used with groupby, response contains: items (array), endCursor (string), hasNextPage (boolean). "
                    + "RULES: 1) ALWAYS call describe_entities first to get valid entity and field names. "
                    + "2) Use field '*' ONLY with function 'count'. "
                    + "3) For avg, sum, min, max: field MUST be a numeric field name from describe_entities. "
                    + "4) orderby, having, and first ONLY apply when groupby is provided. "
                    + "5) Use first and after for paginating large grouped result sets.",
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

                // Validate field/function compatibility
                bool isCountStar = function == "count" && field == "*";

                if (field == "*" && function != "count")
                {
                    return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments",
                        $"Field '*' is only valid with function 'count'. For function '{function}', provide a specific field name.", logger);
                }

                bool distinct = root.TryGetProperty("distinct", out JsonElement distinctEl) && distinctEl.GetBoolean();

                // Reject count(*) with distinct as it is semantically undefined
                if (isCountStar && distinct)
                {
                    return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments",
                        "Cannot use distinct=true with field='*'. DISTINCT requires a specific field name. Use a field name instead of '*' to count distinct values.", logger);
                }

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

                    if (first > 100_000)
                    {
                        return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments", "Argument 'first' must not exceed 100000.", logger);
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

                // Build select list for authorization: groupby fields + aggregation field
                List<string> selectFields = new(groupby);
                if (!isCountStar && !selectFields.Contains(field, StringComparer.OrdinalIgnoreCase))
                {
                    selectFields.Add(field);
                }

                // Build and validate Find context (reuse for authorization and OData filter parsing)
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

                // Build SqlQueryStructure to get OData filter → SQL predicate translation and DB policies
                GQLFilterParser gQLFilterParser = serviceProvider.GetRequiredService<GQLFilterParser>();
                SqlQueryStructure structure = new(
                    context, sqlMetadataProvider, authResolver, runtimeConfigProvider, gQLFilterParser, httpContext);

                // Get database-specific components
                DatabaseType databaseType = runtimeConfig.GetDataSourceFromDataSourceName(dataSourceName).DatabaseType;
                IAbstractQueryManagerFactory queryManagerFactory = serviceProvider.GetRequiredService<IAbstractQueryManagerFactory>();
                IQueryBuilder queryBuilder = queryManagerFactory.GetQueryBuilder(databaseType);
                IQueryExecutor queryExecutor = queryManagerFactory.GetQueryExecutor(databaseType);

                // Resolve backing column name for the aggregation field
                string? backingField = null;
                if (!isCountStar)
                {
                    if (!sqlMetadataProvider.TryGetBackingColumn(entityName, field, out backingField))
                    {
                        return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments",
                            $"Field '{field}' not found for entity '{entityName}'.", logger);
                    }
                }

                // Resolve backing column names for groupby fields
                List<(string entityField, string backingCol)> groupbyMapping = new();
                foreach (string gField in groupby)
                {
                    if (!sqlMetadataProvider.TryGetBackingColumn(entityName, gField, out string? backingGCol))
                    {
                        return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments",
                            $"GroupBy field '{gField}' not found for entity '{entityName}'.", logger);
                    }

                    groupbyMapping.Add((gField, backingGCol));
                }

                string alias = ComputeAlias(function, field);

                // Clear default columns from FindRequestContext
                structure.Columns.Clear();

                // Add groupby columns as LabelledColumns and GroupByMetadata.Fields
                foreach (var (entityField, backingCol) in groupbyMapping)
                {
                    structure.Columns.Add(new LabelledColumn(
                        dbObject.SchemaName, dbObject.Name, backingCol, entityField, structure.SourceAlias));
                    structure.GroupByMetadata.Fields[backingCol] = new Column(
                        dbObject.SchemaName, dbObject.Name, backingCol, structure.SourceAlias);
                }

                // Build aggregation column using engine's AggregationColumn type
                AggregationType aggType = Enum.Parse<AggregationType>(function);
                AggregationColumn aggColumn = isCountStar
                    ? new AggregationColumn("", "", "*", AggregationType.count, alias, false)
                    : new AggregationColumn(dbObject.SchemaName, dbObject.Name, backingField!, aggType, alias, distinct, structure.SourceAlias);

                // Build HAVING predicates using engine's Predicate model
                List<Predicate> havingPredicates = new();
                if (havingOps != null)
                {
                    foreach (var op in havingOps)
                    {
                        PredicateOperation predOp = op.Key.ToLowerInvariant() switch
                        {
                            "eq" => PredicateOperation.Equal,
                            "neq" => PredicateOperation.NotEqual,
                            "gt" => PredicateOperation.GreaterThan,
                            "gte" => PredicateOperation.GreaterThanOrEqual,
                            "lt" => PredicateOperation.LessThan,
                            "lte" => PredicateOperation.LessThanOrEqual,
                            _ => throw new ArgumentException($"Invalid having operator: {op.Key}")
                        };
                        string paramName = BaseQueryStructure.GetEncodedParamName(structure.Counter.Next());
                        structure.Parameters.Add(paramName, new DbConnectionParam(op.Value));
                        havingPredicates.Add(new Predicate(
                            new PredicateOperand(aggColumn),
                            predOp,
                            new PredicateOperand(paramName)));
                    }
                }

                if (havingIn != null && havingIn.Count > 0)
                {
                    List<string> inParams = new();
                    foreach (double val in havingIn)
                    {
                        string paramName = BaseQueryStructure.GetEncodedParamName(structure.Counter.Next());
                        structure.Parameters.Add(paramName, new DbConnectionParam(val));
                        inParams.Add(paramName);
                    }

                    havingPredicates.Add(new Predicate(
                        new PredicateOperand(aggColumn),
                        PredicateOperation.IN,
                        new PredicateOperand($"({string.Join(", ", inParams)})")));
                }

                // Combine multiple HAVING predicates with AND
                Predicate? combinedHaving = null;
                foreach (var pred in havingPredicates)
                {
                    combinedHaving = combinedHaving == null
                        ? pred
                        : new Predicate(new PredicateOperand(combinedHaving), PredicateOperation.AND, new PredicateOperand(pred));
                }

                structure.GroupByMetadata.Aggregations.Add(
                    new AggregationOperation(aggColumn, having: combinedHaving != null ? new List<Predicate> { combinedHaving } : null));
                structure.GroupByMetadata.RequestedAggregations = true;

                // Clear default OrderByColumns (PK-based)
                structure.OrderByColumns.Clear();

                // Set pagination limit if using first
                if (first.HasValue && groupbyMapping.Count > 0)
                {
                    structure.IsListQuery = true;
                }

                // Use engine's query builder to generate SQL
                string sql = queryBuilder.Build(structure);

                // For groupby queries: add ORDER BY aggregate expression before FOR JSON PATH
                if (groupbyMapping.Count > 0)
                {
                    string direction = orderby.Equals("asc", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";
                    string orderByAggExpr = isCountStar
                        ? "COUNT(*)"
                        : distinct
                            ? $"{function.ToUpperInvariant()}(DISTINCT {queryBuilder.QuoteIdentifier(structure.SourceAlias)}.{queryBuilder.QuoteIdentifier(backingField!)})"
                            : $"{function.ToUpperInvariant()}({queryBuilder.QuoteIdentifier(structure.SourceAlias)}.{queryBuilder.QuoteIdentifier(backingField!)})";
                    string orderByClause = $" ORDER BY {orderByAggExpr} {direction}";

                    // Insert ORDER BY before FOR JSON PATH (MsSql/DWSQL) or before LIMIT (PG/MySQL)
                    int insertIdx = sql.IndexOf(" FOR JSON PATH", StringComparison.OrdinalIgnoreCase);
                    if (insertIdx < 0)
                    {
                        insertIdx = sql.IndexOf(" LIMIT ", StringComparison.OrdinalIgnoreCase);
                    }

                    if (insertIdx > 0)
                    {
                        sql = sql.Insert(insertIdx, orderByClause);
                    }
                    else
                    {
                        sql += orderByClause;
                    }

                    // Add pagination (OFFSET/FETCH or LIMIT/OFFSET) for grouped results
                    if (first.HasValue)
                    {
                        int offset = DecodeCursorOffset(after);
                        int fetchCount = first.Value + 1;
                        string offsetParam = BaseQueryStructure.GetEncodedParamName(structure.Counter.Next());
                        structure.Parameters.Add(offsetParam, new DbConnectionParam(offset));
                        string limitParam = BaseQueryStructure.GetEncodedParamName(structure.Counter.Next());
                        structure.Parameters.Add(limitParam, new DbConnectionParam(fetchCount));

                        int paginationIdx = sql.IndexOf(" FOR JSON PATH", StringComparison.OrdinalIgnoreCase);
                        string paginationClause;
                        if (databaseType == DatabaseType.MSSQL || databaseType == DatabaseType.DWSQL)
                        {
                            paginationClause = $" OFFSET {offsetParam} ROWS FETCH NEXT {limitParam} ROWS ONLY";
                        }
                        else
                        {
                            paginationClause = $" LIMIT {limitParam} OFFSET {offsetParam}";
                        }

                        if (paginationIdx > 0)
                        {
                            sql = sql.Insert(paginationIdx, paginationClause);
                        }
                        else
                        {
                            sql += paginationClause;
                        }
                    }
                }

                // Execute the SQL aggregate query against the database
                cancellationToken.ThrowIfCancellationRequested();
                JsonDocument? queryResult = await queryExecutor.ExecuteQueryAsync(
                    sql,
                    structure.Parameters,
                    queryExecutor.GetJsonResultAsync<JsonDocument>,
                    dataSourceName,
                    httpContext);

                // Parse result
                JsonArray? resultArray = null;
                if (queryResult != null)
                {
                    resultArray = JsonSerializer.Deserialize<JsonArray>(queryResult.RootElement.GetRawText());
                }

                // Format and return results
                if (first.HasValue && groupby.Count > 0)
                {
                    return BuildPaginatedResponse(resultArray, first.Value, after, entityName, logger);
                }

                return BuildSimpleResponse(resultArray, entityName, alias, logger);
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
        /// Decodes a base64-encoded cursor string to an integer offset.
        /// Returns 0 if the cursor is null, empty, or invalid.
        /// </summary>
        internal static int DecodeCursorOffset(string? after)
        {
            if (string.IsNullOrWhiteSpace(after))
            {
                return 0;
            }

            try
            {
                byte[] bytes = Convert.FromBase64String(after);
                string decoded = Encoding.UTF8.GetString(bytes);
                return int.TryParse(decoded, out int cursorOffset) && cursorOffset >= 0 ? cursorOffset : 0;
            }
            catch (FormatException)
            {
                return 0;
            }
        }

        /// <summary>
        /// Builds the paginated response from a SQL result that fetched first+1 rows.
        /// </summary>
        private static CallToolResult BuildPaginatedResponse(
            JsonArray? resultArray, int first, string? after, string entityName, ILogger? logger)
        {
            int startOffset = DecodeCursorOffset(after);
            int actualCount = resultArray?.Count ?? 0;
            bool hasNextPage = actualCount > first;
            int returnCount = hasNextPage ? first : actualCount;

            // Build page items from the SQL result
            JsonArray pageItems = new();
            for (int i = 0; i < returnCount && resultArray != null && i < resultArray.Count; i++)
            {
                pageItems.Add(resultArray[i]?.DeepClone());
            }

            string? endCursor = null;
            if (returnCount > 0)
            {
                int lastItemIndex = startOffset + returnCount;
                endCursor = Convert.ToBase64String(Encoding.UTF8.GetBytes(lastItemIndex.ToString()));
            }

            JsonElement itemsElement = JsonSerializer.Deserialize<JsonElement>(pageItems.ToJsonString());

            return McpResponseBuilder.BuildSuccessResult(
                new Dictionary<string, object?>
                {
                    ["entity"] = entityName,
                    ["result"] = new Dictionary<string, object?>
                    {
                        ["items"] = itemsElement,
                        ["endCursor"] = endCursor,
                        ["hasNextPage"] = hasNextPage
                    },
                    ["message"] = $"Successfully aggregated records for entity '{entityName}'"
                },
                logger,
                $"AggregateRecordsTool success for entity {entityName}.");
        }

        /// <summary>
        /// Builds the simple (non-paginated) response from a SQL result.
        /// </summary>
        private static CallToolResult BuildSimpleResponse(
            JsonArray? resultArray, string entityName, string alias, ILogger? logger)
        {
            JsonElement resultElement;
            if (resultArray == null || resultArray.Count == 0)
            {
                // For non-grouped aggregate with no results, return null value
                JsonArray nullArray = new() { new JsonObject { [alias] = null } };
                resultElement = JsonSerializer.Deserialize<JsonElement>(nullArray.ToJsonString());
            }
            else
            {
                resultElement = JsonSerializer.Deserialize<JsonElement>(resultArray.ToJsonString());
            }

            return McpResponseBuilder.BuildSuccessResult(
                new Dictionary<string, object?>
                {
                    ["entity"] = entityName,
                    ["result"] = resultElement,
                    ["message"] = $"Successfully aggregated records for entity '{entityName}'"
                },
                logger,
                $"AggregateRecordsTool success for entity {entityName}.");
        }
    }
}
