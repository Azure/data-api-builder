// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Services;
using HotChocolate.Resolvers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
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
        private readonly ISqlMetadataProvider _sqlMetadataProvider;
        private readonly IQueryExecutor _queryExecutor;
        private readonly IQueryBuilder _queryBuilder;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IAuthorizationResolver _authorizationResolver;
        private readonly ILogger<IQueryEngine> _logger;
        private readonly RuntimeConfigProvider _runtimeConfigProvider;
        private readonly GQLFilterParser _gQLFilterParser;

        // <summary>
        // Constructor.
        // </summary>
        public SqlQueryEngine(
            IQueryExecutor queryExecutor,
            IQueryBuilder queryBuilder,
            ISqlMetadataProvider sqlMetadataProvider,
            IHttpContextAccessor httpContextAccessor,
            IAuthorizationResolver authorizationResolver,
            GQLFilterParser gQLFilterParser,
            ILogger<IQueryEngine> logger,
            RuntimeConfigProvider runtimeConfigProvider)
        {
            _queryExecutor = queryExecutor;
            _queryBuilder = queryBuilder;
            _sqlMetadataProvider = sqlMetadataProvider;
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
        public async Task<Tuple<JsonDocument?, IMetadata?>> ExecuteAsync(IMiddlewareContext context, IDictionary<string, object?> parameters)
        {
            SqlQueryStructure structure = new(
                context,
                parameters,
                _sqlMetadataProvider,
                _authorizationResolver,
                _runtimeConfigProvider,
                _gQLFilterParser);

            if (structure.PaginationMetadata.IsPaginated)
            {
                return new Tuple<JsonDocument?, IMetadata?>(
                    SqlPaginationUtil.CreatePaginationConnectionFromJsonDocument(await ExecuteAsync(structure), structure.PaginationMetadata),
                    structure.PaginationMetadata);
            }
            else
            {
                return new Tuple<JsonDocument?, IMetadata?>(
                    await ExecuteAsync(structure),
                    structure.PaginationMetadata);
            }
        }

        /// <summary>
        /// Executes the given IMiddlewareContext of the GraphQL and expecting result of stored-procedure execution as
        /// list of Jsons and the relevant pagination metadata back.
        /// </summary>
        public async Task<Tuple<IEnumerable<JsonDocument>, IMetadata?>> ExecuteListAsync(IMiddlewareContext context, IDictionary<string, object?> parameters)
        {
            if (_sqlMetadataProvider.GraphQLStoredProcedureExposedNameToEntityNameMap.TryGetValue(context.Selection.Field.Name.Value, out string? entityName))
            {
                SqlExecuteStructure sqlExecuteStructure = new(
                    entityName,
                    _sqlMetadataProvider,
                    _authorizationResolver,
                    _gQLFilterParser,
                    parameters);

                return new Tuple<IEnumerable<JsonDocument>, IMetadata?>(
                        FormatStoredProcedureResultAsJsonList(await ExecuteAsync(sqlExecuteStructure)),
                        PaginationMetadata.MakeEmptyPaginationMetadata());
            }
            else
            {
                SqlQueryStructure structure = new(
                    context,
                    parameters,
                    _sqlMetadataProvider,
                    _authorizationResolver,
                    _runtimeConfigProvider,
                    _gQLFilterParser);

                string queryString = _queryBuilder.Build(structure);
                List<JsonDocument>? jsonListResult =
                    await _queryExecutor.ExecuteQueryAsync(
                        queryString,
                        structure.Parameters,
                        _queryExecutor.GetJsonResultAsync<List<JsonDocument>>,
                        _httpContextAccessor.HttpContext!);

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
        public async Task<IActionResult> ExecuteAsync(FindRequestContext context)
        {
            SqlQueryStructure structure = new(
                context,
                _sqlMetadataProvider,
                _authorizationResolver,
                _runtimeConfigProvider,
                _gQLFilterParser,
                _httpContextAccessor.HttpContext!);
            using JsonDocument? queryJson = await ExecuteAsync(structure);
            // queryJson is null if dbreader had no rows to return
            // If no rows/empty table, return an empty json array
            return queryJson is null ? FormatFindResult(JsonDocument.Parse("[]"), context) :
                                       FormatFindResult(queryJson, context);
        }

        /// <summary>
        /// Given the StoredProcedureRequestContext, obtains the query text and executes it against the backend. Useful for REST API scenarios.
        /// Only the first result set will be returned, regardless of the contents of the stored procedure.
        /// </summary>
        public async Task<IActionResult> ExecuteAsync(StoredProcedureRequestContext context)
        {
            SqlExecuteStructure structure = new(
                context.EntityName,
                _sqlMetadataProvider,
                _authorizationResolver,
                _gQLFilterParser,
                context.ResolvedParameters);
            using JsonDocument? queryJson = await ExecuteAsync(structure);
            // queryJson is null if dbreader had no rows to return
            // If no rows/empty result set, return an empty json array
            return queryJson is null ? OkResponse(JsonDocument.Parse("[]").RootElement.Clone()) :
                                       OkResponse(queryJson.RootElement.Clone());
        }

        /// <summary>
        /// Format the results from a Find operation. Check if there is a requirement
        /// for a nextLink, and if so, add this value to the array of JsonElements to
        /// be used as part of the response.
        /// </summary>
        /// <param name="jsonDoc">The JsonDocument from the query.</param>
        /// <param name="context">The RequestContext.</param>
        /// <returns>An OkObjectResult from a Find operation that has been correctly formatted.</returns>
        private OkObjectResult FormatFindResult(JsonDocument jsonDoc, FindRequestContext context)
        {
            JsonElement jsonElement = jsonDoc.RootElement.Clone();

            // If the results are not a collection or if the query does not have a next page
            // no nextLink is needed, return JsonDocument as is
            if (jsonElement.ValueKind is not JsonValueKind.Array || !SqlPaginationUtil.HasNext(jsonElement, context.First))
            {
                // Clones the root element to a new JsonElement that can be
                // safely stored beyond the lifetime of the original JsonDocument.
                return OkResponse(jsonElement);
            }

            // More records exist than requested, we know this by requesting 1 extra record,
            // that extra record is removed here.
            IEnumerable<JsonElement> rootEnumerated = jsonElement.EnumerateArray();

            rootEnumerated = rootEnumerated.Take(rootEnumerated.Count() - 1);
            string after = SqlPaginationUtil.MakeCursorFromJsonElement(
                               element: rootEnumerated.Last(),
                               orderByColumns: context.OrderByClauseOfBackingColumns,
                               primaryKey: _sqlMetadataProvider.GetSourceDefinition(context.EntityName).PrimaryKey,
                               entityName: context.EntityName,
                               schemaName: context.DatabaseObject.SchemaName,
                               tableName: context.DatabaseObject.Name,
                               sqlMetadataProvider: _sqlMetadataProvider);

            // nextLink is the URL needed to get the next page of records using the same query options
            // with $after base64 encoded for opaqueness
            string path = UriHelper.GetEncodedUrl(_httpContextAccessor.HttpContext!.Request).Split('?')[0];

            RuntimeConfig runtimeConfig = _runtimeConfigProvider.GetConfig();
            // If the base route is not empty, we need to insert it into the URI before the rest path.
            string? baseRoute = runtimeConfig.Runtime.BaseRoute;
            if (!string.IsNullOrWhiteSpace(baseRoute))
            {
                HttpRequest request = _httpContextAccessor.HttpContext!.Request;

                // Path is of the form ....restPath/pathNameForEntity. We want to insert the base route before the restPath.
                // Finally, it will be of the form: .../baseRoute/restPath/pathNameForEntity.
                path = UriHelper.BuildAbsolute(
                    scheme: request.Scheme,
                    host: request.Host,
                    pathBase: baseRoute,
                    path: request.Path);
            }

            JsonElement nextLink = SqlPaginationUtil.CreateNextLink(
                                  path,
                                  nvc: context!.ParsedQueryString,
                                  after);
            rootEnumerated = rootEnumerated.Append(nextLink);
            return OkResponse(JsonSerializer.SerializeToElement(rootEnumerated));
        }

        /// <summary>
        /// Helper function returns an OkObjectResult with provided arguments in a
        /// form that complies with vNext Api guidelines.
        /// </summary>
        /// <param name="jsonResult">Value representing the Json results of the client's request.</param>
        /// <returns>Correctly formatted OkObjectResult.</returns>
        public static OkObjectResult OkResponse(JsonElement jsonResult)
        {
            // For consistency we return all values as type Array
            if (jsonResult.ValueKind != JsonValueKind.Array)
            {
                string jsonString = $"[{JsonSerializer.Serialize(jsonResult)}]";
                jsonResult = JsonSerializer.Deserialize<JsonElement>(jsonString);
            }

            IEnumerable<JsonElement> resultEnumerated = jsonResult.EnumerateArray();
            // More than 0 records, and the last element is of type array, then we have pagination
            if (resultEnumerated.Count() > 0 && resultEnumerated.Last().ValueKind == JsonValueKind.Array)
            {
                // Get the nextLink
                // resultEnumerated will be an array of the form
                // [{object1}, {object2},...{objectlimit}, [{nextLinkObject}]]
                // if the last element is of type array, we know it is nextLink
                // we strip the "[" and "]" and then save the nextLink element
                // into a dictionary with a key of "nextLink" and a value that
                // represents the nextLink data we require.
                string nextLinkJsonString = JsonSerializer.Serialize(resultEnumerated.Last());
                Dictionary<string, object> nextLink = JsonSerializer.Deserialize<Dictionary<string, object>>(nextLinkJsonString[1..^1])!;
                IEnumerable<JsonElement> value = resultEnumerated.Take(resultEnumerated.Count() - 1);
                return new OkObjectResult(new
                {
                    value = value,
                    @nextLink = nextLink["nextLink"]
                });
            }

            // no pagination, do not need nextLink
            return new OkObjectResult(new
            {
                value = resultEnumerated
            });
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
        private async Task<JsonDocument?> ExecuteAsync(SqlQueryStructure structure)
        {
            // Open connection and execute query using _queryExecutor
            string queryString = _queryBuilder.Build(structure);
            JsonDocument? jsonDocument =
                await _queryExecutor.ExecuteQueryAsync(
                    queryString,
                    structure.Parameters,
                    _queryExecutor.GetJsonResultAsync<JsonDocument>,
                    _httpContextAccessor.HttpContext!);
            return jsonDocument;
        }

        // <summary>
        // Given the SqlExecuteStructure structure, obtains the query text and executes it against the backend.
        // Unlike a normal query, result from database may not be JSON. Instead we treat output as SqlMutationEngine does (extract by row).
        // As such, this could feasibly be moved to the mutation engine. 
        // </summary>
        private async Task<JsonDocument?> ExecuteAsync(SqlExecuteStructure structure)
        {
            string queryString = _queryBuilder.Build(structure);

            JsonArray? resultArray =
                await _queryExecutor.ExecuteQueryAsync(
                    queryString,
                    structure.Parameters,
                    _queryExecutor.GetJsonArrayAsync,
                    _httpContextAccessor.HttpContext!);

            JsonDocument? jsonDocument = null;

            // If result set is non-empty, parse rows from json array into JsonDocument
            if (resultArray is not null && resultArray.Count > 0)
            {
                jsonDocument = JsonDocument.Parse(resultArray.ToJsonString());
            }
            else
            {
                _logger.LogInformation($"{HttpContextExtensions.GetLoggerCorrelationId(_httpContextAccessor.HttpContext)}" +
                    "Did not return enough rows.");
            }

            return jsonDocument;
        }
    }
}
