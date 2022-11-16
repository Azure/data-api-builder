using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using System.Text.Json.Nodes;

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
        private readonly ILogger<SqlMutationEngine> _logger;
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
            IHttpContextAccessor httpContextAccessor,
            ILogger<SqlMutationEngine> logger)
        {
            _queryEngine = queryEngine;
            _queryExecutor = queryExecutor;
            _queryBuilder = queryBuilder;
            _sqlMetadataProvider = sqlMetadataProvider;
            _authorizationResolver = authorizationResolver;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
        }

        /// <summary>
        /// Executes the GraphQL mutation query and returns result as JSON object asynchronously.
        /// </summary>
        /// <param name="context">context of graphql mutation</param>
        /// <param name="parameters">parameters in the mutation query.</param>
        /// <returns>JSON object result and its related pagination metadata</returns>
        public async Task<Tuple<JsonDocument, IMetadata>> ExecuteAsync(IMiddlewareContext context, IDictionary<string, object?> parameters)
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

            Tuple<JsonDocument, IMetadata>? result = null;
            Operation mutationOperation =
                MutationBuilder.DetermineMutationOperationTypeBasedOnInputType(graphqlMutationName);

            // If authorization fails, an exception will be thrown and request execution halts.
            AuthorizeMutationFields(context, parameters, entityName, mutationOperation);

            if (mutationOperation is Operation.Delete)
            {
                // compute the mutation result before removing the element,
                // since typical GraphQL delete mutations return the metadata of the deleted item.
                result = await _queryEngine.ExecuteAsync(context, parameters);

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
                    result = new Tuple<JsonDocument, IMetadata>(
                        default(JsonDocument)!,
                        PaginationMetadata.MakeEmptyPaginationMetadata());
                }
            }
            else
            {
                Tuple<Dictionary<string, object?>?, Dictionary<string, object>>? resultRowAndProperties =
                    await PerformMutationOperation(
                        entityName,
                        mutationOperation,
                        parameters,
                        context);

                if (resultRowAndProperties is not null && resultRowAndProperties.Item1 is not null
                    && !context.Selection.Type.IsScalarType())
                {
                    result = await _queryEngine.ExecuteAsync(
                                context,
                                resultRowAndProperties.Item1);
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
        /// Executes the REST mutation query and returns IActionResult asynchronously.
        /// Result error cases differ for Stored Procedure requests than normal mutation requests
        /// QueryStructure built does not depend on Operation enum, thus not useful to use
        /// PerformMutationOperation method.
        /// </summary>
        public async Task<IActionResult?> ExecuteAsync(StoredProcedureRequestContext context)
        {
            SqlExecuteStructure executeQueryStructure = new(context.EntityName, _sqlMetadataProvider, context.ResolvedParameters!);
            string queryText = _queryBuilder.Build(executeQueryStructure);
            _logger.LogInformation(queryText);

            Tuple<Dictionary<string, object?>?, Dictionary<string, object>>? resultRowAndProperties =
                await _queryExecutor.ExecuteQueryAsync(
                    queryText,
                    executeQueryStructure.Parameters,
                    _queryExecutor.ExtractRowFromDbDataReader);

            // A note on returning stored procedure results:
            // We can't infer what the stored procedure actually did beyond the HasRows and RecordsAffected attributes
            // of the DbDataReader. For example, we can't enforce that an UPDATE command outputs a result set using an OUTPUT
            // clause. As such, for this iteration we are just returning the success condition of the operation type that maps
            // to each action, with data always from the first result set, as there may be arbitrarily many.
            switch (context.OperationType)
            {
                case Operation.Delete:
                    // Returns a 204 No Content so long as the stored procedure executes without error
                    return new NoContentResult();
                case Operation.Insert:
                    // Returns a 201 Created with whatever the first result set is returned from the procedure
                    // A "correctly" configured stored procedure would INSERT INTO ... OUTPUT ... VALUES as the first and only result set
                    if (resultRowAndProperties is not null &&
                        DoesResultHaveRows(resultRowAndProperties.Item2))
                    {
                        return new CreatedResult(location: context.EntityName, OkMutationResponse(resultRowAndProperties.Item1).Value);
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
                case Operation.Update:
                case Operation.UpdateIncremental:
                case Operation.Upsert:
                case Operation.UpsertIncremental:
                    // Since we cannot check if anything was created, just return a 200 Ok response with first result set output
                    // A "correctly" configured stored procedure would UPDATE ... SET ... OUTPUT as the first and only result set
                    if (resultRowAndProperties is not null &&
                        DoesResultHaveRows(resultRowAndProperties.Item2))
                    {
                        return OkMutationResponse(resultRowAndProperties.Item1);
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

            if (context.OperationType is Operation.Delete)
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
            else if (context.OperationType is Operation.Upsert || context.OperationType is Operation.UpsertIncremental)
            {
                Tuple<Dictionary<string, object?>?, Dictionary<string, object>>? resultRowAndProperties =
                    await PerformUpsertOperation(
                        parameters,
                        context);

                if (resultRowAndProperties is not null &&
                    resultRowAndProperties.Item1 is not null)
                {
                    Dictionary<string, object?> resultRow = resultRowAndProperties.Item1;

                    bool isFirstResultSet = false;
                    if (resultRowAndProperties.Item2.TryGetValue(IS_FIRST_RESULT_SET, out object? isFirstResultSetValue))
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
                Tuple<Dictionary<string, object?>?, Dictionary<string, object>>? resultRowAndProperties =
                    await PerformMutationOperation(
                        context.EntityName,
                        context.OperationType,
                        parameters);

                if (context.OperationType is Operation.Insert)
                {
                    if (resultRowAndProperties is null || resultRowAndProperties.Item1 is null)
                    {
                        // this case should not happen, we throw an exception
                        // which will be returned as an Unexpected Internal Server Error
                        throw new Exception();
                    }

                    Dictionary<string, object?> resultRow = resultRowAndProperties.Item1;
                    string primaryKeyRoute = ConstructPrimaryKeyRoute(context, resultRow);
                    // location will be updated in rest controller where httpcontext is available
                    return new CreatedResult(location: primaryKeyRoute, OkMutationResponse(resultRow).Value);
                }

                if (context.OperationType is Operation.Update || context.OperationType is Operation.UpdateIncremental)
                {
                    // Nothing to update means we throw Exception
                    if (resultRowAndProperties is null ||
                        resultRowAndProperties.Item1 is null ||
                        resultRowAndProperties.Item1.Count == 0)
                    {
                        throw new DataApiBuilderException(message: "No Update could be performed, record not found",
                                                           statusCode: HttpStatusCode.PreconditionFailed,
                                                           subStatusCode: DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed);
                    }

                    // Valid REST updates return OkObjectResult
                    return OkMutationResponse(resultRowAndProperties.Item1);
                }
            }

            // if we have not yet returned, record is null
            return null;
        }

        private async Task<JsonDocument> ExecuteAsync(SqlExecuteStructure structure)
        {
            string queryString = _queryBuilder.Build(structure);
            _logger.LogInformation(queryString);

            // JsonArray resultArray =
            //     await _queryExecutor.ExecuteQueryAsync(
            //         queryString,
            //         structure.Parameters,
            //         _queryExecutor.GetJsonArrayAsync);

            JsonDocument jsonDocument = null;

            // If result set is non-empty, parse rows from json array into JsonDocument
            // if (resultArray is not null && resultArray.Count > 0)
            // {
            //     jsonDocument = JsonDocument.Parse(resultArray.ToJsonString());
            // }
            // else
            // {
            //     _logger.LogInformation("Did not return enough rows.");
            // }

            return jsonDocument;
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
        /// Performs the given REST and GraphQL mutation operation of type
        /// Insert, Create, Update, UpdateIncremental, UpdateGraphQL
        /// on the source backing the given entity.
        /// </summary>
        /// <param name="entityName">The name of the entity on which mutation is to be performed.</param>
        /// <param name="operationType">The type of mutation operation.
        /// This cannot be Delete, Upsert or UpsertIncremental since those operations have dedicated functions.</param>
        /// <param name="parameters">The parameters of the mutation query.</param>
        /// <param name="context">In the case of GraphQL, the HotChocolate library's middleware context.</param>
        /// <returns>A tuple of 2 dictionaries:
        /// 1. A dictionary representing the row in <c>ColumnName: Value</c> format, null if no row is mutated.
        /// 2. A dictionary of properties of the Db Data Reader like RecordsAffected, HasRows.</returns>
        private async Task<Tuple<Dictionary<string, object?>?, Dictionary<string, object>>?>
            PerformMutationOperation(
                string entityName,
                Operation operationType,
                IDictionary<string, object?> parameters,
                IMiddlewareContext? context = null)
        {
            string queryString;
            Dictionary<string, object?> queryParameters;
            switch (operationType)
            {
                case Operation.Insert:
                case Operation.Create:
                    SqlInsertStructure insertQueryStruct = context is null ?
                        new(entityName,
                        _sqlMetadataProvider,
                        parameters) :
                        new(context, entityName, _sqlMetadataProvider, parameters);
                    queryString = _queryBuilder.Build(insertQueryStruct);
                    queryParameters = insertQueryStruct.Parameters;
                    break;
                case Operation.Update:
                    SqlUpdateStructure updateStructure =
                        new(entityName,
                        _sqlMetadataProvider,
                        parameters,
                        isIncrementalUpdate: false);
                    queryString = _queryBuilder.Build(updateStructure);
                    queryParameters = updateStructure.Parameters;
                    break;
                case Operation.UpdateIncremental:
                    SqlUpdateStructure updateIncrementalStructure =
                        new(entityName,
                        _sqlMetadataProvider,
                        parameters,
                        isIncrementalUpdate: true);
                    queryString = _queryBuilder.Build(updateIncrementalStructure);
                    queryParameters = updateIncrementalStructure.Parameters;
                    break;
                case Operation.UpdateGraphQL:
                    if (context is null)
                    {
                        throw new ArgumentNullException("Context should not be null for a GraphQL operation.");
                    }

                    SqlUpdateStructure updateGraphQLStructure =
                        new(
                        context,
                        entityName,
                        _sqlMetadataProvider,
                        parameters);
                    AuthorizationPolicyHelpers.ProcessAuthorizationPolicies(
                        Operation.Update,
                        updateGraphQLStructure,
                        _httpContextAccessor.HttpContext!,
                        _authorizationResolver,
                        _sqlMetadataProvider);
                    queryString = _queryBuilder.Build(updateGraphQLStructure);
                    queryParameters = updateGraphQLStructure.Parameters;
                    break;
                default:
                    throw new NotSupportedException($"Unexpected mutation operation \" {operationType}\" requested.");
            }

            _logger.LogInformation(queryString);

            Tuple<Dictionary<string, object?>?, Dictionary<string, object>>? resultRecord = null;

            if (context is not null && !context.Selection.Type.IsScalarType())
            {
                SourceDefinition sourceDefinition = _sqlMetadataProvider.GetSourceDefinition(entityName);

                // only extract pk columns
                // since non pk columns can be null
                // and the subsequent query would search with:
                // nullParamName = NULL
                // which would fail to get the mutated entry from the db
                resultRecord =
                    await _queryExecutor.ExecuteQueryAsync(
                        queryString,
                        queryParameters,
                        _queryExecutor.ExtractRowFromDbDataReader,
                        sourceDefinition.PrimaryKey);

                if (resultRecord is not null && resultRecord.Item1 is null)
                {
                    string searchedPK = '<' + string.Join(", ", sourceDefinition.PrimaryKey.Select(pk => $"{pk}: {parameters[pk]}")) + '>';
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
                resultRecord =
                    await _queryExecutor.ExecuteQueryAsync(
                        queryString,
                        queryParameters,
                        _queryExecutor.ExtractRowFromDbDataReader);
            }

            return resultRecord;
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
            SqlDeleteStructure deleteStructure =
                new(entityName,
                _sqlMetadataProvider,
                parameters);
            AuthorizationPolicyHelpers.ProcessAuthorizationPolicies(
                Operation.Delete,
                deleteStructure,
                _httpContextAccessor.HttpContext!,
                _authorizationResolver,
                _sqlMetadataProvider);
            queryString = _queryBuilder.Build(deleteStructure);
            queryParameters = deleteStructure.Parameters;
            _logger.LogInformation(queryString);

            Dictionary<string, object>?
                resultProperties = await _queryExecutor.ExecuteQueryAsync(
                    queryString,
                    queryParameters,
                    _queryExecutor.GetResultProperties);

            return resultProperties;
        }

        /// <summary>
        /// Perform an Upsert or UpsertIncremental operation on the given entity.
        /// Since Upsert operations could simply be an update or result in an insert,
        /// uses QueryExecutor's GetMultipleResultSetsIfAnyAsync as the data reader handler.
        /// </summary>
        /// <param name="parameters">The parameters for the mutation query.</param>
        /// <param name="context">The REST request context.</param>
        /// <returns>A tuple of 2 dictionaries:
        /// 1. A dictionary representing the row in <c>ColumnName: Value</c> format, null if no row was found
        /// 2. A dictionary of properties of the Db Data Reader like RecordsAffected, HasRows.</returns>
        private async Task<Tuple<Dictionary<string, object?>?, Dictionary<string, object>>?>
            PerformUpsertOperation(
                IDictionary<string, object?> parameters,
                RestRequestContext context)
        {
            string queryString;
            Dictionary<string, object?> queryParameters;
            Operation operationType = context.OperationType;
            string entityName = context.EntityName;

            if (operationType is Operation.Upsert)
            {
                SqlUpsertQueryStructure upsertStructure =
                        new(entityName,
                        _sqlMetadataProvider,
                        parameters,
                        incrementalUpdate: false);
                queryString = _queryBuilder.Build(upsertStructure);
                queryParameters = upsertStructure.Parameters;
            }
            else
            {
                SqlUpsertQueryStructure upsertIncrementalStructure =
                        new(entityName,
                        _sqlMetadataProvider,
                        parameters,
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
                case Operation.Delete:
                    // DeleteOne based off primary key in request.
                    parameters = new(context.PrimaryKeyValuePairs!);
                    break;
                case Operation.Upsert:
                case Operation.UpsertIncremental:
                case Operation.Update:
                case Operation.UpdateIncremental:
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
        /// Checks if the given dictionary has a property named `HasRows`
        /// and if its true.
        /// </summary>
        /// <param name="properties">A dictionary of properties of a Db Data Reader like RecordsAffected, HasRows.</param>
        /// <returns>True if HasRows is true, false otherwise.</returns>
        private static bool DoesResultHaveRows(Dictionary<string, object> properties)
        {
            return properties.TryGetValue(nameof(DbDataReader.HasRows), out object? hasRows) &&
                   Convert.ToBoolean(hasRows);
        }

        /// <summary>
        /// Authorization check on mutation fields provided in a GraphQL Mutation request.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="parameters"></param>
        /// <param name="entityName"></param>
        /// <param name="graphQLMutationName"></param>
        /// <param name="mutationOperation"></param>
        /// <returns></returns>
        /// <exception cref="DataApiBuilderException"></exception>
        public void AuthorizeMutationFields(
            IMiddlewareContext context,
            IDictionary<string, object?> parameters,
            string entityName,
            Operation mutationOperation)
        {
            string role = string.Empty;
            if (context.ContextData.TryGetValue(key: AuthorizationResolver.CLIENT_ROLE_HEADER, out object? value))
            {
                role = (StringValues)value!.ToString();
            }

            if (string.IsNullOrEmpty(role))
            {
                throw new DataApiBuilderException(
                    message: "No ClientRoleHeader available to perform authorization.",
                    statusCode: HttpStatusCode.Unauthorized,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed);
            }

            List<string> inputArgumentKeys;
            if (mutationOperation != Operation.Delete)
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
                case Operation.UpdateGraphQL:
                    isAuthorized = _authorizationResolver.AreColumnsAllowedForOperation(entityName, roleName: role, operation: Operation.Update, inputArgumentKeys);
                    break;
                case Operation.Create:
                    isAuthorized = _authorizationResolver.AreColumnsAllowedForOperation(entityName, roleName: role, operation: Operation.Create, inputArgumentKeys);
                    break;
                case Operation.Delete:
                    // Delete operations are not checked for authorization on field level,
                    // and instead at the mutation level and would be rejected before this time in the pipeline.
                    // Continuing on with operation.
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
    }
}
