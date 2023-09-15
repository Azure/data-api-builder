// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Service.Exceptions;
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
        /// <param name="dataSourceName">Name of datasource for which to set access token. Default dbName taken from config if empty</param>
        public async Task<Tuple<JsonDocument?, IMetadata?>> ExecuteAsync(IMiddlewareContext context, IDictionary<string, object?> parameters, string dataSourceName = "")
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
        public async Task<Tuple<IEnumerable<JsonDocument>, IMetadata?>> ExecuteListAsync(IMiddlewareContext context, IDictionary<string, object?> parameters, string dataSourceName = "")
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
                        FormatStoredProcedureResultAsJsonList(await ExecuteAsync(sqlExecuteStructure, dataSourceName)),
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
                        sqltext: queryString,
                        parameters: structure.Parameters,
                        dataReaderHandler: _queryExecutor.GetJsonResultAsync<List<JsonDocument>>,
                        httpContext: _httpContextAccessor.HttpContext!,
                        args: null,
                        dataSourceName: dataSourceName);

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
        public async Task<IActionResult> ExecuteAsync(FindRequestContext context, string datasourceName = "")
        {
            SqlQueryStructure structure = new(
                context,
                _sqlMetadataProvider,
                _authorizationResolver,
                _runtimeConfigProvider,
                _gQLFilterParser,
                _httpContextAccessor.HttpContext!);
            using JsonDocument? queryJson = await ExecuteAsync(structure, datasourceName);
            // queryJson is null if dbreader had no rows to return
            // If no rows/empty table, return an empty json array
            return queryJson is null ? FormatFindResult(JsonDocument.Parse("[]"), context) :
                                       FormatFindResult(queryJson, context);
        }

        /// <summary>
        /// Given the StoredProcedureRequestContext, obtains the query text and executes it against the backend. Useful for REST API scenarios.
        /// Only the first result set will be returned, regardless of the contents of the stored procedure.
        /// </summary>
        public async Task<IActionResult> ExecuteAsync(StoredProcedureRequestContext context, string dataSourceName = "")
        {
            SqlExecuteStructure structure = new(
                context.EntityName,
                _sqlMetadataProvider,
                _authorizationResolver,
                _gQLFilterParser,
                context.ResolvedParameters);
            using JsonDocument? queryJson = await ExecuteAsync(structure, dataSourceName);
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

            // When there are no rows returned from the database, the jsonElement will be an empty array.
            // In that case, the response is returned as is.
            if (jsonElement.ValueKind is JsonValueKind.Array && jsonElement.GetArrayLength() == 0)
            {
                return OkResponse(jsonElement);
            }

            HashSet<string> extraFieldsInResponse = (jsonElement.ValueKind is not JsonValueKind.Array)
                                                  ? DetermineExtraFieldsInResponse(jsonElement, context)
                                                  : DetermineExtraFieldsInResponse(jsonElement.EnumerateArray().First(), context);

            // If the results are not a collection or if the query does not have a next page
            // no nextLink is needed. So, the response is returned after removing the extra fields.
            if (jsonElement.ValueKind is not JsonValueKind.Array || !SqlPaginationUtil.HasNext(jsonElement, context.First))
            {
                // If there are no additional fields present, the response is returned directly. When there
                // are extra fields, they are removed before returning the response.
                if (extraFieldsInResponse.Count == 0)
                {
                    return OkResponse(jsonElement);
                }
                else
                {
                    return jsonElement.ValueKind is JsonValueKind.Array ? OkResponse(JsonSerializer.SerializeToElement(RemoveExtraFieldsInResponseWithMultipleItems(jsonElement.EnumerateArray().ToList(), extraFieldsInResponse)))
                                                                        : OkResponse(RemoveExtraFieldsInResponseWithSingleItem(jsonElement, extraFieldsInResponse));
                }
            }

            List<JsonElement> rootEnumerated = jsonElement.EnumerateArray().ToList();

            // More records exist than requested, we know this by requesting 1 extra record,
            // that extra record is removed here.
            rootEnumerated.RemoveAt(rootEnumerated.Count - 1);

            // The fields such as primary keys, fields in $orderby clause that are retrieved in addition to the
            // fields requested in the $select clause are required for calculating the $after element which is part of nextLink.
            // So, the extra fields are removed post the calculation of $after element.
            string after = SqlPaginationUtil.MakeCursorFromJsonElement(
                               element: rootEnumerated[rootEnumerated.Count - 1],
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

            // When there are extra fields present, they are removed before returning the response.
            if (extraFieldsInResponse.Count > 0)
            {
                rootEnumerated = RemoveExtraFieldsInResponseWithMultipleItems(rootEnumerated, extraFieldsInResponse);
            }

            rootEnumerated.Add(nextLink);
            return OkResponse(JsonSerializer.SerializeToElement(rootEnumerated));
        }

        /// <summary>
        /// To support pagination and $first clause with Find requests, it is necessary to provide the nextLink
        /// field in the response. For the calculation of nextLink, the fields such as primary keys, fields in $orderby clause
        /// are retrieved from the database in addition to the fields requested in the $select clause.
        /// However, these fields are not required in the response.
        /// This function helps to determine those additional fields that are present in the response.
        /// </summary>
        /// <param name="response">Response json retrieved from the database</param>
        /// <param name="context">FindRequestContext for the GET request.</param>
        /// <returns>Additional fields that are present in the response</returns>
        private static HashSet<string> DetermineExtraFieldsInResponse(JsonElement response, FindRequestContext context)
        {
            HashSet<string> fieldsPresentInResponse = new();

            foreach (JsonProperty property in response.EnumerateObject())
            {
                fieldsPresentInResponse.Add(property.Name);
            }

            // context.FieldsToBeReturned will contain the fields requested in the $select clause.
            // If $select clause is absent, it will contain the list of columns that can be returned in the
            // response taking into account the include and exclude fields configured for the entity.
            // So, the other fields in the response apart from the fields in context.FieldsToBeReturned
            // are not required.
            return fieldsPresentInResponse.Except(context.FieldsToBeReturned).ToHashSet();
        }

        /// <summary>
        /// Helper function that removes the extra fields from each item of a list of json elements.
        /// </summary>
        /// <param name="jsonElementList">List of Json Elements with extra fields</param>
        /// <param name="extraFields">Additional fields that needs to be removed from the list of Json elements</param>
        /// <returns>List of Json Elements after removing the additional fields</returns>
        private static List<JsonElement> RemoveExtraFieldsInResponseWithMultipleItems(List<JsonElement> jsonElementList, IEnumerable<string> extraFields)
        {
            for (int i = 0; i < jsonElementList.Count; i++)
            {
                jsonElementList[i] = RemoveExtraFieldsInResponseWithSingleItem(jsonElementList[i], extraFields);
            }

            return jsonElementList;
        }

        /// <summary>
        /// Helper function that removes the extra fields from a single json element.
        /// </summary>
        /// <param name="jsonElement"> Json Element with extra fields</param>
        /// <param name="extraFields">Additional fields that needs to be removed from the Json element</param>
        /// <returns>Json Element after removing the additional fields</returns>
        private static JsonElement RemoveExtraFieldsInResponseWithSingleItem(JsonElement jsonElement, IEnumerable<string> extraFields)
        {
            JsonObject? jsonObject = JsonObject.Create(jsonElement);

            if (jsonObject is null)
            {
                throw new DataApiBuilderException(
                    message: "While processing your request the server ran into an unexpected error",
                    statusCode: System.Net.HttpStatusCode.InternalServerError,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError);
            }

            foreach (string extraField in extraFields)
            {
                jsonObject.Remove(extraField);
            }

            return JsonSerializer.SerializeToElement(jsonObject);
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
        private async Task<JsonDocument?> ExecuteAsync(SqlQueryStructure structure, string dataSourceName = "")
        {
            // Open connection and execute query using _queryExecutor
            string queryString = _queryBuilder.Build(structure);
            JsonDocument? jsonDocument =
                await _queryExecutor.ExecuteQueryAsync(
                    sqltext: queryString,
                    parameters: structure.Parameters,
                    dataReaderHandler: _queryExecutor.GetJsonResultAsync<JsonDocument>,
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
        private async Task<JsonDocument?> ExecuteAsync(SqlExecuteStructure structure, string dataSourceName = "")
        {
            string queryString = _queryBuilder.Build(structure);

            JsonArray? resultArray =
                await _queryExecutor.ExecuteQueryAsync(
                    sqltext: queryString,
                    parameters: structure.Parameters,
                    dataReaderHandler: _queryExecutor.GetJsonArrayAsync,
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
    }
}
