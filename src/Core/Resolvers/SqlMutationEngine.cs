// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data;
using System.Data.Common;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Transactions;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Resolvers.Sql_Query_Structures;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Mutations;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    /// <summary>
    /// Implements the mutation engine interface for mutations against Sql like databases.
    /// </summary>
    public class SqlMutationEngine : IMutationEngine
    {
        private readonly IAbstractQueryManagerFactory _queryManagerFactory;
        private readonly IMetadataProviderFactory _sqlMetadataProviderFactory;
        private readonly IQueryEngineFactory _queryEngineFactory;
        private readonly IAuthorizationResolver _authorizationResolver;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly GQLFilterParser _gQLFilterParser;
        private readonly RuntimeConfigProvider _runtimeConfigProvider;
        public const string IS_UPDATE_RESULT_SET = "IsUpdateResultSet";
        private const string TRANSACTION_EXCEPTION_ERROR_MSG = "An unexpected error occurred during the transaction execution";
        public const string SINGLE_INPUT_ARGUEMENT_NAME = "item";
        public const string MULTIPLE_INPUT_ARGUEMENT_NAME = "items";
        public const string MULTIPLE_ITEMS_RESPONSE_TYPE_SUFFIX = "Connection";

        private static DataApiBuilderException _dabExceptionWithTransactionErrorMessage = new(message: TRANSACTION_EXCEPTION_ERROR_MSG,
                                                                                            statusCode: HttpStatusCode.InternalServerError,
                                                                                            subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError);

        /// <summary>
        /// Constructor
        /// </summary>
        public SqlMutationEngine(
            IAbstractQueryManagerFactory queryManagerFactory,
            IMetadataProviderFactory sqlMetadataProviderFactory,
            IQueryEngineFactory queryEngineFactory,
            IAuthorizationResolver authorizationResolver,
            GQLFilterParser gQLFilterParser,
            IHttpContextAccessor httpContextAccessor,
            RuntimeConfigProvider runtimeConfigProvider)
        {
            _queryManagerFactory = queryManagerFactory;
            _sqlMetadataProviderFactory = sqlMetadataProviderFactory;
            _queryEngineFactory = queryEngineFactory;
            _authorizationResolver = authorizationResolver;
            _httpContextAccessor = httpContextAccessor;
            _gQLFilterParser = gQLFilterParser;
            _runtimeConfigProvider = runtimeConfigProvider;
        }

        /// <summary>
        /// Executes the GraphQL mutation query and returns result as JSON object asynchronously.
        /// </summary>
        /// <param name="context">context of graphql mutation</param>
        /// <param name="parameters">parameters in the mutation query.</param>
        /// <param name="dataSourceName">dataSourceName to execute against.</param>
        /// <returns>JSON object result and its related pagination metadata</returns>
        public async Task<Tuple<JsonDocument?, IMetadata?>> ExecuteAsync(IMiddlewareContext context, IDictionary<string, object?> parameters, string dataSourceName = "")
        {
            if (context.Selection.Type.IsListType())
            {
                throw new NotSupportedException("Returning list types from mutations not supported");
            }

            bool multipleInputType = false;

            dataSourceName = GetValidatedDataSourceName(dataSourceName);
            ISqlMetadataProvider sqlMetadataProvider = _sqlMetadataProviderFactory.GetMetadataProvider(dataSourceName);
            IQueryEngine queryEngine = _queryEngineFactory.GetQueryEngine(sqlMetadataProvider.GetDatabaseType());

            string graphqlMutationName = context.Selection.Field.Name.Value;
            IOutputType outputType = context.Selection.Field.Type;
            string entityName = outputType.TypeName();
            ObjectType _underlyingFieldType = GraphQLUtils.UnderlyingGraphQLEntityType(outputType);

            if (_underlyingFieldType.Name.Value.EndsWith(MULTIPLE_ITEMS_RESPONSE_TYPE_SUFFIX))
            {
                multipleInputType = true;
                IObjectField subField = GraphQLUtils.UnderlyingGraphQLEntityType(context.Selection.Field.Type).Fields[MULTIPLE_INPUT_ARGUEMENT_NAME];
                outputType = subField.Type;
                _underlyingFieldType = GraphQLUtils.UnderlyingGraphQLEntityType(outputType);
                entityName = _underlyingFieldType.Name;
            }

            if (GraphQLUtils.TryExtractGraphQLFieldModelName(_underlyingFieldType.Directives, out string? modelName))
            {
                entityName = modelName;
            }

            Tuple<JsonDocument?, IMetadata?>? result = null;
            EntityActionOperation mutationOperation = MutationBuilder.DetermineMutationOperationTypeBasedOnInputType(graphqlMutationName);

            // Ignoring AuthZ validations for Nested Insert operations in this PR.
            // AuthZ for nested inserts are implemented in a separate PR ---> https://github.com/Azure/data-api-builder/pull/1943
            if (mutationOperation is not EntityActionOperation.Create)
            {
                AuthorizeMutationFields(context, parameters, entityName, mutationOperation);
            }

            string roleName = GetRoleOfGraphQLRequest(context);

            // The presence of READ permission is checked in the current role (with which the request is executed) as well as Anonymous role. This is because, for GraphQL requests,
            // READ permission is inherited by other roles from Anonymous role when present.
            bool isReadPermissionConfigured = _authorizationResolver.AreRoleAndOperationDefinedForEntity(entityName, roleName, EntityActionOperation.Read)
                                              || _authorizationResolver.AreRoleAndOperationDefinedForEntity(entityName, AuthorizationType.Anonymous.ToString(), EntityActionOperation.Read);

            try
            {
                // Creating an implicit transaction
                using (TransactionScope transactionScope = ConstructTransactionScopeBasedOnDbType(sqlMetadataProvider))
                {
                    if (mutationOperation is EntityActionOperation.Delete)
                    {
                        // When read permission is not configured, an error response is returned. So, the mutation result needs to
                        // be computed only when the read permission is configured.
                        if (isReadPermissionConfigured)
                        {
                            // compute the mutation result before removing the element,
                            // since typical GraphQL delete mutations return the metadata of the deleted item.
                            result = await queryEngine.ExecuteAsync(
                                        context,
                                        GetBackingColumnsFromCollection(entityName: entityName, parameters: parameters, sqlMetadataProvider: sqlMetadataProvider),
                                        dataSourceName);
                        }

                        Dictionary<string, object>? resultProperties =
                            await PerformDeleteOperation(
                                entityName,
                                parameters,
                                sqlMetadataProvider);

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
                    else if (mutationOperation is EntityActionOperation.Create)
                    {
                        List<IDictionary<string, object?>> resultPKs = PerformNestedCreateOperation(
                                    entityName,
                                    parameters,
                                    sqlMetadataProvider,
                                    context,
                                    multipleInputType);

                        if (!multipleInputType)
                        {
                            result = await queryEngine.ExecuteAsync(
                                        context,
                                        resultPKs[0],
                                        dataSourceName);
                        }
                        else
                        {
                            result = await queryEngine.ExecuteAsync(
                                        context,
                                        resultPKs,
                                        dataSourceName);
                        }

                    }
                    else
                    {
                        DbResultSetRow? mutationResultRow =
                            await PerformMutationOperation(
                                entityName,
                                mutationOperation,
                                parameters,
                                sqlMetadataProvider,
                                context);

                        // When read permission is not configured, an error response is returned. So, the mutation result needs to
                        // be computed only when the read permission is configured.
                        if (isReadPermissionConfigured && mutationResultRow is not null && mutationResultRow.Columns.Count > 0
                            && !context.Selection.Type.IsScalarType())
                        {
                            // Because the GraphQL mutation result set columns were exposed (mapped) column names,
                            // the column names must be converted to backing (source) column names so the
                            // PrimaryKeyPredicates created in the SqlQueryStructure created by the query engine
                            // represent database column names.
                            result = await queryEngine.ExecuteAsync(
                                        context,
                                        GetBackingColumnsFromCollection(entityName: entityName, parameters: mutationResultRow.Columns, sqlMetadataProvider: sqlMetadataProvider),
                                        dataSourceName);
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

            if (!isReadPermissionConfigured)
            {
                throw new DataApiBuilderException(message: $"The mutation operation {context.Selection.Field.Name} was successful but the current user is unauthorized to view the response due to lack of read permissions",
                                                  statusCode: HttpStatusCode.Forbidden,
                                                  subStatusCode: DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed);
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
        public static Dictionary<string, object?> GetBackingColumnsFromCollection(string entityName, IDictionary<string, object?> parameters, ISqlMetadataProvider sqlMetadataProvider)
        {
            Dictionary<string, object?> backingRowParams = new();

            foreach (KeyValuePair<string, object?> resultEntry in parameters)
            {
                sqlMetadataProvider.TryGetBackingColumn(entityName, resultEntry.Key, out string? name);
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
        public async Task<IActionResult?> ExecuteAsync(StoredProcedureRequestContext context, string dataSourceName = "")
        {
            dataSourceName = GetValidatedDataSourceName(dataSourceName);
            ISqlMetadataProvider sqlMetadataProvider = _sqlMetadataProviderFactory.GetMetadataProvider(dataSourceName);
            IQueryBuilder queryBuilder = _queryManagerFactory.GetQueryBuilder(sqlMetadataProvider.GetDatabaseType());
            IQueryExecutor queryExecutor = _queryManagerFactory.GetQueryExecutor(sqlMetadataProvider.GetDatabaseType());
            SqlExecuteStructure executeQueryStructure = new(
                context.EntityName,
                sqlMetadataProvider,
                _authorizationResolver,
                _gQLFilterParser,
                context.ResolvedParameters);
            string queryText = queryBuilder.Build(executeQueryStructure);

            JsonArray? resultArray = null;

            try
            {
                // Creating an implicit transaction
                using (TransactionScope transactionScope = ConstructTransactionScopeBasedOnDbType(sqlMetadataProvider))
                {
                    resultArray =
                        await queryExecutor.ExecuteQueryAsync(
                            queryText,
                            executeQueryStructure.Parameters,
                            queryExecutor.GetJsonArrayAsync,
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

                    HttpContext httpContext = GetHttpContext();
                    string locationHeaderURL = UriHelper.BuildAbsolute(
                            scheme: httpContext.Request.Scheme,
                            host: httpContext.Request.Host,
                            pathBase: GetBaseRouteFromConfig(_runtimeConfigProvider.GetConfig()),
                            path: httpContext.Request.Path);

                    // Returns a 201 Created with whatever the first result set is returned from the procedure
                    // A "correctly" configured stored procedure would INSERT INTO ... OUTPUT ... VALUES as the result set
                    if (resultArray is not null && resultArray.Count > 0)
                    {
                        using (JsonDocument jsonDocument = JsonDocument.Parse(resultArray.ToJsonString()))
                        {
                            // The final location header for stored procedures should be of the form ../api/<SP-Entity-Name>
                            // Location header is constructed using the base URL, base-route and the set location value.

                            return new CreatedResult(location: locationHeaderURL, SqlResponseHelpers.OkMutationResponse(jsonDocument.RootElement.Clone()).Value);
                        }
                    }
                    else
                    {   // If no result set returned, just return a 201 Created with empty array instead of array with single null value
                        return new CreatedResult(
                            location: locationHeaderURL,
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
                            return SqlResponseHelpers.OkMutationResponse(jsonDocument.RootElement.Clone());
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
            // for REST API scenarios, use the default datasource
            string dataSourceName = _runtimeConfigProvider.GetConfig().GetDefaultDataSourceName();

            Dictionary<string, object?> parameters = PrepareParameters(context);
            ISqlMetadataProvider sqlMetadataProvider = _sqlMetadataProviderFactory.GetMetadataProvider(dataSourceName);

            if (context.OperationType is EntityActionOperation.Delete)
            {
                Dictionary<string, object>? resultProperties = null;

                try
                {
                    // Creating an implicit transaction
                    using (TransactionScope transactionScope = ConstructTransactionScopeBasedOnDbType(sqlMetadataProvider))
                    {
                        resultProperties = await PerformDeleteOperation(
                                entityName: context.EntityName,
                                parameters: parameters,
                                sqlMetadataProvider: sqlMetadataProvider);
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
                    && resultProperties.TryGetValue(nameof(DbDataReader.RecordsAffected), out object? value))
                {
                    // DbDataReader.RecordsAffected contains the number of rows changed deleted. 0 if no records were deleted.
                    // When the flow reaches this code block and the number of records affected is 0, then it means that no failure occurred at the database layer
                    // and that the item identified by the specified PK was not found.
                    if (Convert.ToInt32(value) == 0)
                    {
                        string prettyPrintPk = "<" + string.Join(", ", context.PrimaryKeyValuePairs.Select(kv_pair => $"{kv_pair.Key}: {kv_pair.Value}")) + ">";

                        throw new DataApiBuilderException(
                            message: $"Could not find item with {prettyPrintPk}",
                            statusCode: HttpStatusCode.NotFound,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.ItemNotFound);
                    }

                    return new NoContentResult();
                }
            }
            else
            {
                string roleName = GetHttpContext().Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER];
                bool isReadPermissionConfiguredForRole = _authorizationResolver.AreRoleAndOperationDefinedForEntity(context.EntityName, roleName, EntityActionOperation.Read);
                bool isDatabasePolicyDefinedForReadAction = false;
                JsonDocument? selectOperationResponse = null;

                if (isReadPermissionConfiguredForRole)
                {
                    isDatabasePolicyDefinedForReadAction = !string.IsNullOrWhiteSpace(_authorizationResolver.GetDBPolicyForRequest(context.EntityName, roleName, EntityActionOperation.Read));
                }

                try
                {
                    if (context.OperationType is EntityActionOperation.Upsert || context.OperationType is EntityActionOperation.UpsertIncremental)
                    {
                        DbResultSet? upsertOperationResult;
                        DbResultSetRow upsertOperationResultSetRow;

                        // This variable indicates whether the upsert resulted in an update operation. If true, then the upsert resulted in an update operation.
                        // If false, the upsert resulted in an insert operation.
                        bool hasPerformedUpdate = false;

                        try
                        {
                            // Creating an implicit transaction
                            using (TransactionScope transactionScope = ConstructTransactionScopeBasedOnDbType(sqlMetadataProvider))
                            {
                                upsertOperationResult = await PerformUpsertOperation(
                                                                    parameters: parameters,
                                                                    context: context,
                                                                    sqlMetadataProvider: sqlMetadataProvider);

                                if (upsertOperationResult is null)
                                {
                                    // Ideally this case should not happen, however may occur due to unexpected reasons,
                                    // like the DbDataReader being null. We throw an exception
                                    // which will be returned as an InternalServerError with UnexpectedError substatus code.
                                    throw new DataApiBuilderException(
                                        message: "An unexpected error occurred while trying to execute the query.",
                                        statusCode: HttpStatusCode.InternalServerError,
                                        subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError);
                                }

                                upsertOperationResultSetRow = upsertOperationResult.Rows.FirstOrDefault() ?? new();

                                if (upsertOperationResultSetRow.Columns.Count > 0 &&
                                    upsertOperationResult.ResultProperties.TryGetValue(IS_UPDATE_RESULT_SET, out object? isUpdateResultSetValue))
                                {

                                    hasPerformedUpdate = Convert.ToBoolean(isUpdateResultSetValue);
                                }

                                // The role with which the REST request is executed can have a database policy defined for the read action.
                                // In such a case, to get the results back, a select query which honors the database policy is executed.
                                if (isDatabasePolicyDefinedForReadAction)
                                {
                                    FindRequestContext findRequestContext = ConstructFindRequestContext(context, upsertOperationResultSetRow, roleName, sqlMetadataProvider);
                                    IQueryEngine queryEngine = _queryEngineFactory.GetQueryEngine(sqlMetadataProvider.GetDatabaseType());
                                    selectOperationResponse = await queryEngine.ExecuteAsync(findRequestContext);
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

                        Dictionary<string, object?> resultRow = upsertOperationResultSetRow.Columns;

                        // For all SQL database types, when an upsert operation results in an update operation, an entry <IsUpdateResultSet,true> is added to the result set dictionary.
                        // For MsSQL and MySQL database types, the "IsUpdateResultSet" field is sufficient to determine whether the resultant operation was an insert or an update.
                        // For PostgreSQL, the result set dictionary will always contain the entry <IsUpdateResultSet,true> irrespective of the upsert resulting in an insert/update operation.
                        // PostgreSQL result sets will contain a field "___upsert_op___" that indicates whether the resultant operation was an update or an insert. So, the value present in this field
                        // is used to determine whether the upsert resulted in an update/insert.
                        if (sqlMetadataProvider.GetDatabaseType() is DatabaseType.PostgreSQL)
                        {
                            hasPerformedUpdate = !PostgresQueryBuilder.IsInsert(resultRow);
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
                        if (!hasPerformedUpdate)
                        {
                            // Location Header is made up of the Base URL of the request and the primary key of the item created.
                            // However, for PATCH and PUT requests, the primary key would be present in the request URL. For POST request, however, the primary key
                            // would not be available in the URL and needs to be appended. Since, this is a PUT or PATCH request that has resulted in the creation of
                            // a new item, the URL already contains the primary key and hence, an empty string is passed as the primary key route.
                            return SqlResponseHelpers.ConstructCreatedResultResponse(resultRow, selectOperationResponse, primaryKeyRoute: string.Empty, isReadPermissionConfiguredForRole, isDatabasePolicyDefinedForReadAction, context.OperationType, GetBaseRouteFromConfig(_runtimeConfigProvider.GetConfig()), GetHttpContext());
                        }

                        // When the upsert operation results in the update of an existing record, an HTTP 200 OK response is returned.
                        return SqlResponseHelpers.ConstructOkMutationResponse(resultRow, selectOperationResponse, isReadPermissionConfiguredForRole, isDatabasePolicyDefinedForReadAction);
                    }
                    else
                    {
                        // This code block gets executed when the operation type is one among Insert, Update or UpdateIncremental.
                        DbResultSetRow? mutationResultRow = null;

                        try
                        {
                            // Creating an implicit transaction
                            using (TransactionScope transactionScope = ConstructTransactionScopeBasedOnDbType(sqlMetadataProvider))
                            {
                                mutationResultRow =
                                        await PerformMutationOperation(
                                            entityName: context.EntityName,
                                            operationType: context.OperationType,
                                            parameters: parameters,
                                            sqlMetadataProvider: sqlMetadataProvider);

                                if (mutationResultRow is null || mutationResultRow.Columns.Count == 0)
                                {
                                    if (context.OperationType is EntityActionOperation.Insert)
                                    {
                                        if (mutationResultRow is null)
                                        {
                                            // Ideally this case should not happen, however may occur due to unexpected reasons,
                                            // like the DbDataReader being null. We throw an exception
                                            // which will be returned as an UnexpectedError.
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
                                    else
                                    {
                                        if (mutationResultRow is null)
                                        {
                                            // Ideally this case should not happen, however may occur due to unexpected reasons,
                                            // like the DbDataReader being null. We throw an exception
                                            // which will be returned as an UnexpectedError  
                                            throw new DataApiBuilderException(message: "An unexpected error occurred while trying to execute the query.",
                                                                                statusCode: HttpStatusCode.NotFound,
                                                                                subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError);
                                        }

                                        if (mutationResultRow.Columns.Count == 0)
                                        {
                                            // This code block is reached when Update or UpdateIncremental operation does not successfully find the record to
                                            // update. An exception is thrown which will be returned as a 404 NotFound response.
                                            throw new DataApiBuilderException(message: "No Update could be performed, record not found",
                                                                                statusCode: HttpStatusCode.NotFound,
                                                                                subStatusCode: DataApiBuilderException.SubStatusCodes.EntityNotFound);
                                        }

                                    }
                                }

                                // The role with which the REST request is executed can have database policies defined for the read action.
                                // When the database policy is defined for the read action, a select query that honors the database policy
                                // is executed to fetch the results.
                                if (isDatabasePolicyDefinedForReadAction)
                                {
                                    FindRequestContext findRequestContext = ConstructFindRequestContext(context, mutationResultRow, roleName, sqlMetadataProvider);
                                    IQueryEngine queryEngine = _queryEngineFactory.GetQueryEngine(sqlMetadataProvider.GetDatabaseType());
                                    selectOperationResponse = await queryEngine.ExecuteAsync(findRequestContext);
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

                        string primaryKeyRouteForLocationHeader = isReadPermissionConfiguredForRole ? SqlResponseHelpers.ConstructPrimaryKeyRoute(context, mutationResultRow!.Columns, sqlMetadataProvider)
                                                                                                    : string.Empty;

                        if (context.OperationType is EntityActionOperation.Insert)
                        {
                            // Location Header is made up of the Base URL of the request and the primary key of the item created.
                            // For POST requests, the primary key info would not be available in the URL and needs to be appended. So, the primary key of the newly created item
                            // which is stored in the primaryKeyRoute is used to construct the Location Header.
                            return SqlResponseHelpers.ConstructCreatedResultResponse(mutationResultRow!.Columns, selectOperationResponse, primaryKeyRouteForLocationHeader, isReadPermissionConfiguredForRole, isDatabasePolicyDefinedForReadAction, context.OperationType, GetBaseRouteFromConfig(_runtimeConfigProvider.GetConfig()), GetHttpContext());
                        }

                        if (context.OperationType is EntityActionOperation.Update || context.OperationType is EntityActionOperation.UpdateIncremental)
                        {
                            return SqlResponseHelpers.ConstructOkMutationResponse(mutationResultRow!.Columns, selectOperationResponse, isReadPermissionConfiguredForRole, isDatabasePolicyDefinedForReadAction);
                        }
                    }

                }
                finally
                {
                    if (selectOperationResponse is not null)
                    {
                        selectOperationResponse.Dispose();
                    }
                }
            }

            // if we have not yet returned, record is null
            return null;
        }

        /// <summary>
        /// Constructs a FindRequestContext from the Insert/Upsert RequestContext and the results of insert/upsert database operation.
        /// For REST POST, PUT AND PATCH API requests, when there are database policies defined for the read action,
        /// a subsequent select query that honors the database policy is executed to fetch the results.
        /// </summary>
        /// <param name="context">Insert/Upsert Request context for the REST POST, PUT and PATCH request</param>
        /// <param name="mutationResultRow">Result of the insert/upsert database operation</param>
        /// <param name="roleName">Role with which the API request is executed</param>
        /// <param name="sqlMetadataProvider">SqlMetadataProvider object - provides helper method to get the exposed column name for a given column name</param>
        /// <returns>Returns a FindRequestContext object constructed from the existing context and create/upsert operation results.</returns>
        private FindRequestContext ConstructFindRequestContext(RestRequestContext context, DbResultSetRow mutationResultRow, string roleName, ISqlMetadataProvider sqlMetadataProvider)
        {
            FindRequestContext findRequestContext = new(entityName: context.EntityName, dbo: context.DatabaseObject, isList: false);

            // PrimaryKeyValuePairs in the context is populated using the primary key values from the
            // results of the insert/update database operation.
            foreach (string primarykey in context.DatabaseObject.SourceDefinition.PrimaryKey)
            {
                // The primary keys can have a mapping defined. mutationResultRow contains the mapped column names as keys.
                // So, the mapped field names are used to look up and fetch the values from mutationResultRow.
                // TryGetExposedColumnName method populates the the mapped column name (if configured) or the original column name into exposedColumnName.
                // It returns false if the primary key does not exist.
                if (sqlMetadataProvider.TryGetExposedColumnName(context.EntityName, primarykey, out string? exposedColumnName))
                {
                    findRequestContext.PrimaryKeyValuePairs.Add(exposedColumnName, mutationResultRow.Columns[exposedColumnName]!);
                }
                else
                {
                    // This code block should never be reached because the information about primary keys gets populated during the startup.
                    throw new DataApiBuilderException(
                       message: "Insert/Upsert operation was successful but unexpected error when constructing the response",
                       statusCode: HttpStatusCode.InternalServerError,
                       subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError);
                }
            }

            // READ action for the given role can have include and exclude fields configured. Populating UpdateReturnFields
            // ensures that the select query retrieves only those fields that are allowed for the given role.
            findRequestContext.UpdateReturnFields(_authorizationResolver.GetAllowedExposedColumns(context.EntityName, roleName, EntityActionOperation.Read));

            return findRequestContext;
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
                ISqlMetadataProvider sqlMetadataProvider,
                IMiddlewareContext? context = null)
        {
            IQueryBuilder queryBuilder = _queryManagerFactory.GetQueryBuilder(sqlMetadataProvider.GetDatabaseType());
            IQueryExecutor queryExecutor = _queryManagerFactory.GetQueryExecutor(sqlMetadataProvider.GetDatabaseType());
            string dataSourceName = _runtimeConfigProvider.GetConfig().GetDataSourceNameFromEntityName(entityName);

            string queryString;
            Dictionary<string, DbConnectionParam> queryParameters;
            switch (operationType)
            {
                case EntityActionOperation.Insert:
                case EntityActionOperation.Create:
                    SqlInsertStructure insertQueryStruct = context is null
                        ? new(
                            entityName,
                            sqlMetadataProvider,
                            _authorizationResolver,
                            _gQLFilterParser,
                            parameters,
                            GetHttpContext())
                        : new(
                            context,
                            entityName,
                            sqlMetadataProvider,
                            _authorizationResolver,
                            _gQLFilterParser,
                            parameters,
                            GetHttpContext());
                    queryString = queryBuilder.Build(insertQueryStruct);
                    queryParameters = insertQueryStruct.Parameters;
                    break;
                case EntityActionOperation.Update:
                    SqlUpdateStructure updateStructure = new(
                        entityName,
                        sqlMetadataProvider,
                        _authorizationResolver,
                        _gQLFilterParser,
                        parameters,
                        GetHttpContext(),
                        isIncrementalUpdate: false);
                    queryString = queryBuilder.Build(updateStructure);
                    queryParameters = updateStructure.Parameters;
                    break;
                case EntityActionOperation.UpdateIncremental:
                    SqlUpdateStructure updateIncrementalStructure = new(
                        entityName,
                        sqlMetadataProvider,
                        _authorizationResolver,
                        _gQLFilterParser,
                        parameters,
                        GetHttpContext(),
                        isIncrementalUpdate: true);
                    queryString = queryBuilder.Build(updateIncrementalStructure);
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
                        sqlMetadataProvider,
                        _authorizationResolver,
                        _gQLFilterParser,
                        parameters,
                        GetHttpContext());
                    queryString = queryBuilder.Build(updateGraphQLStructure);
                    queryParameters = updateGraphQLStructure.Parameters;
                    break;
                default:
                    throw new NotSupportedException($"Unexpected mutation operation \" {operationType}\" requested.");
            }

            DbResultSet? dbResultSet;
            DbResultSetRow? dbResultSetRow;

            /*
             * Move the below logic to a separate helper function. Re-use this in the fucntion for nested insertions.
             *
             */

            if (context is not null && !context.Selection.Type.IsScalarType())
            {
                SourceDefinition sourceDefinition = sqlMetadataProvider.GetSourceDefinition(entityName);

                // To support GraphQL field mappings (DB column aliases), convert the sourceDefinition
                // primary key column names (backing columns) to the exposed (mapped) column names to
                // identify primary key column names in the mutation result set.
                List<string> primaryKeyExposedColumnNames = new();
                foreach (string primaryKey in sourceDefinition.PrimaryKey)
                {
                    if (sqlMetadataProvider.TryGetExposedColumnName(entityName, primaryKey, out string? name) && !string.IsNullOrWhiteSpace(name))
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
                    await queryExecutor.ExecuteQueryAsync(
                        queryString,
                        queryParameters,
                        queryExecutor.ExtractResultSetFromDbDataReaderAsync,
                        GetHttpContext(),
                        primaryKeyExposedColumnNames.Count > 0 ? primaryKeyExposedColumnNames : sourceDefinition.PrimaryKey,
                        dataSourceName);

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
                        message: $"Could not find item with {searchedPK}",
                        statusCode: HttpStatusCode.NotFound,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ItemNotFound);
                }
            }
            else
            {
                // This is the scenario for all REST mutation operations covered by this function
                // and the case when the Selection Type is a scalar for GraphQL.
                dbResultSet =
                    await queryExecutor.ExecuteQueryAsync(
                        sqltext: queryString,
                        parameters: queryParameters,
                        dataReaderHandler: queryExecutor.ExtractResultSetFromDbDataReaderAsync,
                        httpContext: GetHttpContext(),
                        dataSourceName: dataSourceName);
                dbResultSetRow = dbResultSet is not null ? (dbResultSet.Rows.FirstOrDefault() ?? new()) : null;
            }

            return dbResultSetRow;
        }

        /// <summary>
        /// Performs the given GraphQL create mutation operation.
        /// </summary>
        /// <param name="entityName">Name of the top level entity</param>
        /// <param name="parameters">Mutation parameter arguments</param>
        /// <param name="sqlMetadataProvider">SqlMetadaprovider</param>
        /// <param name="context">Hotchocolate's context for the graphQL request.</param>
        /// <param name="multipleInputType">Boolean indicating whether the create operation is for multiple items.</param>
        /// <returns>Primary keys of the created records (in the top level entity).</returns>
        /// <exception cref="DataApiBuilderException"></exception>
        private List<IDictionary<string, object?>> PerformNestedCreateOperation(
                string entityName,
                IDictionary<string, object?> parameters,
                ISqlMetadataProvider sqlMetadataProvider,
                IMiddlewareContext context,
                bool multipleInputType = false)
        {
            string fieldName = multipleInputType ? MULTIPLE_INPUT_ARGUEMENT_NAME : SINGLE_INPUT_ARGUEMENT_NAME;
            object? inputParams = GQLNestedInsertArgumentToDictParams(context, fieldName, parameters);

            if (inputParams is null)
            {
                throw new DataApiBuilderException(
                              message: "Invalid data entered in the mutation request",
                              statusCode: HttpStatusCode.BadRequest,
                              subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }

            List<IDictionary<string, object?>> finalResultPKs = new();

            if (multipleInputType)
            {
                List<IDictionary<string, object?>> inputList = (List<IDictionary<string, object?>>)inputParams;
                foreach (IDictionary<string, object?> input in inputList)
                {
                    NestedInsertStructure nestedInsertStructure = new(entityName, entityName, null, input);
                    Dictionary<string, Dictionary<string, object?>> resultPKs = new();
                    PerformDbInsertOperation(sqlMetadataProvider, nestedInsertStructure, resultPKs, context);
                    if (nestedInsertStructure.CurrentEntityPKs is not null)
                    {
                        finalResultPKs.Add(nestedInsertStructure.CurrentEntityPKs);
                    }
                }
            }
            else
            {
                IDictionary<string, object?> input = (IDictionary<string, object?>)inputParams;

                Dictionary<string, Dictionary<string, object?>> resultPKs = new();
                NestedInsertStructure nestedInsertStructure = new(entityName, entityName, null, input);

                PerformDbInsertOperation(sqlMetadataProvider, nestedInsertStructure, resultPKs, context);
                if (nestedInsertStructure.CurrentEntityPKs is not null)
                {
                    finalResultPKs.Add(nestedInsertStructure.CurrentEntityPKs);
                }
            }

            return finalResultPKs;
        }

        /// <summary>
        /// Builds and executes the INSERT SQL statements necessary for the nested create mutation operation.
        /// </summary>
        /// <param name="sqlMetadataProvider">SqlMetadataprovider for the given database type.</param>
        /// <param name="nestedInsertStructure">Wrapper object for the current entity</param>
        /// <param name="resultPKs">Dictionary containing the PKs of the created items.</param>
        /// <param name="context">Hotchocolate's context for the graphQL request.</param>
        private void PerformDbInsertOperation(
            ISqlMetadataProvider sqlMetadataProvider,
            NestedInsertStructure nestedInsertStructure,
            Dictionary<string, Dictionary<string, object?>> resultPKs,
            IMiddlewareContext? context = null)
        {

            if (nestedInsertStructure.InputMutParams is null)
            {
                throw new DataApiBuilderException(
                        message: "Null input parameter is not acceptable",
                        statusCode: HttpStatusCode.InternalServerError,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError
                    );
            }

            // For One - Many and Many - Many relationship types, the entire logic needs to be run for each element of the input.
            // So, when the input is a list, we iterate over the list and run the logic for each element.
            if (nestedInsertStructure.InputMutParams.GetType().GetGenericTypeDefinition() == typeof(List<>))
            {
                List<IDictionary<string, object?>> inputParamList = (List<IDictionary<string, object?>>)nestedInsertStructure.InputMutParams;
                foreach (IDictionary<string, object?> inputParam in inputParamList)
                {
                    NestedInsertStructure ns = new(nestedInsertStructure.EntityName, nestedInsertStructure.HigherLevelEntityName, nestedInsertStructure.HigherLevelEntityPKs, inputParam, nestedInsertStructure.IsLinkingTableInsertionRequired);
                    Dictionary<string, Dictionary<string, object?>> newResultPks = new();
                    PerformDbInsertOperation(sqlMetadataProvider, ns, newResultPks, context);
                }
            }
            else
            {
                string entityName = nestedInsertStructure.EntityName;
                Entity entity = _runtimeConfigProvider.GetConfig().Entities[entityName];

                // Dependency Entity refers to those entities that are to be inserted before the top level entities. PKs of these entites are required
                // to be able to successfully create a record in the table backing the top level entity. 
                // Dependent Entity refers to those entities that are to be inserted after the top level entities. These entities require the PK of the top
                // level entity.
                DetermineDependentAndDependencyEntities(nestedInsertStructure.EntityName, nestedInsertStructure, sqlMetadataProvider, entity.Relationships);

                // Recurse for dependency entities
                foreach (Tuple<string, object?> dependecyEntity in nestedInsertStructure.DependencyEntities)
                {
                    NestedInsertStructure dependencyEntityNestedInsertStructure = new(GetRelatedEntityNameInRelationship(entity, dependecyEntity.Item1), entityName, nestedInsertStructure.CurrentEntityPKs, dependecyEntity.Item2);
                    PerformDbInsertOperation(sqlMetadataProvider, dependencyEntityNestedInsertStructure, resultPKs, context);
                }

                SourceDefinition currentEntitySourceDefinition = sqlMetadataProvider.GetSourceDefinition(entityName);

                List<string> primaryKeyColumnNames = new();
                foreach (string primaryKey in currentEntitySourceDefinition.PrimaryKey)
                {
                    primaryKeyColumnNames.Add(primaryKey);
                }

                DatabaseObject entityObject = sqlMetadataProvider.EntityToDatabaseObject[entityName];
                string entityFullName = entityObject.FullName;
                RelationshipMetadata relationshipData = currentEntitySourceDefinition.SourceEntityRelationshipMap[entityName];

                // Populate the foreign key values for the current entity. 
                foreach ((string relatedEntityName, List<ForeignKeyDefinition> fkDefinitions) in relationshipData.TargetEntityToFkDefinitionMap)
                {
                    DatabaseObject relatedEntityObject = sqlMetadataProvider.EntityToDatabaseObject[relatedEntityName];
                    string relatedEntityFullName = relatedEntityObject.FullName;
                    ForeignKeyDefinition fkDefinition = fkDefinitions[0];
                    if (string.Equals(fkDefinition.Pair.ReferencingDbTable.FullName, entityFullName) && string.Equals(fkDefinition.Pair.ReferencedDbTable.FullName, relatedEntityFullName))
                    {
                        int count = fkDefinition.ReferencingColumns.Count;
                        for (int i = 0; i < count; i++)
                        {
                            string referencingColumnName = fkDefinition.ReferencingColumns[i];
                            string referencedColumnName = fkDefinition.ReferencedColumns[i];

                            if (nestedInsertStructure.CurrentEntityParams!.ContainsKey(referencingColumnName))
                            {
                                continue;
                            }

                            if (resultPKs.TryGetValue(relatedEntityName, out Dictionary<string, object?>? relatedEntityPKs)
                                 && relatedEntityPKs is not null
                                 && relatedEntityPKs.TryGetValue(referencedColumnName, out object? relatedEntityPKValue)
                                 && relatedEntityPKValue is not null)
                            {
                                nestedInsertStructure.CurrentEntityParams.Add(referencingColumnName, relatedEntityPKValue);
                            }
                            else if (nestedInsertStructure.HigherLevelEntityPKs is not null
                                 && nestedInsertStructure.HigherLevelEntityPKs.TryGetValue(referencedColumnName, out object? pkValue)
                                 && pkValue is not null)
                            {
                                nestedInsertStructure.CurrentEntityParams.Add(referencingColumnName, pkValue);
                            }
                            else
                            {
                                throw new DataApiBuilderException(
                                                        message: $"Foreign Key value for  Entity: {entityName}, Column : {referencedColumnName} not found",
                                                        subStatusCode: DataApiBuilderException.SubStatusCodes.ForeignKeyNotFound,
                                                        statusCode: HttpStatusCode.InternalServerError);
                            }
                        }
                    }
                }

                SqlInsertStructure sqlInsertStructure = new(entityName,
                                                            sqlMetadataProvider,
                                                            _authorizationResolver,
                                                            _gQLFilterParser,
                                                            nestedInsertStructure.CurrentEntityParams!,
                                                            GetHttpContext());

                IQueryBuilder queryBuilder = _queryManagerFactory.GetQueryBuilder(sqlMetadataProvider.GetDatabaseType());
                IQueryExecutor queryExecutor = _queryManagerFactory.GetQueryExecutor(sqlMetadataProvider.GetDatabaseType());
                string dataSourceName = _runtimeConfigProvider.GetConfig().GetDataSourceNameFromEntityName(entityName);
                string queryString = queryBuilder.Build(sqlInsertStructure);
                Dictionary<string, DbConnectionParam> queryParameters = sqlInsertStructure.Parameters;

                DbResultSet? dbResultSet;
                DbResultSetRow? dbResultSetRow;

                dbResultSet = queryExecutor.ExecuteQuery(
                                      queryString,
                                      queryParameters,
                                      queryExecutor.ExtractResultSetFromDbDataReader,
                                      GetHttpContext(),
                                      primaryKeyColumnNames,
                                      dataSourceName);

                dbResultSetRow = dbResultSet is not null ?
                        (dbResultSet.Rows.FirstOrDefault() ?? new DbResultSetRow()) : null;

                if (dbResultSetRow is not null && dbResultSetRow.Columns.Count == 0)
                {
                    // For GraphQL, insert operation corresponds to Create action.
                    throw new DataApiBuilderException(
                        message: "Could not insert row with given values.",
                        statusCode: HttpStatusCode.Forbidden,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.DatabasePolicyFailure);
                }

                if (dbResultSetRow is null)
                {
                    throw new DataApiBuilderException(
                        message: "No data returned back from database.",
                        statusCode: HttpStatusCode.InternalServerError,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed);
                }

                Dictionary<string, object?> insertedValues = dbResultSetRow.Columns;
                Dictionary<string, object?> pkValues = new();
                foreach (string pk in primaryKeyColumnNames)
                {
                    pkValues.Add(pk, insertedValues[pk]);
                }

                resultPKs.Add(entityName, pkValues);
                nestedInsertStructure.CurrentEntityPKs = pkValues;

                //Perform an insertion in the linking table if required
                if (nestedInsertStructure.IsLinkingTableInsertionRequired)
                {
                    if (nestedInsertStructure.LinkingTableParams is null)
                    {
                        nestedInsertStructure.LinkingTableParams = new Dictionary<string, object?>();
                    }

                    // Add higher level entity PKs
                    List<ForeignKeyDefinition> foreignKeyDefinitions = relationshipData.TargetEntityToFkDefinitionMap[nestedInsertStructure.HigherLevelEntityName];
                    ForeignKeyDefinition fkDefinition = foreignKeyDefinitions[0];

                    int count = fkDefinition.ReferencingColumns.Count;
                    for (int i = 0; i < count; i++)
                    {
                        string referencingColumnName = fkDefinition.ReferencingColumns[i];
                        string referencedColumnName = fkDefinition.ReferencedColumns[i];

                        if (nestedInsertStructure.LinkingTableParams.ContainsKey(referencingColumnName))
                        {
                            continue;
                        }

                        nestedInsertStructure.LinkingTableParams.Add(referencingColumnName, nestedInsertStructure.CurrentEntityPKs![referencedColumnName]);
                    }

                    // Add current entity PKs
                    SourceDefinition higherLevelEntityRelationshipMetadata = sqlMetadataProvider.GetSourceDefinition(nestedInsertStructure.HigherLevelEntityName);
                    RelationshipMetadata relationshipMetadata2 = higherLevelEntityRelationshipMetadata.SourceEntityRelationshipMap[nestedInsertStructure.HigherLevelEntityName];

                    foreignKeyDefinitions = relationshipMetadata2.TargetEntityToFkDefinitionMap[entityName];
                    fkDefinition = foreignKeyDefinitions[0];

                    count = fkDefinition.ReferencingColumns.Count;
                    for (int i = 0; i < count; i++)
                    {
                        string referencingColumnName = fkDefinition.ReferencingColumns[i];
                        string referencedColumnName = fkDefinition.ReferencedColumns[i];

                        if (nestedInsertStructure.LinkingTableParams.ContainsKey(referencingColumnName))
                        {
                            continue;
                        }

                        nestedInsertStructure.LinkingTableParams.Add(referencingColumnName, nestedInsertStructure.HigherLevelEntityPKs![referencedColumnName]);
                    }

                    SqlInsertStructure linkingEntitySqlInsertStructure = new(RuntimeConfig.GenerateLinkingEntityName(nestedInsertStructure.HigherLevelEntityName, entityName),
                                                                            sqlMetadataProvider,
                                                                            _authorizationResolver,
                                                                            _gQLFilterParser,
                                                                            nestedInsertStructure.LinkingTableParams!,
                                                                            GetHttpContext(),
                                                                            isLinkingEntity: true);

                    string linkingTableQueryString = queryBuilder.Build(linkingEntitySqlInsertStructure);
                    SourceDefinition linkingTableSourceDefinition = sqlMetadataProvider.GetSourceDefinition(RuntimeConfig.GenerateLinkingEntityName(nestedInsertStructure.HigherLevelEntityName, entityName));

                    List<string> linkingTablePkColumns = new();
                    foreach (string primaryKey in linkingTableSourceDefinition.PrimaryKey)
                    {
                        linkingTablePkColumns.Add(primaryKey);
                    }

                    Dictionary<string, DbConnectionParam> linkingTableQueryParams = linkingEntitySqlInsertStructure.Parameters;
                    dbResultSet = queryExecutor.ExecuteQuery(
                                      linkingTableQueryString,
                                      linkingTableQueryParams,
                                      queryExecutor.ExtractResultSetFromDbDataReader,
                                      GetHttpContext(),
                                      linkingTablePkColumns,
                                      dataSourceName);

                    dbResultSetRow = dbResultSet is not null ?
                            (dbResultSet.Rows.FirstOrDefault() ?? new DbResultSetRow()) : null;

                    if (dbResultSetRow is not null && dbResultSetRow.Columns.Count == 0)
                    {
                        // For GraphQL, insert operation corresponds to Create action.
                        throw new DataApiBuilderException(
                            message: "Could not insert row with given values.",
                            statusCode: HttpStatusCode.Forbidden,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.DatabasePolicyFailure);
                    }

                    if (dbResultSetRow is null)
                    {
                        throw new DataApiBuilderException(
                            message: "No data returned back from database.",
                            statusCode: HttpStatusCode.InternalServerError,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed);
                    }
                }

                // Recurse for dependent entities
                foreach (Tuple<string, object?> dependentEntity in nestedInsertStructure.DependentEntities)
                {
                    string relatedEntityName = GetRelatedEntityNameInRelationship(entity, dependentEntity.Item1);
                    NestedInsertStructure dependentEntityNestedInsertStructure = new(relatedEntityName, entityName, nestedInsertStructure.CurrentEntityPKs, dependentEntity.Item2, IsManyToManyRelationship(entity, dependentEntity.Item1));
                    PerformDbInsertOperation(sqlMetadataProvider, dependentEntityNestedInsertStructure, resultPKs, context);
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="relationshipName"></param>
        /// <returns></returns>
        public static string GetRelatedEntityNameInRelationship(Entity entity, string relationshipName)
        {
            if (entity.Relationships is null)
            {
                throw new DataApiBuilderException(message: "Entity has no relationships defined",
                                                  statusCode: HttpStatusCode.InternalServerError,
                                                  subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError);
            }

            if (entity.Relationships.TryGetValue(relationshipName, out EntityRelationship? entityRelationship)
               && entityRelationship is not null)
            {
                return entityRelationship.TargetEntity;
            }
            else
            {
                throw new DataApiBuilderException(message: $"Entity does not have a relationship named {relationshipName}",
                                                  statusCode: HttpStatusCode.InternalServerError,
                                                  subStatusCode: DataApiBuilderException.SubStatusCodes.RelationshipNotFound);
            }

        }

        /// <summary>
        /// Helper method to determine whether the relationship is a M:N relationship.
        /// </summary>
        /// <param name="entity">Entity </param>
        /// <param name="relationshipName">Name of the relationship</param>
        /// <returns>True/False indicating whther a record should be created in the linking table</returns>
        public static bool IsManyToManyRelationship(Entity entity, string relationshipName)
        {
            return entity is not null &&
                   entity.Relationships is not null &&
                   entity.Relationships[relationshipName] is not null &&
                   entity.Relationships[relationshipName].Cardinality is Cardinality.Many &&
                   entity.Relationships[relationshipName].LinkingObject is not null;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="parameters"></param>
        /// <param name="sqlMetadataProvider"></param>
        /// <param name="dependencyEntities"></param>
        /// <param name="dependentEntities"></param>
        /// <param name="currentEntityParams"></param>
        private static void DetermineDependentAndDependencyEntities(string entityName,
                                                             NestedInsertStructure nestedInsertStructure,
                                                             ISqlMetadataProvider sqlMetadataProvider,
                                                             Dictionary<string, EntityRelationship>? topLevelEntityRelationships)
        {
            IDictionary<string, object?> currentEntityParams = new Dictionary<string, object?>();
            IDictionary<string, object?> linkingTableParams = new Dictionary<string, object?>();

            if (nestedInsertStructure.InputMutParams is null)
            {
                return;
            }

            if (topLevelEntityRelationships is not null)
            {
                foreach (KeyValuePair<string, object?> entry in (Dictionary<string, object?>)nestedInsertStructure.InputMutParams)
                {
                    if (topLevelEntityRelationships.ContainsKey(entry.Key))
                    {
                        EntityRelationship relationshipInfo = topLevelEntityRelationships[entry.Key];
                        string relatedEntityName = relationshipInfo.TargetEntity;

                        if (relationshipInfo.Cardinality is Cardinality.Many)
                        {
                            nestedInsertStructure.DependentEntities.Add(new Tuple<string, object?>(entry.Key, entry.Value) { });
                        }

                        if (relationshipInfo.Cardinality is Cardinality.One)
                        {
                            SourceDefinition sourceDefinition = sqlMetadataProvider.GetSourceDefinition(entityName);
                            RelationshipMetadata relationshipMetadata = sourceDefinition.SourceEntityRelationshipMap[entityName];
                            List<ForeignKeyDefinition> fkDefinitions = relationshipMetadata.TargetEntityToFkDefinitionMap[relatedEntityName];
                            ForeignKeyDefinition fkDefinition = fkDefinitions[0];
                            DatabaseObject entityDbObject = sqlMetadataProvider.EntityToDatabaseObject[entityName];
                            string topLevelEntityFullName = entityDbObject.FullName;
                            Console.WriteLine("Top Level Entity Full Name : " + topLevelEntityFullName);

                            DatabaseObject relatedDbObject = sqlMetadataProvider.EntityToDatabaseObject[relatedEntityName];
                            string relatedEntityFullName = relatedDbObject.FullName;
                            Console.WriteLine("Related Entity Full Name : " + relatedEntityFullName);

                            if (string.Equals(fkDefinition.Pair.ReferencingDbTable.FullName, topLevelEntityFullName) && string.Equals(fkDefinition.Pair.ReferencedDbTable.FullName, relatedEntityFullName))
                            {
                                nestedInsertStructure.DependencyEntities.Add(new Tuple<string, object?>(entry.Key, entry.Value) { });
                            }
                            else
                            {
                                nestedInsertStructure.DependentEntities.Add(new Tuple<string, object?>(entry.Key, entry.Value) { });
                            }
                        }
                    }
                    else
                    {
                        if (sqlMetadataProvider.TryGetBackingColumn(entityName, entry.Key, out _))
                        {
                            currentEntityParams.Add(entry.Key, entry.Value);
                        }
                        else
                        {
                            linkingTableParams.Add(entry.Key, entry.Value);
                        }
                    }
                }
            }

            nestedInsertStructure.CurrentEntityParams = currentEntityParams;
            nestedInsertStructure.LinkingTableParams = linkingTableParams;
        }

        /// <summary>
        /// Function to parse the mutation parameters from Hotchocolate input types to Dictionary of field names and values.
        /// </summary>
        /// <param name="context">GQL middleware context used to resolve the values of arguments</param>
        /// <param name="fieldName">GQL field from which to extract the parameters</param>
        /// <param name="mutationParameters">Dictionary of mutation parameters</param>
        /// <returns></returns>
        internal static object? GQLNestedInsertArgumentToDictParams(IMiddlewareContext context, string fieldName, IDictionary<string, object?> mutationParameters)
        {

            if (mutationParameters.TryGetValue(fieldName, out object? inputParameters))
            {
                IObjectField fieldSchema = context.Selection.Field;
                IInputField itemsArgumentSchema = fieldSchema.Arguments[fieldName];
                InputObjectType itemsArgumentObject = ResolverMiddleware.InputObjectTypeFromIInputField(itemsArgumentSchema);
                return GQLNestedInsertArgumentToDictParamsHelper(context, itemsArgumentObject, inputParameters);
            }
            else
            {
                throw new DataApiBuilderException(
                    message: $"Expected {fieldName} argument in mutation arguments.",
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest,
                    statusCode: HttpStatusCode.BadRequest);
            }

        }

        /// <summary>
        /// Helper function to parse the mutation parameters from Hotchocolate input types to Dictionary of field names and values.
        /// For nested create mutation, the input types of a field can be a scalar, object or list type.
        /// This function recursively parses for each input type.
        /// </summary>
        /// <param name="context">GQL middleware context used to resolve the values of arguments.</param>
        /// <param name="rawInput">Hotchocolate input object type.</param>
        /// <returns></returns>
        internal static object? GQLNestedInsertArgumentToDictParamsHelper(IMiddlewareContext context, InputObjectType itemsArgumentObject, object? inputParameters)
        {
            // This condition is met for input types that accepts an array of values.
            // Ex: 1. Multiple nested create operation ---> createbooks_multiple.   
            //     2. Input types for 1:N and M:N relationships.
            if (inputParameters is List<IValueNode> inputList)
            {
                List<IDictionary<string, object?>> resultList = new();

                foreach (IValueNode input in inputList)
                {
                    object? resultItem = GQLNestedInsertArgumentToDictParamsHelper(context, itemsArgumentObject, input.Value);

                    if (resultItem is not null)
                    {
                        resultList.Add((IDictionary<string, object?>)resultItem);
                    }
                }

                return resultList;
            }
            // This condition is met for input types that accept input for a single item.
            // Ex: 1. Simple nested create operation --> createbook.
            //     2. Input types for 1:1 and N:1 relationships.
            else if (inputParameters is List<ObjectFieldNode> nodes)
            {
                Dictionary<string, object?> result = new();
                foreach (ObjectFieldNode node in nodes)
                {

                    string name = node.Name.Value;
                    if (node.Value.Kind == SyntaxKind.ListValue)
                    {
                        result.Add(name, GQLNestedInsertArgumentToDictParamsHelper(context, GetInputObjectTypeForAField(name, itemsArgumentObject.Fields), node.Value.Value));
                    }
                    else if (node.Value.Kind == SyntaxKind.ObjectValue)
                    {
                        result.Add(name, GQLNestedInsertArgumentToDictParamsHelper(context, GetInputObjectTypeForAField(name, itemsArgumentObject.Fields), node.Value.Value));
                    }
                    else
                    {
                        object? value = ResolverMiddleware.ExtractValueFromIValueNode(value: node.Value,
                                                                                      argumentSchema: itemsArgumentObject.Fields[name],
                                                                                      variables: context.Variables);

                        result.Add(name, value);
                    }
                }

                return result;
            }

            return null;
        }

        /// <summary>
        /// Extracts the InputObjectType for a given field.
        /// </summary>
        /// <param name="fieldName">Field name for which the input object type is to be extracted.</param>
        /// <param name="fields">Fields present in the input object type.</param>
        /// <returns>The input object type for the given field.</returns>
        /// <exception cref="DataApiBuilderException"></exception>
        private static InputObjectType GetInputObjectTypeForAField(string fieldName, FieldCollection<InputField> fields)
        {
            if (fields.TryGetField(fieldName, out IInputField? field))
            {
                return ResolverMiddleware.InputObjectTypeFromIInputField(field);
            }

            throw new DataApiBuilderException(message: $"Field {fieldName} not found.",
                                              subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError,
                                              statusCode: HttpStatusCode.InternalServerError);
        }

        internal static IDictionary<string, object?> GQLMutArgumentToDictParams(
            IMiddlewareContext context,
            string fieldName,
            IDictionary<string, object?> mutationParameters)
        {
            string errMsg;

            if (mutationParameters.TryGetValue(fieldName, out object? item))
            {
                IObjectField fieldSchema = context.Selection.Field;
                IInputField itemsArgumentSchema = fieldSchema.Arguments[fieldName];
                InputObjectType itemsArgumentObject = ResolverMiddleware.InputObjectTypeFromIInputField(itemsArgumentSchema);

                Dictionary<string, object?> mutationInput;
                // An inline argument was set
                // TODO: This assumes the input was NOT nullable.
                if (item is List<ObjectFieldNode> mutationInputRaw)
                {
                    mutationInput = new Dictionary<string, object?>();
                    foreach (ObjectFieldNode node in mutationInputRaw)
                    {
                        string nodeName = node.Name.Value;
                        Console.WriteLine(node.Value.ToString());

                        mutationInput.Add(nodeName, ResolverMiddleware.ExtractValueFromIValueNode(
                            value: node.Value,
                            argumentSchema: itemsArgumentObject.Fields[nodeName],
                            variables: context.Variables));
                    }

                    return mutationInput;
                }
                else
                {
                    errMsg = $"Unexpected {fieldName} argument format.";
                }
            }
            else
            {
                errMsg = $"Expected {fieldName} argument in mutation arguments.";
            }

            // should not happen due to gql schema validation
            throw new DataApiBuilderException(
                message: errMsg,
                subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest,
                statusCode: HttpStatusCode.BadRequest);
        }

        /// <summary>
        /// Perform the DELETE operation on the given entity.
        /// To determine the correct response, uses QueryExecutor's GetResultProperties handler for
        /// obtaining the db data reader properties like RecordsAffected, HasRows.
        /// </summary>
        /// <param name="entityName">The name of the entity.</param>
        /// <param name="parameters">The parameters for the DELETE operation.</param>
        /// <param name="sqlMetadataProvider">Metadataprovider for db on which to perform operation.</param>
        /// <returns>A dictionary of properties of the Db Data Reader like RecordsAffected, HasRows.</returns>
        private async Task<Dictionary<string, object>?>
            PerformDeleteOperation(
                string entityName,
                IDictionary<string, object?> parameters,
                ISqlMetadataProvider sqlMetadataProvider)
        {
            IQueryBuilder queryBuilder = _queryManagerFactory.GetQueryBuilder(sqlMetadataProvider.GetDatabaseType());
            IQueryExecutor queryExecutor = _queryManagerFactory.GetQueryExecutor(sqlMetadataProvider.GetDatabaseType());
            string dataSourceName = _runtimeConfigProvider.GetConfig().GetDataSourceNameFromEntityName(entityName);
            string queryString;
            Dictionary<string, DbConnectionParam> queryParameters;
            SqlDeleteStructure deleteStructure = new(
                entityName,
                sqlMetadataProvider,
                _authorizationResolver,
                _gQLFilterParser,
                parameters,
                GetHttpContext());
            queryString = queryBuilder.Build(deleteStructure);
            queryParameters = deleteStructure.Parameters;

            Dictionary<string, object>?
                resultProperties = await queryExecutor.ExecuteQueryAsync(
                    sqltext: queryString,
                    parameters: queryParameters,
                    dataReaderHandler: queryExecutor.GetResultProperties,
                    httpContext: GetHttpContext(),
                    dataSourceName: dataSourceName);

            return resultProperties;
        }

        /// <summary>
        /// Perform an Upsert or UpsertIncremental operation on the given entity.
        /// Since Upsert operations could simply be an update or result in an insert,
        /// uses QueryExecutor's GetMultipleResultSetsIfAnyAsync as the data reader handler.
        /// </summary>
        /// <param name="parameters">The parameters for the mutation query.</param>
        /// <param name="context">The REST request context.</param>
        /// <param name="sqlMetadataProvider">Metadataprovider for db on which to perform operation.</param>
        /// <returns>Single row read from DbDataReader.</returns>
        private async Task<DbResultSet?>
            PerformUpsertOperation(
                IDictionary<string, object?> parameters,
                RestRequestContext context,
                ISqlMetadataProvider sqlMetadataProvider)
        {
            string queryString;
            Dictionary<string, DbConnectionParam> queryParameters;
            EntityActionOperation operationType = context.OperationType;
            string entityName = context.EntityName;
            IQueryBuilder queryBuilder = _queryManagerFactory.GetQueryBuilder(sqlMetadataProvider.GetDatabaseType());
            IQueryExecutor queryExecutor = _queryManagerFactory.GetQueryExecutor(sqlMetadataProvider.GetDatabaseType());
            string dataSourceName = _runtimeConfigProvider.GetConfig().GetDataSourceNameFromEntityName(entityName);

            if (operationType is EntityActionOperation.Upsert)
            {
                SqlUpsertQueryStructure upsertStructure = new(
                    entityName,
                    sqlMetadataProvider,
                    _authorizationResolver,
                    _gQLFilterParser,
                    parameters,
                    httpContext: GetHttpContext(),
                    incrementalUpdate: false);
                queryString = queryBuilder.Build(upsertStructure);
                queryParameters = upsertStructure.Parameters;
            }
            else
            {
                SqlUpsertQueryStructure upsertIncrementalStructure = new(
                    entityName,
                    sqlMetadataProvider,
                    _authorizationResolver,
                    _gQLFilterParser,
                    parameters,
                    httpContext: GetHttpContext(),
                    incrementalUpdate: true);
                queryString = queryBuilder.Build(upsertIncrementalStructure);
                queryParameters = upsertIncrementalStructure.Parameters;
            }

            string prettyPrintPk = "<" + string.Join(", ", context.PrimaryKeyValuePairs.Select(
                kv_pair => $"{kv_pair.Key}: {kv_pair.Value}"
                )) + ">";

            return await queryExecutor.ExecuteQueryAsync(
                       queryString,
                       queryParameters,
                       queryExecutor.GetMultipleResultSetsIfAnyAsync,
                       GetHttpContext(),
                       new List<string> { prettyPrintPk, entityName },
                       dataSourceName);
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
            string dataSourceName = _runtimeConfigProvider.GetConfig().GetDataSourceNameFromEntityName(context.EntityName);
            ISqlMetadataProvider sqlMetadataProvider = _sqlMetadataProviderFactory.GetMetadataProvider(dataSourceName);
            SourceDefinition sourceDefinition = sqlMetadataProvider.GetSourceDefinition(context.EntityName);
            foreach ((string field, object? value) in context.FieldValuePairsInBody)
            {
                if (sqlMetadataProvider.TryGetBackingColumn(context.EntityName, field, out string? backingColumnName)
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
            string role = GetRoleOfGraphQLRequest(context);

            List<string> inputArgumentKeys;
            if (mutationOperation != EntityActionOperation.Delete)
            {
                inputArgumentKeys = BaseSqlQueryStructure.GetSubArgumentNamesFromGQLMutArguments(MutationBuilder.ITEM_INPUT_ARGUMENT_NAME, parameters);
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
        /// Helper method to get the role with which the GraphQL API request was executed.
        /// </summary>
        /// <param name="context">HotChocolate context for the GraphQL request</param>
        private static string GetRoleOfGraphQLRequest(IMiddlewareContext context)
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

            return role;
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
        /// Helper method to extract the configured base route in the runtime config.
        /// </summary>
        private static string GetBaseRouteFromConfig(RuntimeConfig? config)
        {
            if (config?.Runtime?.BaseRoute is not null)
            {
                return config.Runtime.BaseRoute;
            }

            return string.Empty;
        }

        /// <summary>
        /// For MySql database type, the isolation level is set at Repeatable Read as it is the default isolation level. Likewise, for MsSql and PostgreSql
        /// database types, the isolation level is set at Read Committed as it is the default.
        /// </summary>
        /// <param name="sqlMetadataProvider">Metadataprovider.</param>
        /// <returns>TransactionScope object with the appropriate isolation level based on the database type</returns>
        private static TransactionScope ConstructTransactionScopeBasedOnDbType(ISqlMetadataProvider sqlMetadataProvider)
        {
            return sqlMetadataProvider.GetDatabaseType() is DatabaseType.MySQL ? ConstructTransactionScopeWithSpecifiedIsolationLevel(isolationLevel: System.Transactions.IsolationLevel.RepeatableRead)
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

        /// <summary>
        /// Returns the data source name if it is valid. If not, returns the default data source name.
        /// </summary>
        /// <param name="dataSourceName">datasourceName.</param>
        /// <returns>datasourceName.</returns>
        private string GetValidatedDataSourceName(string dataSourceName)
        {
            // For rest scenarios - no multiple db support. Hence to maintain backward compatibility, we will use the default db.
            return string.IsNullOrEmpty(dataSourceName) ? _runtimeConfigProvider.GetConfig().GetDefaultDataSourceName() : dataSourceName;
        }
    }
}
