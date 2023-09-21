// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data;
using System.Data.Common;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Transactions;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Mutations;
using HotChocolate.Resolvers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;

namespace Azure.DataApiBuilder.Core.Resolvers
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
        public const string IS_UPDATE_RESULT_SET = "IsUpdateResultSet";
        private const string TRANSACTION_EXCEPTION_ERROR_MSG = "An unexpected error occurred during the transaction execution";

        private static DataApiBuilderException _dabExceptionWithTransactionErrorMessage = new(message: TRANSACTION_EXCEPTION_ERROR_MSG,
                                                                                            statusCode: HttpStatusCode.InternalServerError,
                                                                                            subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError);

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
            EntityActionOperation mutationOperation = MutationBuilder.DetermineMutationOperationTypeBasedOnInputType(graphqlMutationName);

            // If authorization fails, an exception will be thrown and request execution halts.
            AuthorizeMutationFields(context, parameters, entityName, mutationOperation);

            try
            {
                // Creating an implicit transaction
                using (TransactionScope transactionScope = ConstructTransactionScopeBasedOnDbType())
                {
                    if (mutationOperation is EntityActionOperation.Delete)
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

                    transactionScope.Complete();
                }
            }
            // All the exceptions that can be thrown by .Complete() and .Dispose() methods of transactionScope
            // derive from TransactionException. Hence, TransactionException acts as a catch-all.
            // When an exception related to Transactions is encountered, the mutation is deemed unsuccessful and
            // a DataApiBuilderException is thrown
            catch (TransactionException)
            {
                throw _dabExceptionWithTransactionErrorMessage;
            }

            if (result is null)
            {
                throw new DataApiBuilderException(message: "Failed to resolve any query based on the current configuration.",
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

            JsonArray? resultArray = null;

            try
            {
                // Creating an implicit transaction
                using (TransactionScope transactionScope = ConstructTransactionScopeBasedOnDbType())
                {
                    resultArray =
                        await _queryExecutor.ExecuteQueryAsync(
                            queryText,
                            executeQueryStructure.Parameters,
                            _queryExecutor.GetJsonArrayAsync,
                            GetHttpContext());

                    transactionScope.Complete();
                }
            }

            // All the exceptions that can be thrown by .Complete() and .Dispose() methods of transactionScope
            // derive from TransactionException. Hence, TransactionException acts as a catch-all.
            // When an exception related to Transactions is encountered, the mutation is deemed unsuccessful and
            // a DataApiBuilderException is thrown
            catch (TransactionException)
            {
                throw _dabExceptionWithTransactionErrorMessage;
            }

            // A note on returning stored procedure results:
            // We can't infer what the stored procedure actually did beyond the HasRows and RecordsAffected attributes
            // of the DbDataReader. For example, we can't enforce that an UPDATE command outputs a result set using an OUTPUT
            // clause. As such, for this iteration we are just returning the success condition of the operation type that maps
            // to each action, with data always from the first result set, as there may be arbitrarily many.
            switch (context.OperationType)
            {
                case EntityActionOperation.Delete:
                    // Returns a 204 No Content so long as the stored procedure executes without error
                    return new NoContentResult();
                case EntityActionOperation.Insert:
                    // Returns a 201 Created with whatever the first result set is returned from the procedure
                    // A "correctly" configured stored procedure would INSERT INTO ... OUTPUT ... VALUES as the result set
                    if (resultArray is not null && resultArray.Count > 0)
                    {
                        using (JsonDocument jsonDocument = JsonDocument.Parse(resultArray.ToJsonString()))
                        {
                            // The final location header for stored procedures should be of the form ../api/<SP-Entity-Name>
                            // Location header is constructed using the base URL, base-route and the set location value.
                            // Since, SP-Entity-Name is already available in the base URL, location is set as an empty string.
                            return new CreatedResult(location: string.Empty, OkMutationResponse(jsonDocument.RootElement.Clone()).Value);
                        }
                    }
                    else
                    {   // If no result set returned, just return a 201 Created with empty array instead of array with single null value
                        return new CreatedResult(
                            location: string.Empty,
                            value: new
                            {
                                value = JsonDocument.Parse("[]").RootElement.Clone()
                            }
                        );
                    }
                case EntityActionOperation.Update:
                case EntityActionOperation.UpdateIncremental:
                case EntityActionOperation.Upsert:
                case EntityActionOperation.UpsertIncremental:
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

            if (context.OperationType is EntityActionOperation.Delete)
            {
                Dictionary<string, object>? resultProperties = null;

                try
                {
                    // Creating an implicit transaction
                    using (TransactionScope transactionScope = ConstructTransactionScopeBasedOnDbType())
                    {
                        resultProperties = await PerformDeleteOperation(
                                context.EntityName,
                                parameters);
                        transactionScope.Complete();
                    }
                }

                // All the exceptions that can be thrown by .Complete() and .Dispose() methods of transactionScope
                // derive from TransactionException. Hence, TransactionException acts as a catch-all.
                // When an exception related to Transactions is encountered, the mutation is deemed unsuccessful and
                // a DataApiBuilderException is thrown
                catch (TransactionException)
                {
                    throw _dabExceptionWithTransactionErrorMessage;
                }

                // Records affected tells us that item was successfully deleted.
                // No records affected happens for a DELETE request on nonexistent object
                if (resultProperties is not null
                    && resultProperties.TryGetValue(nameof(DbDataReader.RecordsAffected), out object? value)
                    && Convert.ToInt32(value) > 0)
                {
                    return new NoContentResult();
                }
            }
            else
            {
                string roleName = GetHttpContext().Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER];
                bool isReadPermissionConfiguredForRole = _authorizationResolver.AreRoleAndOperationDefinedForEntity(context.EntityName, roleName, EntityActionOperation.Read);
                bool isDatabasePolicyDefinedForReadAction = false;

                if (isReadPermissionConfiguredForRole)
                {
                    isDatabasePolicyDefinedForReadAction = _authorizationResolver.IsDBPolicyDefinedForRoleAndAction(context.EntityName, roleName, EntityActionOperation.Read);
                }

                if (context.OperationType is EntityActionOperation.Upsert || context.OperationType is EntityActionOperation.UpsertIncremental)
                {
                    DbResultSet? upsertOperationResult;
                    DbResultSetRow? upsertOperationResultSetRow = null;
                    JsonDocument? selectOperationResponse = null;
                    bool isUpdateResultSet = false;

                    try
                    {
                        // Creating an implicit transaction
                        using (TransactionScope transactionScope = ConstructTransactionScopeBasedOnDbType())
                        {
                            upsertOperationResult = await PerformUpsertOperation(
                                                                parameters,
                                                                context);

                            upsertOperationResultSetRow = upsertOperationResult is not null ?
                        (upsertOperationResult.Rows.FirstOrDefault() ?? new()) : null;

                            if (upsertOperationResult is not null &&
                                upsertOperationResultSetRow is not null &&
                                upsertOperationResultSetRow.Columns.Count > 0 &&
                                upsertOperationResult.ResultProperties.TryGetValue(IS_UPDATE_RESULT_SET, out object? isUpdateResultSetValue))
                            {

                                isUpdateResultSet = Convert.ToBoolean(isUpdateResultSetValue);                             
                            }

                            // The role with which the REST request is executed can have a database policy defined for the read action.
                            // In such a case, to get the results back, a select query which honors the database policy is excuted.
                            if (isDatabasePolicyDefinedForReadAction)
                            {
                                FindRequestContext findRequestContext = ConstructFindRequestContext(context, upsertOperationResultSetRow!, roleName);
                                selectOperationResponse = await _queryEngine.ExecuteAsyncAndGetResponseAsJsonDocument(findRequestContext);
                            }

                            transactionScope.Complete();
                        }
                    }

                    // All the exceptions that can be thrown by .Complete() and .Dispose() methods of transactionScope
                    // derive from TransactionException. Hence, TransactionException acts as a catch-all.
                    // When an exception related to Transactions is encountered, the mutation is deemed unsuccessful and
                    // a DataApiBuilderException is thrown
                    catch (TransactionException)
                    {
                        throw _dabExceptionWithTransactionErrorMessage;
                    }

                    Dictionary<string, object?> resultRow = upsertOperationResultSetRow!.Columns;

                    // For MsSql, MySql, if it's not the first result, the upsert resulted in an INSERT operation.
                    // With postgresql, even if it is the first result, the upsert could have resulted in an INSERT. So, that condition is evaluated.
                    if (_sqlMetadataProvider.GetDatabaseType() is DatabaseType.PostgreSQL)
                    {
                        isUpdateResultSet = !PostgresQueryBuilder.IsInsert(resultRow);
                    }

                    // When read permissions is configured without database policy, a subsequent select query will not be executed.
                    // However, the read action could have include and exclude fields configured. To honor that configuration setup,
                    // any additional fields that are present in the response are removed.
                    if (isReadPermissionConfiguredForRole && !isDatabasePolicyDefinedForReadAction)
                    {
                        IEnumerable<string> allowedExposedColumns = _authorizationResolver.GetAllowedExposedColumns(context.EntityName, roleName, EntityActionOperation.Read);
                        foreach (string columnInResponse in resultRow.Keys)
                        {
                            if (!allowedExposedColumns.Contains(columnInResponse))
                            {
                                resultRow.Remove(columnInResponse);
                            }
                        }
                    }

                    // When the upsert operation results in the creation of a new record, an HTTP 201 CreatedResult response is returned.
                    if (!isUpdateResultSet)
                    {
                        // Location Header is made up of the Base URL of the request and the primary key of the item created.
                        // However, for PATCH and PUT requests, the primary key would be present in the request URL. For POST request, however, the primary key
                        // would not be available in the URL and needs to be appened. Since, this is a PUT or PATCH request that has resulted in the creation of
                        // a new item, the URL already contains the primary key and hence, an empty string is passed as the primary key route.
                        return ConstructCreatedResultResponse(resultRow, selectOperationResponse, string.Empty, isReadPermissionConfiguredForRole, isDatabasePolicyDefinedForReadAction);
                    }

                    // When the upsert operation results in the update of an existing record, an HTTP 200 OK response is returned.
                    return ConstructOkMutationResponse(resultRow, selectOperationResponse, isReadPermissionConfiguredForRole, isDatabasePolicyDefinedForReadAction);
                }
                else
                {
                    DbResultSetRow? mutationResultRow = null;
                    JsonDocument? selectOperationResponse = null;

                    try
                    {
                        // Creating an implicit transaction
                        using (TransactionScope transactionScope = ConstructTransactionScopeBasedOnDbType())
                        {
                            mutationResultRow =
                                    await PerformMutationOperation(
                                        context.EntityName,
                                        context.OperationType,
                                        parameters);

                            if (context.OperationType is EntityActionOperation.Insert)
                            {
                                if (mutationResultRow is null)
                                {
                                    // Ideally this case should not happen, however may occur due to unexpected reasons,
                                    // like the DbDataReader being null. We throw an exception
                                    // which will be returned as an Unexpected InternalServerError
                                    throw new DataApiBuilderException(
                                        message: "An unexpected error occurred while trying to execute the query.",
                                        statusCode: HttpStatusCode.InternalServerError,
                                        subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError);
                                }

                                if (mutationResultRow.Columns.Count == 0)
                                {
                                    throw new DataApiBuilderException(
                                        message: "Could not insert row with given values.",
                                        statusCode: HttpStatusCode.Forbidden,
                                        subStatusCode: DataApiBuilderException.SubStatusCodes.DatabasePolicyFailure
                                        );
                                }

                            }

                            if (context.OperationType is EntityActionOperation.Update || context.OperationType is EntityActionOperation.UpdateIncremental)
                            {
                                // Nothing to update means we throw Exception
                                if (mutationResultRow is null || mutationResultRow.Columns.Count == 0)
                                {
                                    throw new DataApiBuilderException(message: "No Update could be performed, record not found",
                                                                       statusCode: HttpStatusCode.PreconditionFailed,
                                                                       subStatusCode: DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed);
                                }

                            }

                            // The role with which the REST request is executed can have database policies defined for the read action.
                            // When the database policy is defined for the read action, a select query that honors the database policy
                            // is executed to fetch the results.
                            if (isDatabasePolicyDefinedForReadAction)
                            {
                                FindRequestContext findRequestContext = ConstructFindRequestContext(context, mutationResultRow!, roleName);
                                selectOperationResponse = await _queryEngine.ExecuteAsyncAndGetResponseAsJsonDocument(findRequestContext);
                            }

                            transactionScope.Complete();
                        }
                    }

                    // All the exceptions that can be thrown by .Complete() and .Dispose() methods of transactionScope
                    // derive from TransactionException. Hence, TransactionException acts as a catch-all.
                    // When an exception related to Transactions is encountered, the mutation is deemed unsuccessful and
                    // a DataApiBuilderException is thrown
                    catch (TransactionException)
                    {
                        throw _dabExceptionWithTransactionErrorMessage;
                    }

                    string primaryKeyRoute = ConstructPrimaryKeyRoute(context, mutationResultRow!.Columns);

                    // When read permission is configured without a database policy, a subsequent select query will not be executed.
                    // So, if the read action has include/exclude fields configured, additional fields present in the response
                    // need to be removed.
                    if (isReadPermissionConfiguredForRole && !isDatabasePolicyDefinedForReadAction)
                    {
                        IEnumerable<string> allowedExposedColumns = _authorizationResolver.GetAllowedExposedColumns(context.EntityName, roleName, EntityActionOperation.Read);
                        foreach (string columnInResponse in mutationResultRow!.Columns.Keys)
                        {
                            if (!allowedExposedColumns.Contains(columnInResponse))
                            {
                                mutationResultRow!.Columns.Remove(columnInResponse);
                            }
                        }
                    }

                    if (context.OperationType is EntityActionOperation.Insert)
                    {
                        // Location Header is made up of the Base URL of the request and the primary key of the item created.
                        // However, for PATCH and PUT requests, the primary key would be present in the request URL. For POST request, however, the primary key
                        // would not be available in the URL and needs to be appened. So, the primary key of the newly created item which is stored in the primaryKeyRoute
                        // is used to construct the Location Header.
                        return ConstructCreatedResultResponse(mutationResultRow!.Columns, selectOperationResponse, primaryKeyRoute, isReadPermissionConfiguredForRole, isDatabasePolicyDefinedForReadAction);
                    }

                    if (context.OperationType is EntityActionOperation.Update || context.OperationType is EntityActionOperation.UpdateIncremental)
                    {
                        return ConstructOkMutationResponse(mutationResultRow!.Columns, selectOperationResponse, isReadPermissionConfiguredForRole, isDatabasePolicyDefinedForReadAction);
                    }
                }

            }

            

            // if we have not yet returned, record is null
            return null;
        }

        /// <summary>
        /// Constructs a FindRequestContext from the Insert/Upsert RequestContext and the results of insert/upsert database operation.
        /// For REST POST, PUT AND PATCH API reqeusts, when there are database policies defined for the read action,
        /// a subsequent select query that honors the database policy is executed to fetch the results.
        /// </summary>
        /// <param name="context">Insert/Upsert Request context for the REST POST, PUT and PATCH request</param>
        /// <param name="mutationResultRow">Result of the insert/upsert database operation</param>
        /// <param name="roleName">Role with which the API request is executed</param>
        /// <returns>Returns a FindRequestContext object constructed from the existing context and create/upsert operation results.</returns>
        private FindRequestContext ConstructFindRequestContext(RestRequestContext context, DbResultSetRow mutationResultRow, string roleName)
        {
            FindRequestContext findRequestContext = new(entityName: context.EntityName, dbo: context.DatabaseObject, isList: false);

            // PrimaryKeyValuePairs in the context is populated using the primary key values from the
            // results of the insert/update database operation.
            foreach (string primarykey in context.DatabaseObject.SourceDefinition.PrimaryKey)
            {
                _sqlMetadataProvider.TryGetBackingColumn(context.EntityName, primarykey, out string? backingColumnName);
                if (!string.IsNullOrEmpty(backingColumnName))
                {
                    findRequestContext.PrimaryKeyValuePairs.Add(backingColumnName, value: mutationResultRow.Columns[primarykey]!);
                }
                else
                {
                    findRequestContext.PrimaryKeyValuePairs.Add(primarykey, value: mutationResultRow.Columns[primarykey]!);
                }
            }

            // READ action for the given role can have include and exclude fields configured. Populating UpdateReturnFields
            // ensures that the select query retrieves only those fields that are allowed for the given role.
            findRequestContext.UpdateReturnFields(_authorizationResolver.GetAllowedExposedColumns(context.EntityName, roleName, EntityActionOperation.Read));

            return findRequestContext;
        }

        /// <summary>
        /// Constructs and returns a HTTP 201 Created response.
        /// The response is constructed using results of the upsert database operation when database policy is not defined for the read permission.
        /// If database policy is defined, the results of the subsequent select statement is used for constructing the response.
        /// </summary>
        /// <param name="resultRow">Reuslt of the upsert database operation</param>
        /// <param name="jsonDocument">Result of the select database operation</param>
        /// <param name="isReadPermissionConfiguredForRole">Indicates whether read permissions is configured for the role</param>
        /// <param name="isDatabasePolicyDefinedForReadAction">Indicates whether database policy is configured for read action</param>
        private static CreatedResult ConstructCreatedResultResponse(Dictionary<string, object?> resultRow, JsonDocument? jsonDocument, string primaryKeyRoute, bool isReadPermissionConfiguredForRole, bool isDatabasePolicyDefinedForReadAction)
        {
            // When the database policy is defined for the read action, a subsequent select query will be executed to fetch the results.
            // So, the response of that database query is used to construct the final response to be returned.
            if (isDatabasePolicyDefinedForReadAction)
            {
                return (jsonDocument is not null) ? new CreatedResult(location: primaryKeyRoute, OkMutationResponse(jsonDocument.RootElement.Clone()).Value)
                                                  : new CreatedResult(location: primaryKeyRoute, OkMutationResponse(JsonDocument.Parse("[]").RootElement.Clone()).Value);
            }

            // When no database policy is defined for the read action, the reuslts from the upsert database operation is
            // used to construct the final response.
            // When no read permissions are configured for the role, or all the fields are excluded
            // an empty response is returned.
            return (isReadPermissionConfiguredForRole && resultRow.Count > 0) ? new CreatedResult(location: primaryKeyRoute, OkMutationResponse(resultRow).Value)
                                                     : new CreatedResult(location: primaryKeyRoute, OkMutationResponse(JsonDocument.Parse("[]").RootElement.Clone()).Value);
        }

        /// <summary>
        /// Constructs and returns a HTTP 200 Ok response.
        /// The response is constructed using results of the upsert database operation when database policy is not defined for the read permission.
        /// If database policy is defined, the results of the subsequent select statement is used for constructing the response.
        /// </summary>
        /// <param name="resultRow">Reuslt of the upsert database operation</param>
        /// <param name="jsonDocument">Result of the select database operation</param>
        /// <param name="isReadPermissionConfiguredForRole">Indicates whether read permissions is configured for the role</param>
        /// <param name="isDatabasePolicyDefinedForReadAction">Indicates whether database policy is configured for read action</param>
        private static OkObjectResult ConstructOkMutationResponse(Dictionary<string, object?> resultRow, JsonDocument? jsonDocument, bool isReadPermissionConfiguredForRole, bool isDatabasePolicyDefinedForReadAction)
        {
            // When the database policy is defined for the read action, a subsequent select query will be executed to fetch the results.
            // So, the response of that database query is used to construct the final response to be returned.
            if (isDatabasePolicyDefinedForReadAction)
            {
                return (jsonDocument is not null) ? OkMutationResponse(jsonDocument.RootElement.Clone())
                                                  : OkMutationResponse(JsonDocument.Parse("[]").RootElement.Clone());
            }

            // When no database policy is defined for the read action, the results from the upsert database operation is
            // used to construct the final response.
            // When no read permissions are configured for the role, or all the fields are excluded
            // an empty response is returned.
            return (isReadPermissionConfiguredForRole && resultRow.Count > 0) ? OkMutationResponse(resultRow)
                                                     : OkMutationResponse(JsonDocument.Parse("[]").RootElement.Clone());
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
                EntityActionOperation operationType,
                IDictionary<string, object?> parameters,
                IMiddlewareContext? context = null)
        {
            string queryString;
            Dictionary<string, DbConnectionParam> queryParameters;
            switch (operationType)
            {
                case EntityActionOperation.Insert:
                case EntityActionOperation.Create:
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
                case EntityActionOperation.Update:
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
                case EntityActionOperation.UpdateIncremental:
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
                case EntityActionOperation.UpdateGraphQL:
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
                    if (operationType is EntityActionOperation.Create)
                    {
                        throw new DataApiBuilderException(
                            message: "Could not insert row with given values.",
                            statusCode: HttpStatusCode.Forbidden,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.DatabasePolicyFailure
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
            Dictionary<string, DbConnectionParam> queryParameters;
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
            Dictionary<string, DbConnectionParam> queryParameters;
            EntityActionOperation operationType = context.OperationType;
            string entityName = context.EntityName;

            if (operationType is EntityActionOperation.Upsert)
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
            if (context.DatabaseObject.SourceType is EntitySourceType.View)
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

        private Dictionary<string, object?> PrepareParameters(RestRequestContext context)
        {
            Dictionary<string, object?> parameters;

            switch (context.OperationType)
            {
                case EntityActionOperation.Delete:
                    // DeleteOne based off primary key in request.
                    parameters = new(context.PrimaryKeyValuePairs!);
                    break;
                case EntityActionOperation.Upsert:
                case EntityActionOperation.UpsertIncremental:
                case EntityActionOperation.Update:
                case EntityActionOperation.UpdateIncremental:
                    // Combine both PrimaryKey/Field ValuePairs
                    // because we create an update statement.
                    parameters = new(context.PrimaryKeyValuePairs!);
                    PopulateParamsFromRestRequest(parameters, context);
                    break;
                default:
                    parameters = new();
                    PopulateParamsFromRestRequest(parameters, context);
                    break;
            }

            return parameters;
        }

        /// <summary>
        /// Helper method to populate all the params from the Rest request's URI(PK)/request body into the parameters dictionary.
        /// An entry is added only for those parameters which actually map to a backing column in the table/view.
        /// </summary>
        /// <param name="parameters">Parameters dictionary to be populated.</param>
        /// <param name="context">Rest request context.</param>
        private void PopulateParamsFromRestRequest(Dictionary<string, object?> parameters, RestRequestContext context)
        {
            SourceDefinition sourceDefinition = _sqlMetadataProvider.GetSourceDefinition(context.EntityName);
            foreach ((string field, object? value) in context.FieldValuePairsInBody)
            {
                if (_sqlMetadataProvider.TryGetBackingColumn(context.EntityName, field, out string? backingColumnName)
                    && !sourceDefinition.Columns[backingColumnName].IsReadOnly)
                {
                    // Use TryAdd because there can be primary key fields present in the request body as well
                    // (in addition to request URL), when we operate in non-strict mode for REST.
                    // In such a case, the duplicate PK fields in the request body are ignored.
                    parameters.TryAdd(field, value);
                }
            }
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
            EntityActionOperation mutationOperation)
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
            if (mutationOperation != EntityActionOperation.Delete)
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
                case EntityActionOperation.UpdateGraphQL:
                    isAuthorized = _authorizationResolver.AreColumnsAllowedForOperation(entityName, roleName: role, operation: EntityActionOperation.Update, inputArgumentKeys);
                    break;
                case EntityActionOperation.Create:
                    isAuthorized = _authorizationResolver.AreColumnsAllowedForOperation(entityName, roleName: role, operation: mutationOperation, inputArgumentKeys);
                    break;
                case EntityActionOperation.Execute:
                case EntityActionOperation.Delete:
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

        /// <summary>
        /// For MySql database type, the isolation level is set at Repeatable Read as it is the default isolation level. Likewise, for MsSql and PostgreSql
        /// database types, the isolation level is set at Read Committed as it is the default.
        /// </summary>
        /// <returns>TransactionScope object with the appropriate isolation level based on the database type</returns>
        private TransactionScope ConstructTransactionScopeBasedOnDbType()
        {
            return _sqlMetadataProvider.GetDatabaseType() is DatabaseType.MySQL ? ConstructTransactionScopeWithSpecifiedIsolationLevel(isolationLevel: System.Transactions.IsolationLevel.RepeatableRead)
                                                                                : ConstructTransactionScopeWithSpecifiedIsolationLevel(isolationLevel: System.Transactions.IsolationLevel.ReadCommitted);
        }

        /// <summary>
        /// Helper method to construct a TransactionScope object with the specified isolation level and
        /// with the TransactionScopeAsyncFlowOption option enabled.
        /// </summary>
        /// <param name="isolationLevel">Transaction isolation level</param>
        /// <seealso cref="https://learn.microsoft.com/en-us/dotnet/framework/data/transactions/implementing-an-implicit-transaction-using-transaction-scope"/>
        /// <seealso cref="https://learn.microsoft.com/en-us/dotnet/api/system.transactions.transactionscopeoption?view=net-6.0#fields" />
        /// <seealso cref="https://learn.microsoft.com/en-us/dotnet/api/system.transactions.transactionscopeasyncflowoption?view=net-6.0#fields" />
        /// <returns>TransactionScope object set at the specified isolation level</returns>
        private static TransactionScope ConstructTransactionScopeWithSpecifiedIsolationLevel(System.Transactions.IsolationLevel isolationLevel)
        {
            return new(TransactionScopeOption.Required,
                        new TransactionOptions
                        {
                            IsolationLevel = isolationLevel
                        },
                        TransactionScopeAsyncFlowOption.Enabled);
        }
    }
}
