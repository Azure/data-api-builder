// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.Cache;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Service.GraphQLBuilder;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Queries;
using HotChocolate.Resolvers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLStoredProcedureBuilder;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    //<summary>
    // SqlQueryEngine to execute queries against Sql like databases.
    //</summary>
    public class SqlQueryEngine : IQueryEngine
    {
        private readonly IMetadataProviderFactory _sqlMetadataProviderFactory;
        private readonly IAbstractQueryManagerFactory _queryFactory;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IAuthorizationResolver _authorizationResolver;
        private readonly ILogger<IQueryEngine> _logger;
        private readonly RuntimeConfigProvider _runtimeConfigProvider;
        private readonly GQLFilterParser _gQLFilterParser;
        private readonly DabCacheService _cache;

        // <summary>
        // Constructor.
        // </summary>
        public SqlQueryEngine(
            IAbstractQueryManagerFactory queryFactory,
            IMetadataProviderFactory sqlMetadataProviderFactory,
            IHttpContextAccessor httpContextAccessor,
            IAuthorizationResolver authorizationResolver,
            GQLFilterParser gQLFilterParser,
            ILogger<IQueryEngine> logger,
            RuntimeConfigProvider runtimeConfigProvider,
            DabCacheService cache)
        {
            _queryFactory = queryFactory;
            _sqlMetadataProviderFactory = sqlMetadataProviderFactory;
            _httpContextAccessor = httpContextAccessor;
            _authorizationResolver = authorizationResolver;
            _gQLFilterParser = gQLFilterParser;
            _logger = logger;
            _runtimeConfigProvider = runtimeConfigProvider;
            _cache = cache;
        }

        /// <summary>
        /// Executes the given IMiddlewareContext of the GraphQL query and
        /// expecting a single Json and its related pagination metadata back.
        /// This method is called by the ResolverMiddleware processing GraphQL queries.
        /// </summary>
        /// <param name="context">HotChocolate Request Pipeline context containing request metadata</param>
        /// <param name="parameters">GraphQL Query Parameters from schema retrieved from ResolverMiddleware.GetParametersFromSchemaAndQueryFields()</param>
        /// <param name="dataSourceName">Name of datasource for which to set access token. Default dbName taken from config if empty</param>
        public async Task<Tuple<JsonDocument?, IMetadata?>> ExecuteAsync(IMiddlewareContext context, IDictionary<string, object?> parameters, string dataSourceName)
        {
            SqlQueryStructure structure = new(
                context,
                parameters,
                _sqlMetadataProviderFactory.GetMetadataProvider(dataSourceName),
                _authorizationResolver,
                _runtimeConfigProvider,
                _gQLFilterParser);

            if (structure.PaginationMetadata.IsPaginated)
            {
                return new Tuple<JsonDocument?, IMetadata?>(
                    SqlPaginationUtil.CreatePaginationConnectionFromJsonDocument(await ExecuteAsync(structure, dataSourceName), structure.PaginationMetadata),
                    structure.PaginationMetadata);
            }
            else
            {
                return new Tuple<JsonDocument?, IMetadata?>(
                    await ExecuteAsync(structure, dataSourceName),
                    structure.PaginationMetadata);
            }
        }

        /// <summary>
        /// Executes the given IMiddlewareContext of the GraphQL query and
        /// expecting a single Json and its related pagination metadata back.
        /// This method is used for the selection set resolution of multiple create mutation operation.
        /// </summary>
        /// <param name="context">HotChocolate Request Pipeline context containing request metadata</param>
        /// <param name="parameters">PKs of the created items</param>
        /// <param name="dataSourceName">Name of datasource for which to set access token. Default dbName taken from config if empty</param>
        public async Task<Tuple<JsonDocument?, IMetadata?>> ExecuteMultipleCreateFollowUpQueryAsync(IMiddlewareContext context, List<IDictionary<string, object?>> parameters, string dataSourceName)
        {

            string entityName = GraphQLUtils.GetEntityNameFromContext(context);

            SqlQueryStructure structure = new(
                context,
                parameters,
                _sqlMetadataProviderFactory.GetMetadataProvider(dataSourceName),
                _authorizationResolver,
                _runtimeConfigProvider,
                _gQLFilterParser,
                new IncrementingInteger(),
                entityName,
                isMultipleCreateOperation: true);

            if (structure.PaginationMetadata.IsPaginated)
            {
                return new Tuple<JsonDocument?, IMetadata?>(
                    SqlPaginationUtil.CreatePaginationConnectionFromJsonDocument(await ExecuteAsync(structure, dataSourceName, isMultipleCreateOperation: true), structure.PaginationMetadata),
                    structure.PaginationMetadata);
            }
            else
            {
                return new Tuple<JsonDocument?, IMetadata?>(
                    await ExecuteAsync(structure, dataSourceName, isMultipleCreateOperation: true),
                    structure.PaginationMetadata);
            }
        }

        /// <summary>
        /// Executes the given IMiddlewareContext of the GraphQL and expecting result of stored-procedure execution as
        /// list of Jsons and the relevant pagination metadata back.
        /// </summary>
        public async Task<Tuple<IEnumerable<JsonDocument>, IMetadata?>> ExecuteListAsync(IMiddlewareContext context, IDictionary<string, object?> parameters, string dataSourceName)
        {
            ISqlMetadataProvider sqlMetadataProvider = _sqlMetadataProviderFactory.GetMetadataProvider(dataSourceName);
            if (sqlMetadataProvider.GraphQLStoredProcedureExposedNameToEntityNameMap.TryGetValue(context.Selection.Field.Name.Value, out string? entityName))
            {
                SqlExecuteStructure sqlExecuteStructure = new(
                    entityName,
                    sqlMetadataProvider,
                    _authorizationResolver,
                    _gQLFilterParser,
                    parameters);

                return new Tuple<IEnumerable<JsonDocument>, IMetadata?>(
                        FormatStoredProcedureResultAsJsonList(await ExecuteAsync(sqlExecuteStructure, dataSourceName)),
                        PaginationMetadata.MakeEmptyPaginationMetadata());
            }
            else
            {
                SqlQueryStructure structure = new(
                    context,
                    parameters,
                    sqlMetadataProvider,
                    _authorizationResolver,
                    _runtimeConfigProvider,
                    _gQLFilterParser);

                List<JsonDocument>? jsonListResult = await ExecuteListAsync(structure, dataSourceName);

                if (jsonListResult is null)
                {
                    return new Tuple<IEnumerable<JsonDocument>, IMetadata?>(new List<JsonDocument>(), null);
                }
                else
                {
                    return new Tuple<IEnumerable<JsonDocument>, IMetadata?>(jsonListResult, structure.PaginationMetadata);
                }
            }
        }

        // <summary>
        // Given the FindRequestContext, obtains the query text and executes it against the backend. Useful for REST API scenarios.
        // </summary>
        public async Task<JsonDocument?> ExecuteAsync(FindRequestContext context)
        {
            string dataSourceName = _runtimeConfigProvider.GetConfig().GetDataSourceNameFromEntityName(context.EntityName);

            ISqlMetadataProvider sqlMetadataProvider = _sqlMetadataProviderFactory.GetMetadataProvider(dataSourceName);
            SqlQueryStructure structure = new(
                context,
                sqlMetadataProvider,
                _authorizationResolver,
                _runtimeConfigProvider,
                _gQLFilterParser,
                _httpContextAccessor.HttpContext!);
            return await ExecuteAsync(structure, dataSourceName);
        }

        /// <summary>
        /// Given the StoredProcedureRequestContext, obtains the query text and executes it against the backend. Useful for REST API scenarios.
        /// Only the first result set will be returned, regardless of the contents of the stored procedure.
        /// </summary>
        public async Task<IActionResult> ExecuteAsync(StoredProcedureRequestContext context, string dataSourceName)
        {
            SqlExecuteStructure structure = new(
                context.EntityName,
                _sqlMetadataProviderFactory.GetMetadataProvider(dataSourceName),
                _authorizationResolver,
                _gQLFilterParser,
                context.ResolvedParameters);
            using JsonDocument? queryJson = await ExecuteAsync(structure, dataSourceName);
            // queryJson is null if dbreader had no rows to return
            // If no rows/empty result set, return an empty json array
            return queryJson is null ? SqlResponseHelpers.OkResponse(JsonDocument.Parse("[]").RootElement.Clone()) :
                                       SqlResponseHelpers.OkResponse(queryJson.RootElement.Clone());
        }

        /// <inheritdoc />
        public JsonElement ResolveObject(JsonElement element, IObjectField fieldSchema, ref IMetadata metadata)
        {
            PaginationMetadata parentMetadata = (PaginationMetadata)metadata;
            if (parentMetadata.Subqueries.TryGetValue(QueryBuilder.PAGINATION_FIELD_NAME, out PaginationMetadata? paginationObjectMetadata))
            {
                parentMetadata = paginationObjectMetadata;
            }

            PaginationMetadata currentMetadata = parentMetadata.Subqueries[fieldSchema.Name.Value];
            metadata = currentMetadata;

            if (currentMetadata.IsPaginated)
            {
                return SqlPaginationUtil.CreatePaginationConnectionFromJsonElement(element, currentMetadata);
            }

            // In certain cirumstances (e.g. when processing a DW result), the JsonElement will be JsonValueKind.String instead
            // of JsonValueKind.Object. In this case, we need to parse the JSON. This snippet can be removed when DW result is consistent
            // with MSSQL result.
            if (element.ValueKind is JsonValueKind.String)
            {
                return JsonDocument.Parse(element.ToString()).RootElement.Clone();
            }

            return element;
        }

        /// <summary>
        /// Resolves the JsonElement, an array, into a list of jsonelements where each element represents
        /// an entry in the original array.
        /// </summary>
        /// <param name="array">JsonElement representing a JSON array. The possible representations:
        /// JsonValueKind.Array -> ["item1","itemN"]
        /// JsonValueKind.String -> "[ { "field1": "field1Value" }, { "field2": "field2Value" }, { ... } ]"
        /// - Input JsonElement is JsonValueKind.String because the array and enclosed objects haven't been deserialized yet.
        /// - This method deserializes the JSON string (representing a JSON array) and collects each element (Json object) within the
        /// list of json elements returned by this method.</param>
        /// <param name="fieldSchema">Definition of field being resolved. For lists: [/]items:[entity!]!]</param>
        /// <param name="metadata">PaginationMetadata of the parent field of the currently processed field in HC middlewarecontext.</param>
        /// <returns>List of JsonElements parsed from the provided JSON array.</returns>
        /// <remarks>Return type is 'object' instead of a 'List of JsonElements' because when this function returns JsonElement,
        /// the HC12 engine doesn't know how to handle the JsonElement and results in requests failing at runtime.</remarks>
        public object ResolveList(JsonElement array, IObjectField fieldSchema, ref IMetadata? metadata)
        {
            if (metadata is not null)
            {
                PaginationMetadata parentMetadata = (PaginationMetadata)metadata;
                PaginationMetadata currentMetadata = parentMetadata.Subqueries[fieldSchema.Name.Value];
                metadata = currentMetadata;
            }

            List<JsonElement> resolvedList = new();

            if (array.ValueKind is JsonValueKind.Array)
            {
                foreach (JsonElement element in array.EnumerateArray())
                {
                    resolvedList.Add(element);
                }
            }
            else if (array.ValueKind is JsonValueKind.String)
            {
                using ArrayPoolWriter buffer = new();

                string text = array.GetString()!;
                int neededCapacity = Encoding.UTF8.GetMaxByteCount(text.Length);
                int written = Encoding.UTF8.GetBytes(text, buffer.GetSpan(neededCapacity));
                buffer.Advance(written);

                Utf8JsonReader reader = new(buffer.GetWrittenSpan());
                foreach (JsonElement element in JsonElement.ParseValue(ref reader).EnumerateArray())
                {
                    resolvedList.Add(element);
                }
            }

            return resolvedList;
        }

        // <summary>
        // Given the SqlQueryStructure structure, obtains the query text and executes it against the backend.
        // </summary>
        private async Task<JsonDocument?> ExecuteAsync(SqlQueryStructure structure, string dataSourceName, bool isMultipleCreateOperation = false)
        {
            RuntimeConfig runtimeConfig = _runtimeConfigProvider.GetConfig();
            DatabaseType databaseType = runtimeConfig.GetDataSourceFromDataSourceName(dataSourceName).DatabaseType;
            IQueryBuilder queryBuilder = _queryFactory.GetQueryBuilder(databaseType);
            IQueryExecutor queryExecutor = _queryFactory.GetQueryExecutor(databaseType);

            string queryString;

            // Open connection and execute query using _queryExecutor
            if (isMultipleCreateOperation)
            {
                structure.IsMultipleCreateOperation = true;
                queryString = queryBuilder.Build(structure);
            }
            else
            {
                queryString = queryBuilder.Build(structure);
            }

            // Global Cache enablement check
            if (runtimeConfig.CanUseCache())
            {
                // Entity level cache behavior checks
                bool dbPolicyConfigured = !string.IsNullOrEmpty(structure.DbPolicyPredicatesForOperations[EntityActionOperation.Read]);
                bool entityCacheEnabled = runtimeConfig.Entities[structure.EntityName].IsCachingEnabled;

                // If a db policy is configured for the read operation in the context of the executing role, skip the cache.
                // We want to avoid caching token metadata because token metadata can change frequently and we want to avoid caching it.
                if (!dbPolicyConfigured && entityCacheEnabled)
                {
                    DatabaseQueryMetadata queryMetadata = new(queryText: queryString, dataSource: dataSourceName, queryParameters: structure.Parameters);
                    JsonElement result = await _cache.GetOrSetAsync<JsonElement>(queryExecutor, queryMetadata, cacheEntryTtl: runtimeConfig.GetEntityCacheEntryTtl(entityName: structure.EntityName));
                    byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(result);
                    JsonDocument cacheServiceResponse = JsonDocument.Parse(jsonBytes);
                    return cacheServiceResponse;
                }
            }

            // Execute a request normally (skipping cache) when any of the cache usage checks fail:
            // 1. Global cache is disabled
            // 2. MSSQL datasource set-session-context property is true
            // 3. Entity level cache is disabled
            // 4. A db policy is resolved for the read operation
            JsonDocument? response = await queryExecutor.ExecuteQueryAsync(
                sqltext: queryString,
                parameters: structure.Parameters,
                dataReaderHandler: queryExecutor.GetJsonResultAsync<JsonDocument>,
                httpContext: _httpContextAccessor.HttpContext!,
                args: null,
                dataSourceName: dataSourceName);
            return response;
        }

        // <summary>
        // Given the SqlExecuteStructure structure, obtains the query text and executes it against the backend.
        // Unlike a normal query, result from database may not be JSON. Instead we treat output as SqlMutationEngine does (extract by row).
        // As such, this could feasibly be moved to the mutation engine. 
        // </summary>
        private async Task<JsonDocument?> ExecuteAsync(SqlExecuteStructure structure, string dataSourceName)
        {
            DatabaseType databaseType = _runtimeConfigProvider.GetConfig().GetDataSourceFromDataSourceName(dataSourceName).DatabaseType;
            IQueryBuilder queryBuilder = _queryFactory.GetQueryBuilder(databaseType);
            IQueryExecutor queryExecutor = _queryFactory.GetQueryExecutor(databaseType);
            string queryString = queryBuilder.Build(structure);

            JsonArray? resultArray =
                await queryExecutor.ExecuteQueryAsync(
                    sqltext: queryString,
                    parameters: structure.Parameters,
                    dataReaderHandler: queryExecutor.GetJsonArrayAsync,
                    httpContext: _httpContextAccessor.HttpContext!,
                    args: null,
                    dataSourceName: dataSourceName);

            JsonDocument? jsonDocument = null;

            // If result set is non-empty, parse rows from json array into JsonDocument
            if (resultArray is not null && resultArray.Count > 0)
            {
                jsonDocument = JsonDocument.Parse(resultArray.ToJsonString());
            }
            else
            {
                _logger.LogInformation(
                    message: "{correlationId} Result set did not have any rows.",
                    HttpContextExtensions.GetLoggerCorrelationId(_httpContextAccessor.HttpContext));
            }

            return jsonDocument;
        }

        private async Task<List<JsonDocument>?> ExecuteListAsync(SqlQueryStructure structure, string dataSourceName)
        {
            DatabaseType databaseType = _runtimeConfigProvider.GetConfig().GetDataSourceFromDataSourceName(dataSourceName).DatabaseType;
            IQueryBuilder queryBuilder = _queryFactory.GetQueryBuilder(databaseType);
            IQueryExecutor queryExecutor = _queryFactory.GetQueryExecutor(databaseType);

            string queryString = queryBuilder.Build(structure);

            List<JsonDocument>? jsonListResult =
                await queryExecutor.ExecuteQueryAsync(
                    sqltext: queryString,
                    parameters: structure.Parameters,
                    dataReaderHandler: queryExecutor.GetJsonResultAsync<List<JsonDocument>>,
                    httpContext: _httpContextAccessor.HttpContext!,
                    args: null,
                    dataSourceName: dataSourceName);
            return jsonListResult;
        }
    }
}
