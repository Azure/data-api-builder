// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
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
            RuntimeConfigProvider runtimeConfigProvider)
        {
            _queryFactory = queryFactory;
            _sqlMetadataProviderFactory = sqlMetadataProviderFactory;
            _httpContextAccessor = httpContextAccessor;
            _authorizationResolver = authorizationResolver;
            _gQLFilterParser = gQLFilterParser;
            _logger = logger;
            _runtimeConfigProvider = runtimeConfigProvider;
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
            // for REST API scenarios, use the default datasource
            string dataSourceName = _runtimeConfigProvider.GetConfig().GetDefaultDataSourceName();

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
        public JsonDocument? ResolveInnerObject(JsonElement element, IObjectField fieldSchema, ref IMetadata metadata)
        {
            PaginationMetadata parentMetadata = (PaginationMetadata)metadata;
            PaginationMetadata currentMetadata = parentMetadata.Subqueries[fieldSchema.Name.Value];
            metadata = currentMetadata;

            if (currentMetadata.IsPaginated)
            {
                return SqlPaginationUtil.CreatePaginationConnectionFromJsonElement(element, currentMetadata);
            }
            else
            {
                //TODO: Try to avoid additional deserialization/serialization here.
                return ResolverMiddleware.RepresentsNullValue(element) ? null : JsonDocument.Parse(element.ToString());
            }
        }

        /// <inheritdoc />
        public object? ResolveListType(JsonElement element, IObjectField fieldSchema, ref IMetadata metadata)
        {
            PaginationMetadata parentMetadata = (PaginationMetadata)metadata;
            PaginationMetadata currentMetadata = parentMetadata.Subqueries[fieldSchema.Name.Value];
            metadata = currentMetadata;

            //TODO: Try to avoid additional deserialization/serialization here.
            return JsonSerializer.Deserialize<List<JsonElement>>(element.ToString());
        }

        // <summary>
        // Given the SqlQueryStructure structure, obtains the query text and executes it against the backend.
        // </summary>
        private async Task<JsonDocument?> ExecuteAsync(SqlQueryStructure structure, string dataSourceName)
        {
            DatabaseType databaseType = _runtimeConfigProvider.GetConfig().GetDataSourceFromDataSourceName(dataSourceName).DatabaseType;
            IQueryBuilder queryBuilder = _queryFactory.GetQueryBuilder(databaseType);
            IQueryExecutor queryExecutor = _queryFactory.GetQueryExecutor(databaseType);

            // Open connection and execute query using _queryExecutor
            string queryString = queryBuilder.Build(structure);
            JsonDocument? jsonDocument =
                await queryExecutor.ExecuteQueryAsync(
                    sqltext: queryString,
                    parameters: structure.Parameters,
                    dataReaderHandler: queryExecutor.GetJsonResultAsync<JsonDocument>,
                    httpContext: _httpContextAccessor.HttpContext!,
                    args: null,
                    dataSourceName: dataSourceName);
            return jsonDocument;
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
