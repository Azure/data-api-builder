// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Authorization;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Mutations;
using Azure.DataApiBuilder.Service.Models;
using Azure.DataApiBuilder.Service.Services;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;

namespace Azure.DataApiBuilder.Service.Resolvers
{
    /// <summary>
    /// Implements the mutation engine interface for mutations against Sql like databases.
    /// </summary>
    public class SqlMutationEngine : IMutationEngine
    {
        private readonly IQueryEngine _queryEngine;
        private readonly ISqlMetadataProvider _sqlMetadataProvider;
        private readonly IQueryExecutor _queryExecutor;
        private readonly IQueryBuilder _queryBuilder;
        private readonly IAuthorizationResolver _authorizationResolver;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly GQLFilterParser _gQLFilterParser;
        public const string IS_FIRST_RESULT_SET = "IsFirstResultSet";

        /// <summary>
        /// Constructor
        /// </summary>
        public SqlMutationEngine(
            IQueryEngine queryEngine,
            IQueryExecutor queryExecutor,
            IQueryBuilder queryBuilder,
            ISqlMetadataProvider sqlMetadataProvider,
            IAuthorizationResolver authorizationResolver,
            GQLFilterParser gQLFilterParser,
            IHttpContextAccessor httpContextAccessor)
        {
            _queryEngine = queryEngine;
            _queryExecutor = queryExecutor;
            _queryBuilder = queryBuilder;
            _sqlMetadataProvider = sqlMetadataProvider;
            _authorizationResolver = authorizationResolver;
            _httpContextAccessor = httpContextAccessor;
            _gQLFilterParser = gQLFilterParser;
        }

        /// <summary>
        /// Executes the GraphQL mutation query and returns result as JSON object asynchronously.
        /// </summary>
        /// <param name="context">context of graphql mutation</param>
        /// <param name="parameters">parameters in the mutation query.</param>
        /// <returns>JSON object result and its related pagination metadata</returns>
        public async Task<Tuple<JsonDocument?, IMetadata?>> ExecuteAsync(IMiddlewareContext context, IDictionary<string, object?> parameters)
        {
            if (context.Selection.Type.IsListType())
            {
                throw new NotSupportedException("Returning list types from mutations not supported");
            }

            string graphqlMutationName = context.Selection.Field.Name.Value;
            IOutputType outputType = context.Selection.Field.Type;
            string entityName = outputType.TypeName();
            ObjectType _underlyingFieldType = GraphQLUtils.UnderlyingGraphQLEntityType(outputType);

            if (GraphQLUtils.TryExtractGraphQLFieldModelName(_underlyingFieldType.Directives, out string? modelName))
            {
                entityName = modelName;
            }

            Tuple<JsonDocument?, IMetadata?>? result = null;
            Config.Operation mutationOperation = MutationBuilder.DetermineMutationOperationTypeBasedOnInputType(graphqlMutationName);

            // If authorization fails, an exception will be thrown and request execution halts.
            AuthorizeMutationFields(context, parameters, entityName, mutationOperation);

            if (mutationOperation is Config.Operation.Delete)
            {
                // compute the mutation result before removing the element,
                // since typical GraphQL delete mutations return the metadata of the deleted item.
                result = await _queryEngine.ExecuteAsync(context, GetBackingColumnsFromCollection(entityName, parameters));

                Dictionary<string, object>? resultProperties =
                    await PerformDeleteOperation(
                        entityName,
                        parameters);

                // If the number of records affected by DELETE were zero,
                // and yet the result was not null previously, it indicates this DELETE lost
                // a concurrent request race. Hence, empty the non-null result.
                if (resultProperties is not null
                    && resultProperties.TryGetValue(nameof(DbDataReader.RecordsAffected), out object? value)
                    && Convert.ToInt32(value) == 0
                    && result is not null && result.Item1 is not null)
                {
                    result = new Tuple<JsonDocument?, IMetadata?>(
                        default(JsonDocument),
                        PaginationMetadata.MakeEmptyPaginationMetadata());
                }
            }
            else
            {
                DbResultSetRow? mutationResultRow =
                    await PerformMutationOperation(
                        entityName,
                        mutationOperation,
                        parameters,
                        context);

                if (mutationResultRow is not null && mutationResultRow.Columns.Count > 0
                    && !context.Selection.Type.IsScalarType())
                {
                    // Because the GraphQL mutation result set columns were exposed (mapped) column names,
                    // the column names must be converted to backing (source) column names so the
                    // PrimaryKeyPredicates created in the SqlQueryStructure created by the query engine
                    // represent database column names.
                    result = await _queryEngine.ExecuteAsync(
                                context,
                                GetBackingColumnsFromCollection(entityName, mutationResultRow.Columns));
                }
            }

            if (result is null)
            {
                throw new DataApiBuilderException(
                    message: "Failed to resolve any query based on the current configuration.",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError);
            }

            return result;
        }

        /// <summary>
        /// Converts exposed column names from the parameters provided to backing column names.
        /// parameters.Value is not modified.
        /// </summary>
        /// <param name="entityName">Name of Entity</param>
        /// <param name="parameters">Key/Value collection where only the key is converted.</param>
        /// <returns>Dictionary where the keys now represent backing column names.</returns>
        public Dictionary<string, object?> GetBackingColumnsFromCollection(string entityName, IDictionary<string, object?> parameters)
        {
            Dictionary<string, object?> backingRowParams = new();

            foreach (KeyValuePair<string, object?> resultEntry in parameters)
            {
                _sqlMetadataProvider.TryGetBackingColumn(entityName, resultEntry.Key, out string? name);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    backingRowParams.Add(name, resultEntry.Value);
                }
                else
                {
                    backingRowParams.Add(resultEntry.Key, resultEntry.Value);
                }
            }

            return backingRowParams;
        }

        /// <summary>
        /// Executes the REST mutation query and returns IActionResult asynchronously.
        /// Result error cases differ for Stored Procedure requests than normal mutation requests
        /// QueryStructure built does not depend on Operation enum, thus not useful to use
        /// PerformMutationOperation method.
        /// </summary>
        public async Task<IActionResult?> ExecuteAsync(StoredProcedureRequestContext context)
        {
            SqlExecuteStructure executeQueryStructure = new(
                context.EntityName,
                _sqlMetadataProvider,
                _authorizationResolver,
                _gQLFilterParser,
                context.ResolvedParameters);
            string queryText = _queryBuilder.Build(executeQueryStructure);

            JsonArray? resultArray =
                await _queryExecutor.ExecuteQueryAsync(
                    queryText,
                    executeQueryStructure.Parameters,
                    _queryExecutor.GetJsonArrayAsync,
                    GetHttpContext());

            // A note on returning stored procedure results:
            // We can't infer what the stored procedure actually did beyond the HasRows and RecordsAffected attributes
            // of the DbDataReader. For example, we can't enforce that an UPDATE command outputs a result set using an OUTPUT
            // clause. As such, for this iteration we are just returning the success condition of the operation type that maps
            // to each action, with data always from the first result set, as there may be arbitrarily many.
            switch (context.OperationType)
            {
                case Config.Operation.Delete:
                    // Returns a 204 No Content so long as the stored procedure executes without error
                    return new NoContentResult();
                case Config.Operation.Insert:
                    // Returns a 201 Created with whatever the first result set is returned from the procedure
                    // A "correctly" configured stored procedure would INSERT INTO ... OUTPUT ... VALUES as the result set
                    if (resultArray is not null && resultArray.Count > 0)
                    {
                        using (JsonDocument jsonDocument = JsonDocument.Parse(resultArray.ToJsonString()))
                        {
                            return new CreatedResult(location: context.EntityName, OkMutationResponse(jsonDocument.RootElement.Clone()).Value);
                        }
                    }
                    else
                    {   // If no result set returned, just return a 201 Created with empty array instead of array with single null value
                        return new CreatedResult(
                            location: context.EntityName,
                            value: new
                            {
                                value = JsonDocument.Parse("[]").RootElement.Clone()
                            }
                        );
                    }
                case Config.Operation.Update:
                case Config.Operation.UpdateIncremental:
                case Config.Operation.Upsert:
                case Config.Operation.UpsertIncremental:
                    // Since we cannot check if anything was created, just return a 200 Ok response with first result set output
                    // A "correctly" configured stored procedure would UPDATE ... SET ... OUTPUT as the result set
                    if (resultArray is not null && resultArray.Count > 0)
                    {
                        using (JsonDocument jsonDocument = JsonDocument.Parse(resultArray.ToJsonString()))
                        {
                            return OkMutationResponse(jsonDocument.RootElement.Clone());
                        }
                    }
                    else
                    {
                        // If no result set returned, return 200 Ok response with empty array instead of array with single null value
                        return new OkObjectResult(
                            value: new
                            {
                                value = JsonDocument.Parse("[]").RootElement.Clone()
                            }
                        );
                    }

                default:
                    throw new DataApiBuilderException(
                        message: "Unsupported operation.",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }
        }

        /// <summary>
        /// Executes the REST mutation query and returns IActionResult asynchronously.
        /// </summary>
        /// <param name="context">context of REST mutation request.</param>
        /// <returns>IActionResult</returns>
        public async Task<IActionResult?> ExecuteAsync(RestRequestContext context)
        {
            Dictionary<string, object?> parameters = PrepareParameters(context);

            if (context.OperationType is Config.Operation.Delete)
            {
                Dictionary<string, object>? resultProperties =
                    await PerformDeleteOperation(
                        context.EntityName,
                        parameters);

                // Records affected tells us that item was successfully deleted.
                // No records affected happens for a DELETE request on nonexistent object
                if (resultProperties is not null
                    && resultProperties.TryGetValue(nameof(DbDataReader.RecordsAffected), out object? value)
                    && Convert.ToInt32(value) > 0)
                {
                    return new NoContentResult();
                }
            }
            else if (context.OperationType is Config.Operation.Upsert || context.OperationType is Config.Operation.UpsertIncremental)
            {
                DbResultSet? upsertOperationResult =
                    await PerformUpsertOperation(
                        parameters,
                        context);

                DbResultSetRow? dbResultSetRow = upsertOperationResult is not null ?
                    (upsertOperationResult.Rows.FirstOrDefault() ?? new()) : null;

                if (upsertOperationResult is not null &&
                    dbResultSetRow is not null && dbResultSetRow.Columns.Count > 0)
                {
                    Dictionary<string, object?> resultRow = dbResultSetRow.Columns;

                    bool isFirstResultSet = false;
                    if (upsertOperationResult.ResultProperties.TryGetValue(IS_FIRST_RESULT_SET, out object? isFirstResultSetValue))
                    {
                        isFirstResultSet = Convert.ToBoolean(isFirstResultSetValue);
                    }

                    // For MsSql, MySql, if it's not the first result, the upsert resulted in an INSERT operation.
                    // Even if its first result, postgresql may still be an insert op here, if so, return CreatedResult
                    if (!isFirstResultSet ||
                        (_sqlMetadataProvider.GetDatabaseType() is DatabaseType.postgresql &&
                        PostgresQueryBuilder.IsInsert(resultRow)))
                    {
                        string primaryKeyRoute = ConstructPrimaryKeyRoute(context, resultRow);
                        // location will be updated in rest controller where httpcontext is available
                        return new CreatedResult(location: primaryKeyRoute, OkMutationResponse(resultRow).Value);
                    }

                    // Valid REST updates return OkObjectResult
                    return OkMutationResponse(resultRow);
                }
            }
            else
            {
                DbResultSetRow? mutationResultRow =
                    await PerformMutationOperation(
                        context.EntityName,
                        context.OperationType,
                        parameters);

                if (context.OperationType is Config.Operation.Insert)
                {
                    if (mutationResultRow is null)
                    {
                        // this case should not happen, we throw an exception
                        // which will be returned as an Unexpected Internal Server Error
                        throw new Exception();
                    }

                    if (mutationResultRow.Columns.Count == 0)
                    {
                        throw new DataApiBuilderException(
                            message: "Could not insert row with given values.",
                            statusCode: HttpStatusCode.Forbidden,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed
                            );
                    }

                    string primaryKeyRoute = ConstructPrimaryKeyRoute(context, mutationResultRow.Columns);
                    // location will be updated in rest controller where httpcontext is available
                    return new CreatedResult(location: primaryKeyRoute, OkMutationResponse(mutationResultRow.Columns).Value);
                }

                if (context.OperationType is Config.Operation.Update || context.OperationType is Config.Operation.UpdateIncremental)
                {
                    // Nothing to update means we throw Exception
                    if (mutationResultRow is null || mutationResultRow.Columns.Count == 0)
                    {
                        throw new DataApiBuilderException(message: "No Update could be performed, record not found",
                                                           statusCode: HttpStatusCode.PreconditionFailed,
                                                           subStatusCode: DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed);
                    }

                    // Valid REST updates return OkObjectResult
                    return OkMutationResponse(mutationResultRow.Columns);
                }
            }

            // if we have not yet returned, record is null
            return null;
        }

        /// <summary>
        /// Helper function returns an OkObjectResult with provided arguments in a
        /// form that complies with vNext Api guidelines.
        /// </summary>
        /// <param name="result">Dictionary representing the results of the client's request.</param>
        private static OkObjectResult OkMutationResponse(Dictionary<string, object?>? result)
        {
            // Convert Dictionary to array of JsonElements
            string jsonString = $"[{JsonSerializer.Serialize(result)}]";
            JsonElement jsonResult = JsonSerializer.Deserialize<JsonElement>(jsonString);
            IEnumerable<JsonElement> resultEnumerated = jsonResult.EnumerateArray();

            return new OkObjectResult(new
            {
                value = resultEnumerated
            });
        }

        /// <summary>
        /// Helper function returns an OkObjectResult with provided arguments in a
        /// form that complies with vNext Api guidelines.
        /// The result is converted to a JSON Array if the result is not of that type already.
        /// </summary>
        /// <seealso>https://github.com/microsoft/api-guidelines/blob/vNext/Guidelines.md#92-serialization</seealso>
        /// <param name="jsonResult">Value representing the Json results of the client's request.</param>
        private static OkObjectResult OkMutationResponse(JsonElement jsonResult)
        {
            if (jsonResult.ValueKind != JsonValueKind.Array)
            {
                string jsonString = $"[{JsonSerializer.Serialize(jsonResult)}]";
                jsonResult = JsonSerializer.Deserialize<JsonElement>(jsonString);
            }

            IEnumerable<JsonElement> resultEnumerated = jsonResult.EnumerateArray();

            return new OkObjectResult(new
            {
                value = resultEnumerated
            });
        }

        /// <summary>
        /// Performs the given REST and GraphQL mutation operation of type
        /// Insert, Create, Update, UpdateIncremental, UpdateGraphQL
        /// on the source backing the given entity.
        /// </summary>
        /// <param name="entityName">The name of the entity on which mutation is to be performed.</param>
        /// <param name="operationType">The type of mutation operation.
        /// This cannot be Delete, Upsert or UpsertIncremental since those operations have dedicated functions.</param>
        /// <param name="parameters">The parameters of the mutation query.</param>
        /// <param name="context">In the case of GraphQL, the HotChocolate library's middleware context.</param>
        /// <returns>Single row read from DbDataReader.</returns>
        private async Task<DbResultSetRow?>
            PerformMutationOperation(
                string entityName,
                Config.Operation operationType,
                IDictionary<string, object?> parameters,
                IMiddlewareContext? context = null)
        {
            string queryString;
            Dictionary<string, object?> queryParameters;
            switch (operationType)
            {
                case Config.Operation.Insert:
                case Config.Operation.Create:
                    SqlInsertStructure insertQueryStruct = context is null
                        ? new(
                            entityName,
                            _sqlMetadataProvider,
                            _authorizationResolver,
                            _gQLFilterParser,
                            parameters,
                            GetHttpContext())
                        : new(
                            context,
                            entityName,
                            _sqlMetadataProvider,
                            _authorizationResolver,
                            _gQLFilterParser,
                            parameters,
                            GetHttpContext());
                    queryString = _queryBuilder.Build(insertQueryStruct);
                    queryParameters = insertQueryStruct.Parameters;
                    break;
                case Config.Operation.Update:
                    SqlUpdateStructure updateStructure = new(
                        entityName,
                        _sqlMetadataProvider,
                        _authorizationResolver,
                        _gQLFilterParser,
                        parameters,
                        GetHttpContext(),
                        isIncrementalUpdate: false);
                    queryString = _queryBuilder.Build(updateStructure);
                    queryParameters = updateStructure.Parameters;
                    break;
                case Config.Operation.UpdateIncremental:
                    SqlUpdateStructure updateIncrementalStructure = new(
                        entityName,
                        _sqlMetadataProvider,
                        _authorizationResolver,
                        _gQLFilterParser,
                        parameters,
                        GetHttpContext(),
                        isIncrementalUpdate: true);
                    queryString = _queryBuilder.Build(updateIncrementalStructure);
                    queryParameters = updateIncrementalStructure.Parameters;
                    break;
                case Config.Operation.UpdateGraphQL:
                    if (context is null)
                    {
                        throw new ArgumentNullException("Context should not be null for a GraphQL operation.");
                    }

                    SqlUpdateStructure updateGraphQLStructure = new(
                        context,
                        entityName,
                        _sqlMetadataProvider,
                        _authorizationResolver,
                        _gQLFilterParser,
                        parameters,
                        GetHttpContext());
                    queryString = _queryBuilder.Build(updateGraphQLStructure);
                    queryParameters = updateGraphQLStructure.Parameters;
                    break;
                default:
                    throw new NotSupportedException($"Unexpected mutation operation \" {operationType}\" requested.");
            }

            DbResultSet? dbResultSet;
            DbResultSetRow? dbResultSetRow;

            if (context is not null && !context.Selection.Type.IsScalarType())
            {
                SourceDefinition sourceDefinition = _sqlMetadataProvider.GetSourceDefinition(entityName);

                // To support GraphQL field mappings (DB column aliases), convert the sourceDefinition
                // primary key column names (backing columns) to the exposed (mapped) column names to
                // identify primary key column names in the mutation result set.
                List<string> primaryKeyExposedColumnNames = new();
                foreach (string primaryKey in sourceDefinition.PrimaryKey)
                {
                    if (_sqlMetadataProvider.TryGetExposedColumnName(entityName, primaryKey, out string? name) && !string.IsNullOrWhiteSpace(name))
                    {
                        primaryKeyExposedColumnNames.Add(name);
                    }
                }

                // Only extract pk columns since non pk columns can be null
                // and the subsequent query would search with:
                // nullParamName = NULL
                // which would fail to get the mutated entry from the db
                // When no exposed column names were resolved, it is safe to provide
                // backing column names (sourceDefinition.Primary) as a list of arguments.
                dbResultSet =
                    await _queryExecutor.ExecuteQueryAsync(
                        queryString,
                        queryParameters,
                        _queryExecutor.ExtractResultSetFromDbDataReader,
                        GetHttpContext(),
                        primaryKeyExposedColumnNames.Count > 0 ? primaryKeyExposedColumnNames : sourceDefinition.PrimaryKey);

                dbResultSetRow = dbResultSet is not null ?
                    (dbResultSet.Rows.FirstOrDefault() ?? new DbResultSetRow()) : null;
                if (dbResultSetRow is not null && dbResultSetRow.Columns.Count == 0)
                {
                    // For GraphQL, insert operation corresponds to Create action.
                    if (operationType is Config.Operation.Create)
                    {
                        throw new DataApiBuilderException(
                            message: "Could not insert row with given values.",
                            statusCode: HttpStatusCode.Forbidden,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed
                            );
                    }

                    string searchedPK;
                    if (primaryKeyExposedColumnNames.Count > 0)
                    {
                        searchedPK = '<' + string.Join(", ", primaryKeyExposedColumnNames.Select(pk => $"{pk}: {parameters[pk]}")) + '>';
                    }
                    else
                    {
                        searchedPK = '<' + string.Join(", ", sourceDefinition.PrimaryKey.Select(pk => $"{pk}: {parameters[pk]}")) + '>';
                    }

                    throw new DataApiBuilderException(
                        message: $"Could not find entity with {searchedPK}",
                        statusCode: HttpStatusCode.NotFound,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.EntityNotFound);
                }
            }
            else
            {
                // This is the scenario for all REST mutation operations covered by this function
                // and the case when the Selection Type is a scalar for GraphQL.
                dbResultSet =
                    await _queryExecutor.ExecuteQueryAsync(
                        queryString,
                        queryParameters,
                        _queryExecutor.ExtractResultSetFromDbDataReader,
                        GetHttpContext());
                dbResultSetRow = dbResultSet is not null ? (dbResultSet.Rows.FirstOrDefault() ?? new()) : null;
            }

            return dbResultSetRow;
        }

        /// <summary>
        /// Perform the DELETE operation on the given entity.
        /// To determine the correct response, uses QueryExecutor's GetResultProperties handler for
        /// obtaining the db data reader properties like RecordsAffected, HasRows.
        /// </summary>
        /// <param name="entityName">The name of the entity.</param>
        /// <param name="parameters">The parameters for the DELETE operation.</param>
        /// <returns>A dictionary of properties of the Db Data Reader like RecordsAffected, HasRows.</returns>
        private async Task<Dictionary<string, object>?>
            PerformDeleteOperation(
                string entityName,
                IDictionary<string, object?> parameters)
        {
            string queryString;
            Dictionary<string, object?> queryParameters;
            SqlDeleteStructure deleteStructure = new(
                entityName,
                _sqlMetadataProvider,
                _authorizationResolver,
                _gQLFilterParser,
                parameters,
                GetHttpContext());
            queryString = _queryBuilder.Build(deleteStructure);
            queryParameters = deleteStructure.Parameters;

            Dictionary<string, object>?
                resultProperties = await _queryExecutor.ExecuteQueryAsync(
                    queryString,
                    queryParameters,
                    _queryExecutor.GetResultProperties,
                    GetHttpContext());

            return resultProperties;
        }

        /// <summary>
        /// Perform an Upsert or UpsertIncremental operation on the given entity.
        /// Since Upsert operations could simply be an update or result in an insert,
        /// uses QueryExecutor's GetMultipleResultSetsIfAnyAsync as the data reader handler.
        /// </summary>
        /// <param name="parameters">The parameters for the mutation query.</param>
        /// <param name="context">The REST request context.</param>
        /// <returns>Single row read from DbDataReader.</returns>
        private async Task<DbResultSet?>
            PerformUpsertOperation(
                IDictionary<string, object?> parameters,
                RestRequestContext context)
        {
            string queryString;
            Dictionary<string, object?> queryParameters;
            Config.Operation operationType = context.OperationType;
            string entityName = context.EntityName;

            if (operationType is Config.Operation.Upsert)
            {
                SqlUpsertQueryStructure upsertStructure = new(
                    entityName,
                    _sqlMetadataProvider,
                    _authorizationResolver,
                    _gQLFilterParser,
                    parameters,
                    httpContext: GetHttpContext(),
                    incrementalUpdate: false);
                queryString = _queryBuilder.Build(upsertStructure);
                queryParameters = upsertStructure.Parameters;
            }
            else
            {
                SqlUpsertQueryStructure upsertIncrementalStructure = new(
                    entityName,
                    _sqlMetadataProvider,
                    _authorizationResolver,
                    _gQLFilterParser,
                    parameters,
                    httpContext: GetHttpContext(),
                    incrementalUpdate: true);
                queryString = _queryBuilder.Build(upsertIncrementalStructure);
                queryParameters = upsertIncrementalStructure.Parameters;
            }

            string prettyPrintPk = "<" + string.Join(", ", context.PrimaryKeyValuePairs.Select(
                kv_pair => $"{kv_pair.Key}: {kv_pair.Value}"
                )) + ">";

            return await _queryExecutor.ExecuteQueryAsync(
                       queryString,
                       queryParameters,
                       _queryExecutor.GetMultipleResultSetsIfAnyAsync,
                       GetHttpContext(),
                       new List<string> { prettyPrintPk, entityName });
        }

        /// <summary>
        /// For the given entity, constructs the primary key route
        /// using the primary key names from metadata and their values
        /// from the JsonElement representing the entity.
        /// </summary>
        /// <param name="entityName">Name of the entity.</param>
        /// <param name="entity">A Json element representing one instance of the entity.</param>
        /// <remarks> This function expects the Json element entity to contain all the properties
        /// that make up the primary keys.</remarks>
        /// <returns>the primary key route e.g. /id/1/partition/2 where id and partition are primary keys.</returns>
        public string ConstructPrimaryKeyRoute(RestRequestContext context, Dictionary<string, object?> entity)
        {
            if (context.DatabaseObject.SourceType is SourceType.View)
            {
                return string.Empty;
            }

            string entityName = context.EntityName;
            SourceDefinition sourceDefinition = _sqlMetadataProvider.GetSourceDefinition(entityName);
            StringBuilder newPrimaryKeyRoute = new();

            foreach (string primaryKey in sourceDefinition.PrimaryKey)
            {
                // get backing column for lookup, previously validated to be non-null
                _sqlMetadataProvider.TryGetExposedColumnName(entityName, primaryKey, out string? pkExposedName);
                newPrimaryKeyRoute.Append(pkExposedName);
                newPrimaryKeyRoute.Append("/");
                newPrimaryKeyRoute.Append(entity[pkExposedName!]!.ToString());
                newPrimaryKeyRoute.Append("/");
            }

            // Remove the trailing "/"
            newPrimaryKeyRoute.Remove(newPrimaryKeyRoute.Length - 1, 1);

            return newPrimaryKeyRoute.ToString();
        }

        private static Dictionary<string, object?> PrepareParameters(RestRequestContext context)
        {
            Dictionary<string, object?> parameters;

            switch (context.OperationType)
            {
                case Config.Operation.Delete:
                    // DeleteOne based off primary key in request.
                    parameters = new(context.PrimaryKeyValuePairs!);
                    break;
                case Config.Operation.Upsert:
                case Config.Operation.UpsertIncremental:
                case Config.Operation.Update:
                case Config.Operation.UpdateIncremental:
                    // Combine both PrimaryKey/Field ValuePairs
                    // because we create an update statement.
                    parameters = new(context.PrimaryKeyValuePairs!);
                    foreach (KeyValuePair<string, object?> pair in context.FieldValuePairsInBody)
                    {
                        parameters.Add(pair.Key, pair.Value);
                    }

                    break;
                default:
                    parameters = new(context.FieldValuePairsInBody);
                    break;
            }

            return parameters;
        }

        /// <summary>
        /// Authorization check on mutation fields provided in a GraphQL Mutation request.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="parameters"></param>
        /// <param name="entityName"></param>
        /// <param name="mutationOperation"></param>
        /// <exception cref="DataApiBuilderException"></exception>
        public void AuthorizeMutationFields(
            IMiddlewareContext context,
            IDictionary<string, object?> parameters,
            string entityName,
            Config.Operation mutationOperation)
        {
            string role = string.Empty;
            if (context.ContextData.TryGetValue(key: AuthorizationResolver.CLIENT_ROLE_HEADER, out object? value) && value is StringValues stringVals)
            {
                role = stringVals.ToString();
            }

            if (string.IsNullOrEmpty(role))
            {
                throw new DataApiBuilderException(
                    message: "No ClientRoleHeader available to perform authorization.",
                    statusCode: HttpStatusCode.Unauthorized,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed);
            }

            List<string> inputArgumentKeys;
            if (mutationOperation != Config.Operation.Delete)
            {
                inputArgumentKeys = BaseSqlQueryStructure.GetSubArgumentNamesFromGQLMutArguments(MutationBuilder.INPUT_ARGUMENT_NAME, parameters);
            }
            else
            {
                inputArgumentKeys = parameters.Keys.ToList();
            }

            bool isAuthorized; // False by default.

            switch (mutationOperation)
            {
                case Config.Operation.UpdateGraphQL:
                    isAuthorized = _authorizationResolver.AreColumnsAllowedForOperation(entityName, roleName: role, operation: Config.Operation.Update, inputArgumentKeys);
                    break;
                case Config.Operation.Create:
                    isAuthorized = _authorizationResolver.AreColumnsAllowedForOperation(entityName, roleName: role, operation: mutationOperation, inputArgumentKeys);
                    break;
                case Config.Operation.Execute:
                case Config.Operation.Delete:
                    // Authorization is not performed for the 'execute' operation because stored procedure
                    // backed entities do not support column level authorization.
                    // Field level authorization is not supported for delete mutations. A requestor must be authorized
                    // to perform the delete operation on the entity to reach this point.
                    isAuthorized = true;
                    break;
                default:
                    throw new DataApiBuilderException(
                        message: "Invalid operation for GraphQL Mutation, must be Create, UpdateGraphQL, or Delete",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest
                        );
            }

            if (!isAuthorized)
            {
                throw new DataApiBuilderException(
                    message: "Unauthorized due to one or more fields in this mutation.",
                    statusCode: HttpStatusCode.Forbidden,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed
                );
            }
        }

        /// <summary>
        /// Gets the httpContext for the current request.
        /// </summary>
        /// <returns>Request's httpContext.</returns>
        private HttpContext GetHttpContext()
        {
            return _httpContextAccessor.HttpContext!;
        }
    }
}
