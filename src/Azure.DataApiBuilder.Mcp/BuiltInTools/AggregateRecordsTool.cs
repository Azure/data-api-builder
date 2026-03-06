// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data.Common;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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
        private static readonly HashSet<string> _validHavingOperators = new(StringComparer.OrdinalIgnoreCase) { "eq", "neq", "gt", "gte", "lt", "lte", "in" };

        private static readonly Tool _cachedToolMetadata = new()
        {
            Name = "aggregate_records",
            Description = "Computes aggregations (count, avg, sum, min, max) on entity data. "
                + "WORKFLOW: 1) Call describe_entities first to get entity names and field names. "
                + "2) Call this tool with entity, function, and field from step 1. "
                + "RULES: field '*' is ONLY valid with count. "
                + "orderby, having, first, and after ONLY apply when groupby is provided. "
                + "RESPONSE: Result is aliased as '{function}_{field}' (e.g. avg_unitPrice). "
                + "For count(*), the alias is 'count'. "
                + "With groupby and first, response includes items, endCursor, and hasNextPage for pagination.",
            InputSchema = JsonSerializer.Deserialize<JsonElement>(
                @"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""entity"": {
                            ""type"": ""string"",
                            ""description"": ""Entity name from describe_entities with READ permission (case-sensitive).""
                        },
                        ""function"": {
                            ""type"": ""string"",
                            ""enum"": [""count"", ""avg"", ""sum"", ""min"", ""max""],
                            ""description"": ""Aggregation function. count supports field '*'; avg, sum, min, max require a numeric field.""
                        },
                        ""field"": {
                            ""type"": ""string"",
                            ""description"": ""Field name to aggregate, or '*' with count to count all rows.""
                        },
                        ""distinct"": {
                            ""type"": ""boolean"",
                            ""description"": ""Remove duplicate values before aggregating. Not valid with field '*'."",
                            ""default"": false
                        },
                        ""filter"": {
                            ""type"": ""string"",
                            ""description"": ""OData WHERE clause applied before aggregating. Operators: eq, ne, gt, ge, lt, le, and, or, not. Example: 'unitPrice lt 10'."",
                            ""default"": """"
                        },
                        ""groupby"": {
                            ""type"": ""array"",
                            ""items"": { ""type"": ""string"" },
                            ""description"": ""Field names to group by. Each unique combination produces one aggregated row. Enables orderby, having, first, and after."",
                            ""default"": []
                        },
                        ""orderby"": {
                            ""type"": ""string"",
                            ""enum"": [""asc"", ""desc""],
                            ""description"": ""Sort grouped results by the aggregated value. Requires groupby."",
                            ""default"": ""desc""
                        },
                        ""having"": {
                            ""type"": ""object"",
                            ""description"": ""Filter groups by the aggregated value (HAVING clause). Requires groupby. Multiple operators are AND-ed."",
                            ""properties"": {
                                ""eq"":  { ""type"": ""number"", ""description"": ""Equals."" },
                                ""neq"": { ""type"": ""number"", ""description"": ""Not equals."" },
                                ""gt"":  { ""type"": ""number"", ""description"": ""Greater than."" },
                                ""gte"": { ""type"": ""number"", ""description"": ""Greater than or equal."" },
                                ""lt"":  { ""type"": ""number"", ""description"": ""Less than."" },
                                ""lte"": { ""type"": ""number"", ""description"": ""Less than or equal."" },
                                ""in"":  {
                                    ""type"": ""array"",
                                    ""items"": { ""type"": ""number"" },
                                    ""description"": ""Matches any value in the list.""
                                }
                            }
                        },
                        ""first"": {
                            ""type"": ""integer"",
                            ""description"": ""Max grouped results to return. Requires groupby. Enables paginated response with endCursor and hasNextPage."",
                            ""minimum"": 1
                        },
                        ""after"": {
                            ""type"": ""string"",
                            ""description"": ""Opaque cursor from a previous endCursor for next-page retrieval. Requires groupby and first. Do not construct manually.""
                        }
                    },
                    ""required"": [""entity"", ""function"", ""field""]
                }"
            )
        };

        public Tool GetToolMetadata()
        {
            return _cachedToolMetadata;
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

                if (!root.TryGetProperty("function", out JsonElement functionElement) || string.IsNullOrWhiteSpace(functionElement.GetString()))
                {
                    return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments", "Missing required argument 'function'.", logger);
                }

                string function = functionElement.GetString()!.ToLowerInvariant();
                if (!_validFunctions.Contains(function))
                {
                    return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments", $"Invalid function '{function}'. Must be one of: count, avg, sum, min, max.", logger);
                }

                if (!root.TryGetProperty("field", out JsonElement fieldElement) || string.IsNullOrWhiteSpace(fieldElement.GetString()))
                {
                    return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments", "Missing required argument 'field'.", logger);
                }

                string field = fieldElement.GetString()!;

                // Validate field/function compatibility
                bool isCountStar = function == "count" && field == "*";

                if (field == "*" && function != "count")
                {
                    return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments",
                        $"Field '*' is only valid with function 'count'. For function '{function}', provide a specific field name.", logger);
                }

                bool distinct = root.TryGetProperty("distinct", out JsonElement distinctElement) && distinctElement.GetBoolean();

                // Reject count(*) with distinct as it is semantically undefined
                if (isCountStar && distinct)
                {
                    return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments",
                        "Cannot use distinct=true with field='*'. DISTINCT requires a specific field name. Use a field name instead of '*' to count distinct values.", logger);
                }

                string? filter = root.TryGetProperty("filter", out JsonElement filterElement) ? filterElement.GetString() : null;
                bool userProvidedOrderby = root.TryGetProperty("orderby", out JsonElement orderbyElement) && !string.IsNullOrWhiteSpace(orderbyElement.GetString());
                string orderby = "desc";
                if (userProvidedOrderby)
                {
                    string normalizedOrderby = (orderbyElement.GetString() ?? string.Empty).Trim().ToLowerInvariant();
                    if (normalizedOrderby != "asc" && normalizedOrderby != "desc")
                    {
                        return McpResponseBuilder.BuildErrorResult(
                            toolName,
                            "InvalidArguments",
                            $"Argument 'orderby' must be either 'asc' or 'desc' when provided. Got: '{orderbyElement.GetString()}'.",
                            logger);
                    }

                    orderby = normalizedOrderby;
                }

                int? first = null;
                if (root.TryGetProperty("first", out JsonElement firstElement) && firstElement.ValueKind == JsonValueKind.Number)
                {
                    first = firstElement.GetInt32();
                    if (first < 1)
                    {
                        return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments", "Argument 'first' must be at least 1.", logger);
                    }
                }

                string? after = root.TryGetProperty("after", out JsonElement afterElement) ? afterElement.GetString() : null;

                List<string> groupby = new();
                if (root.TryGetProperty("groupby", out JsonElement groupbyElement) && groupbyElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement groupbyItem in groupbyElement.EnumerateArray())
                    {
                        string? groupbyFieldName = groupbyItem.GetString();
                        if (!string.IsNullOrWhiteSpace(groupbyFieldName))
                        {
                            groupby.Add(groupbyFieldName);
                        }
                    }
                }

                // Validate that first, after, orderby, and having require groupby
                if (groupby.Count == 0)
                {
                    if (userProvidedOrderby)
                    {
                        return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments",
                            "The 'orderby' parameter requires 'groupby' to be specified. Sorting applies to grouped aggregation results.", logger);
                    }

                    if (first.HasValue)
                    {
                        return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments",
                            "The 'first' parameter requires 'groupby' to be specified. Pagination applies to grouped aggregation results.", logger);
                    }

                    if (!string.IsNullOrEmpty(after))
                    {
                        return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments",
                            "The 'after' parameter requires 'groupby' to be specified. Pagination applies to grouped aggregation results.", logger);
                    }
                }

                if (!string.IsNullOrEmpty(after) && !first.HasValue)
                {
                    return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments",
                        "The 'after' parameter requires 'first' to be specified. Provide 'first' to enable pagination.", logger);
                }

                Dictionary<string, double>? havingOperators = null;
                List<double>? havingInValues = null;
                if (root.TryGetProperty("having", out JsonElement havingElement) && havingElement.ValueKind == JsonValueKind.Object)
                {
                    if (groupby.Count == 0)
                    {
                        return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments",
                            "The 'having' parameter requires 'groupby' to be specified. HAVING filters groups after aggregation.", logger);
                    }

                    havingOperators = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                    foreach (JsonProperty prop in havingElement.EnumerateObject())
                    {
                        // Reject unsupported operators (e.g. between, notIn, like)
                        if (!_validHavingOperators.Contains(prop.Name))
                        {
                            return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments",
                                $"Unsupported having operator '{prop.Name}'. Supported operators: {string.Join(", ", _validHavingOperators)}.", logger);
                        }

                        if (prop.Name.Equals("in", StringComparison.OrdinalIgnoreCase))
                        {
                            if (prop.Value.ValueKind != JsonValueKind.Array)
                            {
                                return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments",
                                    "The 'having.in' value must be a numeric array. Example: {\"in\": [5, 10]}.", logger);
                            }

                            havingInValues = new List<double>();
                            foreach (JsonElement item in prop.Value.EnumerateArray())
                            {
                                if (item.ValueKind != JsonValueKind.Number)
                                {
                                    return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments",
                                        $"All values in 'having.in' must be numeric. Found non-numeric value: '{item}'.", logger);
                                }

                                havingInValues.Add(item.GetDouble());
                            }
                        }
                        else
                        {
                            // Scalar operators (eq, neq, gt, gte, lt, lte) must have numeric values
                            if (prop.Value.ValueKind != JsonValueKind.Number)
                            {
                                return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments",
                                    $"The 'having.{prop.Name}' value must be numeric. Got: '{prop.Value}'. HAVING filters compare aggregated numeric results.", logger);
                            }

                            havingOperators[prop.Name] = prop.Value.GetDouble();
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

                // Early field validation: check all user-supplied field names before authorization or query building.
                // This lets the model discover and fix typos immediately.
                if (!isCountStar)
                {
                    if (!sqlMetadataProvider.TryGetBackingColumn(entityName, field, out _))
                    {
                        return McpErrorHelpers.FieldNotFound(toolName, entityName, field, "field", logger);
                    }
                }

                foreach (string groupbyField in groupby)
                {
                    if (!sqlMetadataProvider.TryGetBackingColumn(entityName, groupbyField, out _))
                    {
                        return McpErrorHelpers.FieldNotFound(toolName, entityName, groupbyField, "groupby", logger);
                    }
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

                // Aggregation is only supported for tables and views, not stored procedures.
                if (dbObject.SourceType != EntitySourceType.Table && dbObject.SourceType != EntitySourceType.View)
                {
                    return McpResponseBuilder.BuildErrorResult(toolName, "InvalidEntity",
                        $"Entity '{entityName}' is not a table or view. Aggregation is not supported for stored procedures. Use 'execute_entity' for stored procedures.", logger);
                }

                // Aggregation is only supported for MsSql/DWSQL (matching engine's GraphQL aggregation support)
                if (databaseType != DatabaseType.MSSQL && databaseType != DatabaseType.DWSQL)
                {
                    return McpResponseBuilder.BuildErrorResult(toolName, "UnsupportedDatabase",
                        $"Aggregation is not supported for database type '{databaseType}'. Aggregation is only available for Azure SQL, SQL Server, and SQL Data Warehouse.", logger);
                }

                IAbstractQueryManagerFactory queryManagerFactory = serviceProvider.GetRequiredService<IAbstractQueryManagerFactory>();
                IQueryBuilder queryBuilder = queryManagerFactory.GetQueryBuilder(databaseType);
                IQueryExecutor queryExecutor = queryManagerFactory.GetQueryExecutor(databaseType);

                // Resolve backing column name for the aggregation field (already validated early)
                string? backingField = null;
                if (!isCountStar)
                {
                    sqlMetadataProvider.TryGetBackingColumn(entityName, field, out backingField);
                }
                else
                {
                    // For COUNT(*), use primary key column since PK is always NOT NULL,
                    // making COUNT(pk) equivalent to COUNT(*). The engine's Build(AggregationColumn)
                    // does not support "*" as a column name (it would produce invalid SQL like count([].[*])).
                    SourceDefinition sourceDefinition = sqlMetadataProvider.GetSourceDefinition(entityName);
                    if (sourceDefinition.PrimaryKey.Count == 0)
                    {
                        return McpResponseBuilder.BuildErrorResult(toolName, "InvalidEntity",
                            $"Entity '{entityName}' has no primary key defined. COUNT(*) requires at least one primary key column.", logger);
                    }

                    backingField = sourceDefinition.PrimaryKey[0];
                }

                // Resolve backing column names for groupby fields (already validated early)
                List<(string entityField, string backingColumn)> groupbyMapping = new();
                foreach (string groupbyField in groupby)
                {
                    sqlMetadataProvider.TryGetBackingColumn(entityName, groupbyField, out string? backingGroupbyColumn);
                    groupbyMapping.Add((groupbyField, backingGroupbyColumn!));
                }

                string alias = ComputeAlias(function, field);

                // Clear default columns from FindRequestContext
                structure.Columns.Clear();

                // Add groupby columns as LabelledColumns and GroupByMetadata.Fields
                foreach (var (entityField, backingColumn) in groupbyMapping)
                {
                    structure.Columns.Add(new LabelledColumn(
                        dbObject.SchemaName, dbObject.Name, backingColumn, entityField, structure.SourceAlias));
                    structure.GroupByMetadata.Fields[backingColumn] = new Column(
                        dbObject.SchemaName, dbObject.Name, backingColumn, structure.SourceAlias);
                }

                // Build aggregation column using engine's AggregationColumn type.
                // For COUNT(*), we use the primary key column (PK is always NOT NULL, so COUNT(pk) ≡ COUNT(*)).
                AggregationType aggregationType = Enum.Parse<AggregationType>(function);
                AggregationColumn aggregationColumn = new(
                    dbObject.SchemaName, dbObject.Name, backingField!, aggregationType, alias, distinct, structure.SourceAlias);

                // Build HAVING predicates using engine's Predicate model
                List<Predicate> havingPredicates = new();
                if (havingOperators != null)
                {
                    foreach (var havingOperator in havingOperators)
                    {
                        PredicateOperation predicateOperation = havingOperator.Key.ToLowerInvariant() switch
                        {
                            "eq" => PredicateOperation.Equal,
                            "neq" => PredicateOperation.NotEqual,
                            "gt" => PredicateOperation.GreaterThan,
                            "gte" => PredicateOperation.GreaterThanOrEqual,
                            "lt" => PredicateOperation.LessThan,
                            "lte" => PredicateOperation.LessThanOrEqual,
                            _ => throw new ArgumentException($"Invalid having operator: {havingOperator.Key}")
                        };
                        string paramName = BaseQueryStructure.GetEncodedParamName(structure.Counter.Next());
                        structure.Parameters.Add(paramName, new DbConnectionParam(havingOperator.Value));
                        havingPredicates.Add(new Predicate(
                            new PredicateOperand(aggregationColumn),
                            predicateOperation,
                            new PredicateOperand(paramName)));
                    }
                }

                if (havingInValues != null && havingInValues.Count > 0)
                {
                    List<string> inParams = new();
                    foreach (double val in havingInValues)
                    {
                        string paramName = BaseQueryStructure.GetEncodedParamName(structure.Counter.Next());
                        structure.Parameters.Add(paramName, new DbConnectionParam(val));
                        inParams.Add(paramName);
                    }

                    havingPredicates.Add(new Predicate(
                        new PredicateOperand(aggregationColumn),
                        PredicateOperation.IN,
                        new PredicateOperand($"({string.Join(", ", inParams)})")));
                }

                // Combine multiple HAVING predicates with AND
                Predicate? combinedHaving = null;
                foreach (var predicate in havingPredicates)
                {
                    combinedHaving = combinedHaving == null
                        ? predicate
                        : new Predicate(new PredicateOperand(combinedHaving), PredicateOperation.AND, new PredicateOperand(predicate));
                }

                structure.GroupByMetadata.Aggregations.Add(
                    new AggregationOperation(aggregationColumn, having: combinedHaving != null ? new List<Predicate> { combinedHaving } : null));
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

                // For groupby queries: add ORDER BY aggregate expression and pagination
                if (groupbyMapping.Count > 0)
                {
                    string direction = orderby.Equals("asc", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";
                    string quotedCol = $"{queryBuilder.QuoteIdentifier(structure.SourceAlias)}.{queryBuilder.QuoteIdentifier(backingField!)}";
                    string orderByAggExpr = distinct
                        ? $"{function.ToUpperInvariant()}(DISTINCT {quotedCol})"
                        : $"{function.ToUpperInvariant()}({quotedCol})";
                    string orderByClause = $" ORDER BY {orderByAggExpr} {direction}";

                    if (first.HasValue)
                    {
                        // With pagination: SQL Server requires ORDER BY for OFFSET/FETCH and
                        // does not allow both TOP and OFFSET/FETCH. Remove TOP and add ORDER BY + OFFSET/FETCH.
                        int offset = DecodeCursorOffset(after);
                        int fetchCount = first.Value + 1;
                        string offsetParam = BaseQueryStructure.GetEncodedParamName(structure.Counter.Next());
                        structure.Parameters.Add(offsetParam, new DbConnectionParam(offset));
                        string limitParam = BaseQueryStructure.GetEncodedParamName(structure.Counter.Next());
                        structure.Parameters.Add(limitParam, new DbConnectionParam(fetchCount));

                        string paginationClause = $" OFFSET {offsetParam} ROWS FETCH NEXT {limitParam} ROWS ONLY";

                        // Remove TOP N from the SELECT clause (TOP conflicts with OFFSET/FETCH)
                        sql = Regex.Replace(sql, @"SELECT TOP \d+", "SELECT");

                        // Insert ORDER BY + pagination before FOR JSON PATH
                        int jsonPathIdx = sql.IndexOf(" FOR JSON PATH", StringComparison.OrdinalIgnoreCase);
                        if (jsonPathIdx > 0)
                        {
                            sql = sql.Insert(jsonPathIdx, orderByClause + paginationClause);
                        }
                        else
                        {
                            sql += orderByClause + paginationClause;
                        }
                    }
                    else
                    {
                        // Without pagination: insert ORDER BY before FOR JSON PATH
                        int jsonPathIdx = sql.IndexOf(" FOR JSON PATH", StringComparison.OrdinalIgnoreCase);
                        if (jsonPathIdx > 0)
                        {
                            sql = sql.Insert(jsonPathIdx, orderByClause);
                        }
                        else
                        {
                            sql += orderByClause;
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
            catch (TimeoutException timeoutException)
            {
                logger?.LogError(timeoutException, "Aggregation operation timed out for entity {Entity}.", entityName);
                return McpResponseBuilder.BuildErrorResult(
                    toolName,
                    "TimeoutError",
                    BuildTimeoutErrorMessage(entityName),
                    logger);
            }
            catch (TaskCanceledException taskCanceledException)
            {
                logger?.LogError(taskCanceledException, "Aggregation task was canceled for entity {Entity}.", entityName);
                return McpResponseBuilder.BuildErrorResult(
                    toolName,
                    "TimeoutError",
                    BuildTaskCanceledErrorMessage(entityName),
                    logger);
            }
            catch (OperationCanceledException)
            {
                logger?.LogWarning("Aggregation operation was canceled for entity {Entity}.", entityName);
                return McpResponseBuilder.BuildErrorResult(
                    toolName,
                    "OperationCanceled",
                    BuildOperationCanceledErrorMessage(entityName),
                    logger);
            }
            catch (DbException dbException)
            {
                logger?.LogError(dbException, "Database error during aggregation for entity {Entity}.", entityName);
                return McpResponseBuilder.BuildErrorResult(toolName, "DatabaseOperationFailed", dbException.Message, logger);
            }
            catch (ArgumentException argumentException)
            {
                return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments", argumentException.Message, logger);
            }
            catch (DataApiBuilderException dabException)
            {
                return McpResponseBuilder.BuildErrorResult(toolName, dabException.StatusCode.ToString(), dabException.Message, logger);
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

        /// <summary>
        /// Builds the error message for a TimeoutException during aggregation.
        /// </summary>
        internal static string BuildTimeoutErrorMessage(string entityName)
        {
            return $"The aggregation query for entity '{entityName}' timed out. "
                + "This is NOT a tool error. The database did not respond in time. "
                + "This may occur with large datasets or complex aggregations. "
                + "Try narrowing results with a 'filter', reducing 'groupby' fields, or adding 'first' for pagination.";
        }

        /// <summary>
        /// Builds the error message for a TaskCanceledException during aggregation (typically a timeout).
        /// </summary>
        internal static string BuildTaskCanceledErrorMessage(string entityName)
        {
            return $"The aggregation query for entity '{entityName}' was canceled, likely due to a timeout. "
                + "This is NOT a tool error. The database did not respond in time. "
                + "Try narrowing results with a 'filter', reducing 'groupby' fields, or adding 'first' for pagination.";
        }

        /// <summary>
        /// Builds the error message for an OperationCanceledException during aggregation.
        /// </summary>
        internal static string BuildOperationCanceledErrorMessage(string entityName)
        {
            return $"The aggregation query for entity '{entityName}' was canceled before completion. "
                + "This is NOT a tool error. The operation was interrupted, possibly due to a timeout or client disconnect. "
                + "No results were returned. You may retry the same request.";
        }
    }
}
