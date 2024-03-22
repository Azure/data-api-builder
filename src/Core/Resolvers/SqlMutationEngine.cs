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
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Mutations;
using Azure.DataApiBuilder.Service.Services;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;

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

            dataSourceName = GetValidatedDataSourceName(dataSourceName);
            string graphqlMutationName = context.Selection.Field.Name.Value;
            string entityName = GraphQLUtils.GetEntityNameFromContext(context);

            ISqlMetadataProvider sqlMetadataProvider = _sqlMetadataProviderFactory.GetMetadataProvider(dataSourceName);
            IQueryEngine queryEngine = _queryEngineFactory.GetQueryEngine(sqlMetadataProvider.GetDatabaseType());

            Tuple<JsonDocument?, IMetadata?>? result = null;
            EntityActionOperation mutationOperation = MutationBuilder.DetermineMutationOperationTypeBasedOnInputType(graphqlMutationName);
            string roleName = AuthorizationResolver.GetRoleOfGraphQLRequest(context);

            // If authorization fails, an exception will be thrown and request execution halts.
            AuthorizeMutation(context, parameters, entityName, mutationOperation);

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
                            // For cases we only require a result summarizing the operation (DBOperationResult),
                            // we can skip getting the impacted records.
                            if (context.Selection.Type.TypeName() != GraphQLUtils.DB_OPERATION_RESULT_TYPE)
                            {
                                // compute the mutation result before removing the element,
                                // since typical GraphQL delete mutations return the metadata of the deleted item.
                                result = await queryEngine.ExecuteAsync(
                                            context,
                                            GetBackingColumnsFromCollection(entityName: entityName, parameters: parameters, sqlMetadataProvider: sqlMetadataProvider),
                                            dataSourceName);
                            }
                        }

                        Dictionary<string, object>? resultProperties =
                            await PerformDeleteOperation(
                                entityName,
                                parameters,
                                sqlMetadataProvider);

                        // If the number of records affected by DELETE were zero,
                        if (resultProperties is not null
                            && resultProperties.TryGetValue(nameof(DbDataReader.RecordsAffected), out object? value)
                            && Convert.ToInt32(value) == 0)
                        {
                            // the result was not null previously, it indicates this DELETE lost
                            // a concurrent request race. Hence, empty the non-null result.
                            if (result is not null && result.Item1 is not null)
                            {

                                result = new Tuple<JsonDocument?, IMetadata?>(
                                    default(JsonDocument),
                                    PaginationMetadata.MakeEmptyPaginationMetadata());
                            }
                            else if (context.Selection.Type.TypeName() == GraphQLUtils.DB_OPERATION_RESULT_TYPE)
                            {
                                // no record affected but db call ran successfully.
                                result = GetDbOperationResultJsonDocument("item not found");
                            }
                        }
                        else if (context.Selection.Type.TypeName() == GraphQLUtils.DB_OPERATION_RESULT_TYPE)
                        {
                            result = GetDbOperationResultJsonDocument("success");
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
                        if (isReadPermissionConfigured)
                        {
                            if (mutationResultRow is not null && mutationResultRow.Columns.Count > 0
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
                            else if (context.Selection.Type.TypeName() == GraphQLUtils.DB_OPERATION_RESULT_TYPE)
                            {
                                result = GetDbOperationResultJsonDocument("success");
                            }
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
        /// Helper method to determine whether a mutation is a mutate one or mutate many operation (eg. createBook/createBooks).
        /// </summary>
        /// <param name="context">GraphQL request context.</param>
        private static bool IsPointMutation(IMiddlewareContext context)
        {
            IOutputType outputType = context.Selection.Field.Type;
            if (outputType.TypeName().Value.Equals(GraphQLUtils.DB_OPERATION_RESULT_TYPE))
            {
                // Hit when the database type is DwSql. We don't support multiple mutation for DwSql yet.
                return true;
            }

            ObjectType underlyingFieldType = GraphQLUtils.UnderlyingGraphQLEntityType(outputType);
            bool isPointMutation;
            if (GraphQLUtils.TryExtractGraphQLFieldModelName(underlyingFieldType.Directives, out string? _))
            {
                isPointMutation = true;
            }
            else
            {
                // Model directive is not added to the output type of 'mutate many' mutations.
                // Thus, absence of model directive here indicates that we are dealing with a 'mutate many'
                // mutation like createBooks.
                isPointMutation = false;
            }

            return isPointMutation;
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
                            dataSourceName,
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
            string dataSourceName = _runtimeConfigProvider.GetConfig().DefaultDataSourceName;

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
                        queryExecutor.ExtractResultSetFromDbDataReader,
                        dataSourceName,
                        GetHttpContext(),
                        primaryKeyExposedColumnNames.Count > 0 ? primaryKeyExposedColumnNames : sourceDefinition.PrimaryKey);

                dbResultSetRow = dbResultSet is not null ?
                    (dbResultSet.Rows.FirstOrDefault() ?? new DbResultSetRow()) : null;

                if (dbResultSetRow is not null && dbResultSetRow.Columns.Count == 0 && dbResultSet!.ResultProperties.TryGetValue("RecordsAffected", out object? recordsAffected) && (int)recordsAffected <= 0)
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
                        dataReaderHandler: queryExecutor.ExtractResultSetFromDbDataReader,
                        httpContext: GetHttpContext(),
                        dataSourceName: dataSourceName);
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
                       dataSourceName,
                       GetHttpContext(),
                       new List<string> { prettyPrintPk, entityName });
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

        /// <inheritdoc/>
        public void AuthorizeMutation(
            IMiddlewareContext context,
            IDictionary<string, object?> parameters,
            string entityName,
            EntityActionOperation mutationOperation)
        {
            string inputArgumentName = MutationBuilder.ITEM_INPUT_ARGUMENT_NAME;
            string clientRole = AuthorizationResolver.GetRoleOfGraphQLRequest(context);
            if (mutationOperation is EntityActionOperation.Create)
            {
                if (!IsPointMutation(context))
                {
                    inputArgumentName = MutationBuilder.ARRAY_INPUT_ARGUMENT_NAME;
                }

                AuthorizeEntityAndFieldsForMutation(context, clientRole, entityName, mutationOperation, inputArgumentName, parameters);
            }
            else
            {
                List<string> inputArgumentKeys;
                if (mutationOperation != EntityActionOperation.Delete)
                {
                    inputArgumentKeys = BaseSqlQueryStructure.GetSubArgumentNamesFromGQLMutArguments(inputArgumentName, parameters);
                }
                else
                {
                    inputArgumentKeys = parameters.Keys.ToList();
                }

                if (!AreFieldsAuthorizedForEntity(clientRole, entityName, mutationOperation, inputArgumentKeys))
                {
                    throw new DataApiBuilderException(
                            message: "Unauthorized due to one or more fields in this mutation.",
                            statusCode: HttpStatusCode.Forbidden,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed
                        );
                }
            }
        }

        private bool AreFieldsAuthorizedForEntity(string clientRole, string entityName, EntityActionOperation mutationOperation, IEnumerable<string> inputArgumentKeys)
        {
            bool isAuthorized; // False by default.

            switch (mutationOperation)
            {
                case EntityActionOperation.UpdateGraphQL:
                    isAuthorized = _authorizationResolver.AreColumnsAllowedForOperation(entityName, roleName: clientRole, operation: EntityActionOperation.Update, inputArgumentKeys);
                    break;
                case EntityActionOperation.Create:
                    isAuthorized = _authorizationResolver.AreColumnsAllowedForOperation(entityName, roleName: clientRole, operation: mutationOperation, inputArgumentKeys);
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

            return isAuthorized;
        }

        /// <summary>
        /// Performs authorization checks on entity level permissions and field level permissions for every entity and field
        /// referenced in a GraphQL mutation for the given client role.
        /// </summary>
        /// <param name="context">Middleware context.</param>
        /// <param name="clientRole">Client role header value extracted from the middleware context of the mutation</param>
        /// <param name="topLevelEntityName">Top level entity name.</param>
        /// <param name="operation">Mutation operation</param>
        /// <param name="inputArgumentName">Name of the input argument (differs based on point/multiple mutation).</param>
        /// <param name="parametersDictionary">Dictionary of key/value pairs for the argument name/value.</param>
        /// <exception cref="DataApiBuilderException">Throws exception when an authorization check fails.</exception>
        private void AuthorizeEntityAndFieldsForMutation(
            IMiddlewareContext context,
            string clientRole,
            string topLevelEntityName,
            EntityActionOperation operation,
            string inputArgumentName,
            IDictionary<string, object?> parametersDictionary
        )
        {
            if (context.Selection.Field.Arguments.TryGetField(inputArgumentName, out IInputField? schemaForArgument))
            {
                // Dictionary to store all the entities and their corresponding exposed column names referenced in the mutation.
                Dictionary<string, HashSet<string>> entityToExposedColumns = new();
                if (parametersDictionary.TryGetValue(inputArgumentName, out object? parameters))
                {
                    // Get all the entity names and field names referenced in the mutation.
                    PopulateMutationEntityAndFieldsToAuthorize(entityToExposedColumns, schemaForArgument, topLevelEntityName, context, parameters!);
                }
                else
                {
                    throw new DataApiBuilderException(
                            message: $"{inputArgumentName} cannot be null for mutation:{context.Selection.Field.Name.Value}.",
                            statusCode: HttpStatusCode.BadRequest,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest
                        );
                }

                // Perform authorization checks at field level.
                foreach ((string entityNameInMutation, HashSet<string> exposedColumnsInEntity) in entityToExposedColumns)
                {
                    if (!AreFieldsAuthorizedForEntity(clientRole, entityNameInMutation, operation, exposedColumnsInEntity))
                    {
                        throw new DataApiBuilderException(
                            message: $"Unauthorized due to one or more fields in this mutation.",
                            statusCode: HttpStatusCode.Forbidden,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed
                        );
                    }
                }
            }
            else
            {
                throw new DataApiBuilderException(
                    message: $"Could not interpret the schema for the input argument: {inputArgumentName}",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }
        }

        /// <summary>
        /// Helper method to collect names of all the fields referenced from every entity in a GraphQL mutation.
        /// </summary>
        /// <param name="entityToExposedColumns">Dictionary to store all the entities and their corresponding exposed column names referenced in the mutation.</param>
        /// <param name="schema">Schema for the input field.</param>
        /// <param name="entityName">Name of the entity.</param>
        /// <param name="context">Middleware Context.</param>
        /// <param name="parameters">Value for the input field.</param>
        /// <example>       1. mutation {
        ///                 createbook(
        ///                     item: {
        ///                         title: "book #1",
        ///                         reviews: [{ content: "Good book." }, { content: "Great book." }],
        ///                         publishers: { name: "Macmillan publishers" },
        ///                         authors: [{ birthdate: "1997-09-03", name: "Red house authors", author_name: "Dan Brown" }]
        ///                     })
        ///                 {
        ///                     id
        ///                 }
        ///                 2. mutation {
        ///                 createbooks(
        ///                     items: [{
        ///                         title: "book #1",
        ///                         reviews: [{ content: "Good book." }, { content: "Great book." }],
        ///                         publishers: { name: "Macmillan publishers" },
        ///                         authors: [{ birthdate: "1997-09-03", name: "Red house authors", author_name: "Dan Brown" }]
        ///                     },
        ///                     {
        ///                         title: "book #2",
        ///                         reviews: [{ content: "Awesome book." }, { content: "Average book." }],
        ///                         publishers: { name: "Pearson Education" },
        ///                         authors: [{ birthdate: "1990-11-04", name: "Penguin Random House", author_name: "William Shakespeare" }]
        ///                     }])
        ///                 {
        ///                     items{
        ///                         id
        ///                         title
        ///                     }
        ///                 }</example>
        private void PopulateMutationEntityAndFieldsToAuthorize(
            Dictionary<string, HashSet<string>> entityToExposedColumns,
            IInputField schema,
            string entityName,
            IMiddlewareContext context,
            object parameters)
        {
            if (parameters is List<ObjectFieldNode> listOfObjectFieldNode)
            {
                // For the example createbook mutation written above, the object value for `item` is interpreted as a List<ObjectFieldNode> i.e.
                // all the fields present for item namely- title, reviews, publishers, authors are interpreted as ObjectFieldNode.
                ProcessObjectFieldNodesForAuthZ(
                    entityToExposedColumns: entityToExposedColumns,
                    schemaObject: ExecutionHelper.InputObjectTypeFromIInputField(schema),
                    entityName: entityName,
                    context: context,
                    fieldNodes: listOfObjectFieldNode);
            }
            else if (parameters is List<IValueNode> listOfIValueNode)
            {
                // For the example createbooks mutation written above, the list value for `items` is interpreted as a List<IValueNode>.
                listOfIValueNode.ForEach(iValueNode => PopulateMutationEntityAndFieldsToAuthorize(
                    entityToExposedColumns: entityToExposedColumns,
                    schema: schema,
                    entityName: entityName,
                    context: context,
                    parameters: iValueNode));
            }
            else if (parameters is ObjectValueNode objectValueNode)
            {
                // For the example createbook mutation written above, the node for publishers field is interpreted as an ObjectValueNode.
                // Similarly the individual node (elements in the list) for the reviews, authors ListValueNode(s) are also interpreted as ObjectValueNode(s).
                ProcessObjectFieldNodesForAuthZ(
                    entityToExposedColumns: entityToExposedColumns,
                    schemaObject: ExecutionHelper.InputObjectTypeFromIInputField(schema),
                    entityName: entityName,
                    context: context,
                    fieldNodes: objectValueNode.Fields);
            }
            else
            {
                ListValueNode listValueNode = (ListValueNode)parameters;
                // For the example createbook mutation written above, the list values for reviews and authors fields are interpreted as ListValueNode.
                // All the nodes in the ListValueNode are parsed one by one.
                listValueNode.GetNodes().ToList().ForEach(objectValueNodeInListValueNode => PopulateMutationEntityAndFieldsToAuthorize(
                    entityToExposedColumns: entityToExposedColumns,
                    schema: schema,
                    entityName: entityName,
                    context: context,
                    parameters: objectValueNodeInListValueNode));
            }
        }

        /// <summary>
        /// Helper method to iterate over all the fields present in the input for the current field and add it to the dictionary
        /// containing all entities and their corresponding fields.
        /// </summary>
        /// <param name="entityToExposedColumns">Dictionary to store all the entities and their corresponding exposed column names referenced in the mutation.</param>
        /// <param name="schemaObject">Input object type for the field.</param>
        /// <param name="entityName">Name of the entity.</param>
        /// <param name="context">Middleware context.</param>
        /// <param name="fieldNodes">List of ObjectFieldNodes for the the input field.</param>
        private void ProcessObjectFieldNodesForAuthZ(
            Dictionary<string, HashSet<string>> entityToExposedColumns,
            InputObjectType schemaObject,
            string entityName,
            IMiddlewareContext context,
            IReadOnlyList<ObjectFieldNode> fieldNodes)
        {
            RuntimeConfig runtimeConfig = _runtimeConfigProvider.GetConfig();
            entityToExposedColumns.TryAdd(entityName, new HashSet<string>());
            string dataSourceName = GraphQLUtils.GetDataSourceNameFromGraphQLContext(context, runtimeConfig);
            ISqlMetadataProvider metadataProvider = _sqlMetadataProviderFactory.GetMetadataProvider(dataSourceName);
            foreach (ObjectFieldNode field in fieldNodes)
            {
                Tuple<IValueNode?, SyntaxKind> fieldDetails = GraphQLUtils.GetFieldDetails(field.Value, context.Variables);
                SyntaxKind underlyingFieldKind = fieldDetails.Item2;

                // For a column field, we do not have to recurse to process fields in the value - which is required for relationship fields.
                if (GraphQLUtils.IsScalarField(underlyingFieldKind) || underlyingFieldKind is SyntaxKind.NullValue)
                {
                    // This code block can be hit in 3 cases:
                    // Case 1. We are processing a column which belongs to this entity,
                    //
                    // Case 2. We are processing the fields for a linking input object. Linking input objects enable users to provide
                    // input for fields belonging to the target entity and the linking entity. Hence the backing column for fields
                    // belonging to the linking entity will not be present in the source definition of this target entity.
                    // We need to skip such fields belonging to linking table as we do not perform authorization checks on them.
                    //
                    // Case 3. When a relationship field is assigned a null value. Such a field also needs to be ignored.
                    if (metadataProvider.TryGetBackingColumn(entityName, field.Name.Value, out string? _))
                    {
                        // Only add those fields to this entity's set of fields which belong to this entity and not the linking entity,
                        // i.e. for Case 1.
                        entityToExposedColumns[entityName].Add(field.Name.Value);
                    }
                }
                else
                {
                    string relationshipName = field.Name.Value;
                    string targetEntityName = runtimeConfig.Entities![entityName].Relationships![relationshipName].TargetEntity;

                    // Recurse to process fields in the value of this relationship field.
                    PopulateMutationEntityAndFieldsToAuthorize(
                        entityToExposedColumns,
                        schemaObject.Fields[relationshipName],
                        targetEntityName,
                        context,
                        fieldDetails.Item1!);
                }
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
            return string.IsNullOrEmpty(dataSourceName) ? _runtimeConfigProvider.GetConfig().DefaultDataSourceName : dataSourceName;
        }

        /// <summary>
        /// Returns DbOperationResult with required result.
        /// </summary>
        private static Tuple<JsonDocument?, IMetadata?> GetDbOperationResultJsonDocument(string result)
        {
            // Create a JSON object with one field "result" and value result
            JsonObject jsonObject = new() { { "result", result } };

            return new Tuple<JsonDocument?, IMetadata?>(
                JsonDocument.Parse(jsonObject.ToString()),
                PaginationMetadata.MakeEmptyPaginationMetadata());
        }
    }
}
