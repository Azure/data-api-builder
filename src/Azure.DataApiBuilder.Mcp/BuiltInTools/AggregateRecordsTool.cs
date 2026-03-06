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

        /// <summary>
        /// Holds all validated arguments parsed from the tool invocation.
        /// </summary>
        internal sealed record AggregateArguments(
            string EntityName,
            string Function,
            string Field,
            bool IsCountStar,
            bool Distinct,
            string? Filter,
            bool UserProvidedOrderby,
            string Orderby,
            int? First,
            string? After,
            List<string> Groupby,
            Dictionary<string, double>? HavingOperators,
            List<double>? HavingInValues);

        /// <summary>
        /// Holds the result of a successful authorization and context-building step.
        /// </summary>
        private sealed record AuthorizedContext(
            FindRequestContext RequestContext,
            HttpContext HttpContext);

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
            string entityName = string.Empty;

            RuntimeConfigProvider runtimeConfigProvider = serviceProvider.GetRequiredService<RuntimeConfigProvider>();
            RuntimeConfig runtimeConfig = runtimeConfigProvider.GetConfig();

            if (runtimeConfig.McpDmlTools?.AggregateRecords is not true)
            {
                return McpErrorHelpers.ToolDisabled(toolName, logger);
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 1. Parse and validate all input arguments
                CallToolResult? parseError = TryParseAndValidateArguments(arguments, runtimeConfig, toolName, out AggregateArguments args, logger);
                if (parseError != null)
                {
                    return parseError;
                }

                entityName = args.EntityName;

                // 2. Resolve metadata and validate entity source type
                if (!McpMetadataHelper.TryResolveMetadata(
                        entityName, runtimeConfig, serviceProvider,
                        out ISqlMetadataProvider sqlMetadataProvider,
                        out DatabaseObject dbObject,
                        out string dataSourceName,
                        out string metadataError))
                {
                    return McpResponseBuilder.BuildErrorResult(toolName, "EntityNotFound", metadataError, logger);
                }

                CallToolResult? sourceTypeError = ValidateEntitySourceType(entityName, dbObject, toolName, logger);
                if (sourceTypeError != null)
                {
                    return sourceTypeError;
                }

                // 3. Early field validation: check all user-supplied field names before authorization or query building
                CallToolResult? fieldError = ValidateFieldsExist(args, entityName, sqlMetadataProvider, toolName, logger);
                if (fieldError != null)
                {
                    return fieldError;
                }

                // 4. Authorize the request and build the query context
                (AuthorizedContext? authCtx, CallToolResult? authError) = await AuthorizeRequestAsync(
                    args, entityName, dbObject, serviceProvider, runtimeConfigProvider, sqlMetadataProvider, toolName, logger);
                if (authError != null)
                {
                    return authError;
                }

                // 5. Validate database type support
                DatabaseType databaseType = runtimeConfig.GetDataSourceFromDataSourceName(dataSourceName).DatabaseType;
                if (databaseType != DatabaseType.MSSQL && databaseType != DatabaseType.DWSQL)
                {
                    return McpResponseBuilder.BuildErrorResult(toolName, "UnsupportedDatabase",
                        $"Aggregation is not supported for database type '{databaseType}'. Aggregation is only available for Azure SQL, SQL Server, and SQL Data Warehouse.", logger);
                }

                // 6. Build SQL query structure with aggregation, groupby, having
                IAuthorizationResolver authResolver = serviceProvider.GetRequiredService<IAuthorizationResolver>();
                GQLFilterParser gQLFilterParser = serviceProvider.GetRequiredService<GQLFilterParser>();
                SqlQueryStructure structure = new(
                    authCtx!.RequestContext, sqlMetadataProvider, authResolver, runtimeConfigProvider, gQLFilterParser, authCtx.HttpContext);

                string? backingField = ResolveBackingField(args, entityName, sqlMetadataProvider, toolName, out CallToolResult? pkError, logger);
                if (pkError != null)
                {
                    return pkError;
                }

                string alias = ComputeAlias(args.Function, args.Field);
                BuildAggregationStructure(args, structure, dbObject, backingField!, alias, entityName, sqlMetadataProvider);

                // 7. Generate and post-process SQL
                IAbstractQueryManagerFactory queryManagerFactory = serviceProvider.GetRequiredService<IAbstractQueryManagerFactory>();
                IQueryBuilder queryBuilder = queryManagerFactory.GetQueryBuilder(databaseType);
                IQueryExecutor queryExecutor = queryManagerFactory.GetQueryExecutor(databaseType);

                string sql = queryBuilder.Build(structure);
                if (args.Groupby.Count > 0)
                {
                    sql = ApplyOrderByAndPagination(sql, args, structure, queryBuilder, backingField!);
                }

                // 8. Execute query and return results
                cancellationToken.ThrowIfCancellationRequested();
                JsonDocument? queryResult = await queryExecutor.ExecuteQueryAsync(
                    sql, structure.Parameters, queryExecutor.GetJsonResultAsync<JsonDocument>,
                    dataSourceName, authCtx.HttpContext);

                JsonArray? resultArray = queryResult != null
                    ? JsonSerializer.Deserialize<JsonArray>(queryResult.RootElement.GetRawText())
                    : null;

                return args.First.HasValue && args.Groupby.Count > 0
                    ? BuildPaginatedResponse(resultArray, args.First.Value, args.After, entityName, logger)
                    : BuildSimpleResponse(resultArray, entityName, alias, logger);
            }
            catch (TimeoutException timeoutException)
            {
                logger?.LogError(timeoutException, "Aggregation operation timed out for entity {Entity}.", entityName);
                return McpResponseBuilder.BuildErrorResult(toolName, "TimeoutError", BuildTimeoutErrorMessage(entityName), logger);
            }
            catch (TaskCanceledException taskCanceledException)
            {
                logger?.LogError(taskCanceledException, "Aggregation task was canceled for entity {Entity}.", entityName);
                return McpResponseBuilder.BuildErrorResult(toolName, "TimeoutError", BuildTaskCanceledErrorMessage(entityName), logger);
            }
            catch (OperationCanceledException)
            {
                logger?.LogWarning("Aggregation operation was canceled for entity {Entity}.", entityName);
                return McpResponseBuilder.BuildErrorResult(toolName, "OperationCanceled", BuildOperationCanceledErrorMessage(entityName), logger);
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

        #region Argument Parsing and Validation

        /// <summary>
        /// Parses and validates all arguments from the tool invocation.
        /// Returns null on success with the parsed arguments in the out parameter,
        /// or returns a <see cref="CallToolResult"/> error to return to the caller.
        /// </summary>
        private static CallToolResult? TryParseAndValidateArguments(
            JsonDocument? arguments,
            RuntimeConfig runtimeConfig,
            string toolName,
            out AggregateArguments args,
            ILogger? logger)
        {
            args = default!;

            if (arguments == null)
            {
                return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments", "No arguments provided.", logger);
            }

            JsonElement root = arguments.RootElement;

            // Parse entity
            if (!McpArgumentParser.TryParseEntity(root, out string entityName, out string parseError))
            {
                return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments", parseError, logger);
            }

            if (runtimeConfig.Entities?.TryGetValue(entityName, out Entity? entity) == true &&
                entity.Mcp?.DmlToolEnabled == false)
            {
                return McpErrorHelpers.ToolDisabled(toolName, logger, $"DML tools are disabled for entity '{entityName}'.");
            }

            // Parse function
            if (!root.TryGetProperty("function", out JsonElement functionElement) || string.IsNullOrWhiteSpace(functionElement.GetString()))
            {
                return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments", "Missing required argument 'function'.", logger);
            }

            string function = functionElement.GetString()!.ToLowerInvariant();
            if (!_validFunctions.Contains(function))
            {
                return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments",
                    $"Invalid function '{function}'. Must be one of: count, avg, sum, min, max.", logger);
            }

            // Parse field
            if (!root.TryGetProperty("field", out JsonElement fieldElement) || string.IsNullOrWhiteSpace(fieldElement.GetString()))
            {
                return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments", "Missing required argument 'field'.", logger);
            }

            string field = fieldElement.GetString()!;
            bool isCountStar = function == "count" && field == "*";

            if (field == "*" && function != "count")
            {
                return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments",
                    $"Field '*' is only valid with function 'count'. For function '{function}', provide a specific field name.", logger);
            }

            // Parse distinct
            bool distinct = root.TryGetProperty("distinct", out JsonElement distinctElement) && distinctElement.GetBoolean();

            if (isCountStar && distinct)
            {
                return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments",
                    "Cannot use distinct=true with field='*'. DISTINCT requires a specific field name. Use a field name instead of '*' to count distinct values.", logger);
            }

            // Parse filter
            string? filter = root.TryGetProperty("filter", out JsonElement filterElement) ? filterElement.GetString() : null;

            // Parse orderby
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

            // Parse first
            int? first = null;
            if (root.TryGetProperty("first", out JsonElement firstElement) && firstElement.ValueKind == JsonValueKind.Number)
            {
                first = firstElement.GetInt32();
                if (first < 1)
                {
                    return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments", "Argument 'first' must be at least 1.", logger);
                }
            }

            // Parse after
            string? after = root.TryGetProperty("after", out JsonElement afterElement) ? afterElement.GetString() : null;

            // Parse groupby
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

            // Validate groupby-dependent parameters
            CallToolResult? dependencyError = ValidateGroupByDependencies(
                groupby.Count, userProvidedOrderby, first, after, toolName, logger);
            if (dependencyError != null)
            {
                return dependencyError;
            }

            // Parse having clause
            Dictionary<string, double>? havingOperators = null;
            List<double>? havingInValues = null;
            if (root.TryGetProperty("having", out JsonElement havingElement) && havingElement.ValueKind == JsonValueKind.Object)
            {
                CallToolResult? havingError = TryParseHaving(
                    havingElement, groupby.Count, toolName, out havingOperators, out havingInValues, logger);
                if (havingError != null)
                {
                    return havingError;
                }
            }

            args = new AggregateArguments(
                EntityName: entityName,
                Function: function,
                Field: field,
                IsCountStar: isCountStar,
                Distinct: distinct,
                Filter: filter,
                UserProvidedOrderby: userProvidedOrderby,
                Orderby: orderby,
                First: first,
                After: after,
                Groupby: groupby,
                HavingOperators: havingOperators,
                HavingInValues: havingInValues);

            return null;
        }

        /// <summary>
        /// Validates that parameters requiring groupby (orderby, first, after) are only used when groupby is present.
        /// Also validates that 'after' requires 'first'.
        /// </summary>
        private static CallToolResult? ValidateGroupByDependencies(
            int groupbyCount,
            bool userProvidedOrderby,
            int? first,
            string? after,
            string toolName,
            ILogger? logger)
        {
            if (groupbyCount == 0)
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

            return null;
        }

        /// <summary>
        /// Parses and validates the 'having' clause from the tool arguments.
        /// </summary>
        private static CallToolResult? TryParseHaving(
            JsonElement havingElement,
            int groupbyCount,
            string toolName,
            out Dictionary<string, double>? havingOperators,
            out List<double>? havingInValues,
            ILogger? logger)
        {
            havingOperators = null;
            havingInValues = null;

            if (groupbyCount == 0)
            {
                return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments",
                    "The 'having' parameter requires 'groupby' to be specified. HAVING filters groups after aggregation.", logger);
            }

            havingOperators = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty prop in havingElement.EnumerateObject())
            {
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
                    if (prop.Value.ValueKind != JsonValueKind.Number)
                    {
                        return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments",
                            $"The 'having.{prop.Name}' value must be numeric. Got: '{prop.Value}'. HAVING filters compare aggregated numeric results.", logger);
                    }

                    havingOperators[prop.Name] = prop.Value.GetDouble();
                }
            }

            return null;
        }

        #endregion

        #region Entity and Field Validation

        /// <summary>
        /// Validates that the entity is a table or view (not a stored procedure).
        /// </summary>
        private static CallToolResult? ValidateEntitySourceType(
            string entityName, DatabaseObject dbObject, string toolName, ILogger? logger)
        {
            if (dbObject.SourceType != EntitySourceType.Table && dbObject.SourceType != EntitySourceType.View)
            {
                return McpResponseBuilder.BuildErrorResult(toolName, "InvalidEntity",
                    $"Entity '{entityName}' is not a table or view. Aggregation is not supported for stored procedures. Use 'execute_entity' for stored procedures.", logger);
            }

            return null;
        }

        /// <summary>
        /// Validates that all user-supplied field names (aggregation field and groupby fields)
        /// exist in the entity's metadata. This early validation lets the model discover typos immediately.
        /// </summary>
        private static CallToolResult? ValidateFieldsExist(
            AggregateArguments args,
            string entityName,
            ISqlMetadataProvider sqlMetadataProvider,
            string toolName,
            ILogger? logger)
        {
            if (!args.IsCountStar && !sqlMetadataProvider.TryGetBackingColumn(entityName, args.Field, out _))
            {
                return McpErrorHelpers.FieldNotFound(toolName, entityName, args.Field, "field", logger);
            }

            foreach (string groupbyField in args.Groupby)
            {
                if (!sqlMetadataProvider.TryGetBackingColumn(entityName, groupbyField, out _))
                {
                    return McpErrorHelpers.FieldNotFound(toolName, entityName, groupbyField, "groupby", logger);
                }
            }

            return null;
        }

        #endregion

        #region Authorization

        /// <summary>
        /// Authorizes the request and builds the <see cref="FindRequestContext"/> with validated fields and filters.
        /// Returns a tuple of (AuthorizedContext on success, CallToolResult error on failure).
        /// </summary>
        private static async Task<(AuthorizedContext? context, CallToolResult? error)> AuthorizeRequestAsync(
            AggregateArguments args,
            string entityName,
            DatabaseObject dbObject,
            IServiceProvider serviceProvider,
            RuntimeConfigProvider runtimeConfigProvider,
            ISqlMetadataProvider sqlMetadataProvider,
            string toolName,
            ILogger? logger)
        {
            IAuthorizationResolver authResolver = serviceProvider.GetRequiredService<IAuthorizationResolver>();
            IAuthorizationService authorizationService = serviceProvider.GetRequiredService<IAuthorizationService>();
            IHttpContextAccessor httpContextAccessor = serviceProvider.GetRequiredService<IHttpContextAccessor>();
            HttpContext? httpContext = httpContextAccessor.HttpContext;

            if (!McpAuthorizationHelper.ValidateRoleContext(httpContext, authResolver, out string roleCtxError))
            {
                return (null, McpErrorHelpers.PermissionDenied(toolName, entityName, "read", roleCtxError, logger));
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
                return (null, McpErrorHelpers.PermissionDenied(toolName, entityName, "read", finalError, logger));
            }

            // Build select list for authorization: groupby fields + aggregation field
            List<string> selectFields = new(args.Groupby);
            if (!args.IsCountStar && !selectFields.Contains(args.Field, StringComparer.OrdinalIgnoreCase))
            {
                selectFields.Add(args.Field);
            }

            // Build and validate FindRequestContext
            RequestValidator requestValidator = new(serviceProvider.GetRequiredService<IMetadataProviderFactory>(), runtimeConfigProvider);
            FindRequestContext context = new(entityName, dbObject, true);
            httpContext!.Request.Method = "GET";

            requestValidator.ValidateEntity(entityName);

            if (selectFields.Count > 0)
            {
                context.UpdateReturnFields(selectFields);
            }

            if (!string.IsNullOrWhiteSpace(args.Filter))
            {
                string filterQueryString = $"?{RequestParser.FILTER_URL}={args.Filter}";
                context.FilterClauseInUrl = sqlMetadataProvider.GetODataParser().GetFilterClause(
                    filterQueryString, $"{context.EntityName}.{context.DatabaseObject.FullName}");
            }

            requestValidator.ValidateRequestContext(context);

            AuthorizationResult authorizationResult = await authorizationService.AuthorizeAsync(
                user: httpContext.User,
                resource: context,
                requirements: new[] { new ColumnsPermissionsRequirement() });
            if (!authorizationResult.Succeeded)
            {
                return (null, McpErrorHelpers.PermissionDenied(toolName, entityName, "read", DataApiBuilderException.AUTHORIZATION_FAILURE, logger));
            }

            return (new AuthorizedContext(context, httpContext), null);
        }

        #endregion

        #region Query Building

        /// <summary>
        /// Resolves the backing database column name for the aggregation field.
        /// For COUNT(*), uses the first primary key column (PK is always NOT NULL, so COUNT(pk) ≡ COUNT(*)).
        /// </summary>
        private static string? ResolveBackingField(
            AggregateArguments args,
            string entityName,
            ISqlMetadataProvider sqlMetadataProvider,
            string toolName,
            out CallToolResult? error,
            ILogger? logger)
        {
            error = null;

            if (!args.IsCountStar)
            {
                sqlMetadataProvider.TryGetBackingColumn(entityName, args.Field, out string? backingField);
                return backingField;
            }

            // For COUNT(*), use primary key column since PK is always NOT NULL,
            // making COUNT(pk) equivalent to COUNT(*). The engine's Build(AggregationColumn)
            // does not support "*" as a column name (it would produce invalid SQL like count([].[*])).
            SourceDefinition sourceDefinition = sqlMetadataProvider.GetSourceDefinition(entityName);
            if (sourceDefinition.PrimaryKey.Count == 0)
            {
                error = McpResponseBuilder.BuildErrorResult(toolName, "InvalidEntity",
                    $"Entity '{entityName}' has no primary key defined. COUNT(*) requires at least one primary key column.", logger);
                return null;
            }

            return sourceDefinition.PrimaryKey[0];
        }

        /// <summary>
        /// Configures the <see cref="SqlQueryStructure"/> with groupby columns, aggregation column,
        /// and HAVING predicates based on the parsed arguments.
        /// </summary>
        private static void BuildAggregationStructure(
            AggregateArguments args,
            SqlQueryStructure structure,
            DatabaseObject dbObject,
            string backingField,
            string alias,
            string entityName,
            ISqlMetadataProvider sqlMetadataProvider)
        {
            // Clear default columns from FindRequestContext
            structure.Columns.Clear();

            // Add groupby columns as LabelledColumns and GroupByMetadata.Fields
            foreach (string groupbyField in args.Groupby)
            {
                sqlMetadataProvider.TryGetBackingColumn(entityName, groupbyField, out string? backingGroupbyColumn);
                structure.Columns.Add(new LabelledColumn(
                    dbObject.SchemaName, dbObject.Name, backingGroupbyColumn!, groupbyField, structure.SourceAlias));
                structure.GroupByMetadata.Fields[backingGroupbyColumn!] = new Column(
                    dbObject.SchemaName, dbObject.Name, backingGroupbyColumn!, structure.SourceAlias);
            }

            // Build aggregation column using engine's AggregationColumn type.
            AggregationType aggregationType = Enum.Parse<AggregationType>(args.Function);
            AggregationColumn aggregationColumn = new(
                dbObject.SchemaName, dbObject.Name, backingField, aggregationType, alias, args.Distinct, structure.SourceAlias);

            // Build HAVING predicate and configure aggregation metadata
            Predicate? combinedHaving = BuildHavingPredicate(args, aggregationColumn, structure);
            structure.GroupByMetadata.Aggregations.Add(
                new AggregationOperation(aggregationColumn, having: combinedHaving != null ? new List<Predicate> { combinedHaving } : null));
            structure.GroupByMetadata.RequestedAggregations = true;

            // Clear default OrderByColumns (PK-based) and configure pagination
            structure.OrderByColumns.Clear();
            if (args.First.HasValue && args.Groupby.Count > 0)
            {
                structure.IsListQuery = true;
            }
        }

        /// <summary>
        /// Builds a combined HAVING predicate from the parsed having operators and IN values.
        /// Multiple conditions are AND-ed together.
        /// </summary>
        private static Predicate? BuildHavingPredicate(
            AggregateArguments args,
            AggregationColumn aggregationColumn,
            SqlQueryStructure structure)
        {
            List<Predicate> havingPredicates = new();

            if (args.HavingOperators != null)
            {
                foreach (var havingOperator in args.HavingOperators)
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

            if (args.HavingInValues != null && args.HavingInValues.Count > 0)
            {
                List<string> inParams = new();
                foreach (double val in args.HavingInValues)
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

            return combinedHaving;
        }

        /// <summary>
        /// Post-processes the generated SQL to add ORDER BY and OFFSET/FETCH pagination
        /// for grouped aggregation queries.
        /// </summary>
        private static string ApplyOrderByAndPagination(
            string sql,
            AggregateArguments args,
            SqlQueryStructure structure,
            IQueryBuilder queryBuilder,
            string backingField)
        {
            string direction = args.Orderby.Equals("asc", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";
            string quotedCol = $"{queryBuilder.QuoteIdentifier(structure.SourceAlias)}.{queryBuilder.QuoteIdentifier(backingField)}";
            string orderByAggExpr = args.Distinct
                ? $"{args.Function.ToUpperInvariant()}(DISTINCT {quotedCol})"
                : $"{args.Function.ToUpperInvariant()}({quotedCol})";
            string orderByClause = $" ORDER BY {orderByAggExpr} {direction}";

            if (args.First.HasValue)
            {
                // With pagination: SQL Server requires ORDER BY for OFFSET/FETCH and
                // does not allow both TOP and OFFSET/FETCH. Remove TOP and add ORDER BY + OFFSET/FETCH.
                int offset = DecodeCursorOffset(args.After);
                int fetchCount = args.First.Value + 1;
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

            return sql;
        }

        #endregion

        #region Result Formatting and Helpers

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

        #endregion

        #region Error Message Builders

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

        #endregion
    }
}
