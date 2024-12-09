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
using Azure.DataApiBuilder.Service.Services;
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

            string inputArgumentName = IsPointMutation(context) ? MutationBuilder.ITEM_INPUT_ARGUMENT_NAME : MutationBuilder.ARRAY_INPUT_ARGUMENT_NAME;
            if (_runtimeConfigProvider.GetConfig().IsMultipleCreateOperationEnabled() &&
                parameters.TryGetValue(inputArgumentName, out object? param) &&
                mutationOperation is EntityActionOperation.Create)
            {
                // Multiple create mutation request is validated to ensure that the request is valid semantically.
                IInputField schemaForArgument = context.Selection.Field.Arguments[inputArgumentName];
                MultipleMutationEntityInputValidationContext multipleMutationEntityInputValidationContext = new(
                    entityName: entityName,
                    parentEntityName: string.Empty,
                    columnsDerivedFromParentEntity: new(),
                    columnsToBeDerivedFromEntity: new());
                MultipleMutationInputValidator multipleMutationInputValidator = new(sqlMetadataProviderFactory: _sqlMetadataProviderFactory, runtimeConfigProvider: _runtimeConfigProvider);
                multipleMutationInputValidator.ValidateGraphQLValueNode(
                    schema: schemaForArgument,
                    context: context,
                    parameters: param,
                    nestingLevel: 0,
                    multipleMutationEntityInputValidationContext: multipleMutationEntityInputValidationContext);
            }

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
                    // This code block contains logic for handling multiple create mutation operations.
                    else if (mutationOperation is EntityActionOperation.Create && _runtimeConfigProvider.GetConfig().IsMultipleCreateOperationEnabled())
                    {
                        bool isPointMutation = IsPointMutation(context);

                        List<IDictionary<string, object?>> primaryKeysOfCreatedItems = PerformMultipleCreateOperation(
                                    entityName,
                                    context,
                                    parameters,
                                    sqlMetadataProvider,
                                    !isPointMutation);

                        // For point create multiple mutation operation, a single item is created in the
                        // table backing the top level entity. So, the PK of the created item is fetched and
                        // used when calling the query engine to process the selection set.
                        // For many type multiple create operation, one or more than one item are created
                        // in the table backing the top level entity. So, the PKs of the created items are
                        // fetched and used when calling the query engine to process the selection set.
                        // Point multiple create mutation and many type multiple create mutation are calling different
                        // overloaded method ("ExecuteAsync") of the query engine to process the selection set.
                        if (isPointMutation)
                        {
                            result = await queryEngine.ExecuteAsync(
                                        context,
                                        primaryKeysOfCreatedItems[0],
                                        dataSourceName);
                        }
                        else
                        {
                            result = await queryEngine.ExecuteMultipleCreateFollowUpQueryAsync(
                                        context,
                                        primaryKeysOfCreatedItems,
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
                if (!GetHttpContext().Request.Headers.TryGetValue(AuthorizationResolver.CLIENT_ROLE_HEADER, out StringValues headerValues) && headerValues.Count != 1)
                {
                    throw new DataApiBuilderException(
                            message: $"No role found.",
                            statusCode: HttpStatusCode.Forbidden,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed);
                }

                string roleName = headerValues.ToString();
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
                        queryExecutor.ExtractResultSetFromDbDataReaderAsync,
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
        /// <param name="mutationInputParamsFromGQLContext">Multiple Create mutation's input parameters retrieved from GraphQL context</param>
        /// <param name="sqlMetadataProvider">SqlMetadaprovider</param>
        /// <param name="context">Hotchocolate's context for the graphQL request.</param>
        /// <param name="isMultipleInputType">Boolean indicating whether the create operation is for multiple items.</param>
        /// <returns>Primary keys of the created records (in the top level entity).</returns>
        /// <exception cref="DataApiBuilderException"></exception>
        private List<IDictionary<string, object?>> PerformMultipleCreateOperation(
                string entityName,
                IMiddlewareContext context,
                IDictionary<string, object?> mutationInputParamsFromGQLContext,
                ISqlMetadataProvider sqlMetadataProvider,
                bool isMultipleInputType = false)
        {
            // rootFieldName can be either "item" or "items" depending on whether the operation
            // is point multiple create or many-type multiple create. 
            string rootFieldName = isMultipleInputType ? MULTIPLE_INPUT_ARGUEMENT_NAME : SINGLE_INPUT_ARGUEMENT_NAME;

            // Parse the hotchocolate input parameters into .net object types
            object? parsedInputParams = GQLMultipleCreateArgumentToDictParams(context, rootFieldName, mutationInputParamsFromGQLContext);

            if (parsedInputParams is null)
            {
                throw new DataApiBuilderException(
                    message: "The input for multiple create mutation operation cannot be null",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }

            // List of Primary keys of the created records in the top level entity.
            // Each dictionary in the list corresponds to the PKs of a single record.
            // For point multiple create operation, only one entry will be present.
            List<IDictionary<string, object?>> primaryKeysOfCreatedItemsInTopLevelEntity = new();

            if (!mutationInputParamsFromGQLContext.TryGetValue(rootFieldName, out object? unparsedInputFieldsForRootField)
                || unparsedInputFieldsForRootField is null)
            {
                throw new DataApiBuilderException(
                    message: $"Mutation Request should contain the expected argument: {rootFieldName} in the input",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }

            if (isMultipleInputType)
            {
                // For a many type multiple create operation, after parsing the hotchocolate input parameters, the resultant data structure is a list of dictionaries.
                // Each entry in the list corresponds to the input parameters for a single input item.
                // The fields belonging to the inputobjecttype are converted to
                // 1. Scalar input fields: Key - Value pair of field name and field value.
                // 2. Object type input fields: Key - Value pair of relationship name and a dictionary of parameters (takes place for 1:1, N:1 relationship types)
                // 3. List type input fields: key - Value pair of relationship name and a list of dictionary of parameters (takes place for 1:N, M:N relationship types) 
                List<IDictionary<string, object?>> parsedMutationInputFields = (List<IDictionary<string, object?>>)parsedInputParams;

                // For many type multiple create operation, the "parameters" dictionary is a key pair of <"items", List<IValueNode>>.
                // Ideally, the input provided for "items" field should not be any other type than List<IValueNode>
                // as HotChocolate will detect and throw errors before the execution flow reaches here.
                // However, this acts as a guard to ensure that the right input type for "items" field is used.
                if (unparsedInputFieldsForRootField is not List<IValueNode> unparsedInputForRootField)
                {
                    throw new DataApiBuilderException(
                        message: $"Unsupported type used with {rootFieldName} in the create mutation input",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                }

                // In the following loop, the input elements in "parsedInputList" are iterated and processed.
                // idx tracks the index number to fetch the corresponding unparsed hotchocolate input parameters from "paramList".
                // Both parsed and unparsed input parameters are necessary for successfully determing the order of insertion
                // among the entities involved in the multiple create mutation request.
                int itemsIndex = 0;

                // Consider a mutation request such as the following
                // mutation{
                //  createbooks(items: [
                //                {
                //                    title: "Harry Potter and the Chamber of Secrets",
                //                    publishers: { name: "Bloomsbury" }
                //                 },
                //                 {
                //                    title: "Educated",
                //                    publishers: { name: "Random House"}
                //                 }
                //     ]){
                //      items{
                //         id
                //         title 
                //         publisher_id 
                //      }
                //   }
                // }
                // In the above mutation, each element in the 'items' array forms the 'parsedInputList'.
                // items[itemsIndex].Key -> field(s) in the input such as 'title' and 'publishers' (type: string)
                // items[itemsIndex].Value -> field value(s) for each corresponding field (type: object?)
                // items[0] -> object with title 'Harry Potter and the Chamber of Secrets'
                // items[1] -> object with title 'Educated'
                // The processing logic is distinctly executed for each object in `items'.
                foreach (IDictionary<string, object?> parsedMutationInputField in parsedMutationInputFields)
                {
                    MultipleCreateStructure multipleCreateStructure = new(
                        entityName: entityName,
                        parentEntityName: string.Empty,
                        inputMutParams: parsedMutationInputField);

                    Dictionary<string, Dictionary<string, object?>> primaryKeysOfCreatedItem = new();

                    IValueNode? unparsedFieldNodeForCurrentItem = unparsedInputForRootField[itemsIndex];
                    if (unparsedFieldNodeForCurrentItem is null)
                    {
                        throw new DataApiBuilderException(
                            message: "Error when processing the mutation request",
                            statusCode: HttpStatusCode.BadRequest,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                    }

                    ProcessMultipleCreateInputField(context, unparsedFieldNodeForCurrentItem.Value, sqlMetadataProvider, multipleCreateStructure, nestingLevel: 0);

                    // Ideally the CurrentEntityCreatedValues should not be null. CurrentEntityCreatedValues being null indicates that the create operation
                    // has failed and that will result in an exception being thrown.
                    // This condition acts as a guard against having to deal with null values during selection set resolution.
                    if (multipleCreateStructure.CurrentEntityCreatedValues is not null)
                    {
                        primaryKeysOfCreatedItemsInTopLevelEntity.Add(FetchPrimaryKeyFieldValues(sqlMetadataProvider, entityName, multipleCreateStructure.CurrentEntityCreatedValues));
                    }

                    itemsIndex++;
                }
            }
            else
            {
                // Consider a mutation request such as the following
                // mutation{
                //  createbook(item:{
                //              title: "Harry Potter and the Chamber of Secrets",
                //              publishers: {
                //                  name: "Bloomsbury"
                //                }})
                //  {
                //     id
                //     title
                //     publisher_id
                //  }
                // For the above mutation request, the parsedInputParams will be a dictionary with the following key value pairs
                //
                // Key          Value
                // title        Harry Potter and the Chamber of Secrets
                // publishers   Dictionary<name, Bloomsbury>
                IDictionary<string, object?> parsedInputFields = (IDictionary<string, object?>)parsedInputParams;

                // For point multiple create operation, the "parameters" dictionary is a key pair of <"item", List<ObjectFieldNode>>.
                // The value field retrieved using the key "item" cannot be of any other type.
                // Ideally, this condition should never be hit, because such cases should be caught by Hotchocolate but acts as a guard against using any other types with "item" field
                if (unparsedInputFieldsForRootField is not List<ObjectFieldNode> unparsedInputFields)
                {
                    throw new DataApiBuilderException(
                        message: $"Unsupported type used with {rootFieldName} in the create mutation input",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                }

                MultipleCreateStructure multipleCreateStructure = new(
                    entityName: entityName,
                    parentEntityName: entityName,
                    inputMutParams: parsedInputFields);

                ProcessMultipleCreateInputField(context, unparsedInputFields, sqlMetadataProvider, multipleCreateStructure, nestingLevel: 0);

                if (multipleCreateStructure.CurrentEntityCreatedValues is not null)
                {
                    primaryKeysOfCreatedItemsInTopLevelEntity.Add(FetchPrimaryKeyFieldValues(sqlMetadataProvider, entityName, multipleCreateStructure.CurrentEntityCreatedValues));
                }
            }

            return primaryKeysOfCreatedItemsInTopLevelEntity;
        }

        /// <summary>
        /// 1. Identifies the order of insertion into tables involved in the create mutation request.
        /// 2. Builds and executes the necessary database queries to insert all the data into appropriate tables.
        /// </summary>
        /// <param name="context">Hotchocolate's context for the graphQL request.</param>
        /// <param name="unparsedInputFields">Mutation input parameter from GQL Context for the current item being processed</param>
        /// <param name="sqlMetadataProvider">SqlMetadataprovider for the given database type.</param>
        /// <param name="multipleCreateStructure">Wrapper object for the current entity for performing the multiple create mutation operation</param>
        /// <param name="nestingLevel">Current depth of nesting in the multiple-create request</param>
        private void ProcessMultipleCreateInputField(
            IMiddlewareContext context,
            object? unparsedInputFields,
            ISqlMetadataProvider sqlMetadataProvider,
            MultipleCreateStructure multipleCreateStructure,
            int nestingLevel)
        {

            if (multipleCreateStructure.InputMutParams is null || unparsedInputFields is null)
            {
                throw new DataApiBuilderException(
                        message: "The input for a multiple create mutation operation cannot be null.",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }

            // For One - Many and Many - Many relationship types, processing logic is distinctly executed for each
            // object in the input list.
            // So, when the input parameters is of list type, we iterate over the list
            // and call the same method for each element.
            if (multipleCreateStructure.InputMutParams.GetType().GetGenericTypeDefinition() == typeof(List<>))
            {
                List<IDictionary<string, object?>> parsedInputItems = (List<IDictionary<string, object?>>)multipleCreateStructure.InputMutParams;
                List<IValueNode> unparsedInputFieldList = (List<IValueNode>)unparsedInputFields;
                int parsedInputItemIndex = 0;

                foreach (IDictionary<string, object?> parsedInputItem in parsedInputItems)
                {
                    MultipleCreateStructure multipleCreateStructureForCurrentItem = new(
                        entityName: multipleCreateStructure.EntityName,
                        parentEntityName: multipleCreateStructure.ParentEntityName,
                        inputMutParams: parsedInputItem,
                        isLinkingTableInsertionRequired: multipleCreateStructure.IsLinkingTableInsertionRequired)
                    {
                        CurrentEntityParams = multipleCreateStructure.CurrentEntityParams,
                        LinkingTableParams = multipleCreateStructure.LinkingTableParams
                    };

                    Dictionary<string, Dictionary<string, object?>> primaryKeysOfCreatedItems = new();
                    IValueNode? nodeForCurrentInput = unparsedInputFieldList[parsedInputItemIndex];
                    if (nodeForCurrentInput is null)
                    {
                        throw new DataApiBuilderException(
                            message: "Error when processing the mutation request",
                            statusCode: HttpStatusCode.BadRequest,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                    }

                    ProcessMultipleCreateInputField(context, nodeForCurrentInput.Value, sqlMetadataProvider, multipleCreateStructureForCurrentItem, nestingLevel);
                    parsedInputItemIndex++;
                }
            }
            else
            {
                if (unparsedInputFields is not List<ObjectFieldNode> parameterNodes)
                {
                    throw new DataApiBuilderException(
                        message: "Error occurred while processing the mutation request",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                }

                string entityName = multipleCreateStructure.EntityName;
                Entity entity = _runtimeConfigProvider.GetConfig().Entities[entityName];

                // Classifiy the relationship fields (if present in the input request) into referencing and referenced relationships and
                // populate multipleCreateStructure.ReferencingRelationships and multipleCreateStructure.ReferencedRelationships respectively.
                DetermineReferencedAndReferencingRelationships(context, multipleCreateStructure, sqlMetadataProvider, entity.Relationships, parameterNodes);
                PopulateCurrentAndLinkingEntityParams(multipleCreateStructure, sqlMetadataProvider, entity.Relationships);

                SourceDefinition currentEntitySourceDefinition = sqlMetadataProvider.GetSourceDefinition(entityName);
                currentEntitySourceDefinition.SourceEntityRelationshipMap.TryGetValue(entityName, out RelationshipMetadata? currentEntityRelationshipMetadata);

                // Process referenced relationships
                foreach ((string relationshipName, object? relationshipFieldValue) in multipleCreateStructure.ReferencedRelationships)
                {
                    string relatedEntityName = GraphQLUtils.GetRelationshipTargetEntityName(entity, entityName, relationshipName);
                    MultipleCreateStructure referencedRelationshipMultipleCreateStructure = new(entityName: relatedEntityName, parentEntityName: entityName, inputMutParams: relationshipFieldValue);
                    IValueNode node = GraphQLUtils.GetFieldNodeForGivenFieldName(parameterNodes, relationshipName);
                    ProcessMultipleCreateInputField(context, node.Value, sqlMetadataProvider, referencedRelationshipMultipleCreateStructure, nestingLevel + 1);

                    if (sqlMetadataProvider.TryGetFKDefinition(
                                                    sourceEntityName: entityName,
                                                    targetEntityName: relatedEntityName,
                                                    referencingEntityName: entityName,
                                                    referencedEntityName: relatedEntityName,
                                                    out ForeignKeyDefinition? foreignKeyDefinition,
                                                    isMToNRelationship: false))
                    {
                        PopulateReferencingFields(
                            sqlMetadataProvider: sqlMetadataProvider,
                            multipleCreateStructure: multipleCreateStructure,
                            fkDefinition: foreignKeyDefinition,
                            computedRelationshipFields: referencedRelationshipMultipleCreateStructure.CurrentEntityCreatedValues,
                            isLinkingTable: false,
                            entityName: relatedEntityName);
                    }
                }

                multipleCreateStructure.CurrentEntityCreatedValues = BuildAndExecuteInsertDbQueries(
                                                                          sqlMetadataProvider: sqlMetadataProvider,
                                                                          entityName: entityName,
                                                                          parentEntityName: entityName,
                                                                          parameters: multipleCreateStructure.CurrentEntityParams!,
                                                                          sourceDefinition: currentEntitySourceDefinition,
                                                                          isLinkingEntity: false,
                                                                          nestingLevel: nestingLevel);

                //Perform an insertion in the linking table if required
                if (multipleCreateStructure.IsLinkingTableInsertionRequired)
                {
                    if (multipleCreateStructure.LinkingTableParams is null)
                    {
                        multipleCreateStructure.LinkingTableParams = new Dictionary<string, object?>();
                    }

                    // Consider the mutation request:
                    // mutation{
                    //     createbook(item: {
                    //         title: "Book Title",
                    //         publisher_id: 1234,
                    //         authors: [
                    //             {...} ,
                    //             {...}
                    //         ]
                    //      }) {
                    //          ...
                    //      }
                    // There exists two relationships for a linking table.
                    // 1. Relationship between the parent entity (Book) and the linking table.
                    // 2. Relationship between the current entity (Author) and the linking table.
                    // To construct the insert database query for the linking table, relationship fields from both the
                    // relationships are required. 

                    // Populate Current entity's relationship fields
                    List<ForeignKeyDefinition> foreignKeyDefinitions = currentEntityRelationshipMetadata!.TargetEntityToFkDefinitionMap[multipleCreateStructure.ParentEntityName];
                    ForeignKeyDefinition fkDefinition = foreignKeyDefinitions[0];
                    PopulateReferencingFields(sqlMetadataProvider, multipleCreateStructure, fkDefinition, multipleCreateStructure.CurrentEntityCreatedValues, isLinkingTable: true);

                    string linkingEntityName = GraphQLUtils.GenerateLinkingEntityName(multipleCreateStructure.ParentEntityName, entityName);
                    SourceDefinition linkingTableSourceDefinition = sqlMetadataProvider.GetSourceDefinition(linkingEntityName);

                    _ = BuildAndExecuteInsertDbQueries(
                            sqlMetadataProvider: sqlMetadataProvider,
                            entityName: linkingEntityName,
                            parentEntityName: entityName,
                            parameters: multipleCreateStructure.LinkingTableParams!,
                            sourceDefinition: linkingTableSourceDefinition,
                            isLinkingEntity: true,
                            nestingLevel: nestingLevel);
                }

                // Process referencing relationships
                foreach ((string relationshipFieldName, object? relationshipFieldValue) in multipleCreateStructure.ReferencingRelationships)
                {
                    string relatedEntityName = GraphQLUtils.GetRelationshipTargetEntityName(entity, entityName, relationshipFieldName);
                    MultipleCreateStructure referencingRelationshipMultipleCreateStructure = new(entityName: relatedEntityName,
                                                                                                 parentEntityName: entityName,
                                                                                                 inputMutParams: relationshipFieldValue,
                                                                                                 isLinkingTableInsertionRequired: GraphQLUtils.IsMToNRelationship(entity, relationshipFieldName));
                    IValueNode node = GraphQLUtils.GetFieldNodeForGivenFieldName(parameterNodes, relationshipFieldName);

                    // Many-Many relationships are marked as Referencing relationships
                    // because the linking table insertion can happen only
                    // when records have been successfully created in both the entities involved in the relationship.
                    // The entities involved do not derive any fields from each other. Only the linking table derives the
                    // primary key fields from the entities involved in the relationship.
                    // For a M:N relationships, the referencing fields are populated in LinkingTableParams whereas for  
                    // a 1:N relationship, referencing fields will be populated in CurrentEntityParams.
                    if (sqlMetadataProvider.TryGetFKDefinition(
                            sourceEntityName: entityName,
                            targetEntityName: relatedEntityName,
                            referencingEntityName: relatedEntityName,
                            referencedEntityName: entityName,
                            out ForeignKeyDefinition? referencingEntityFKDefinition,
                            isMToNRelationship: referencingRelationshipMultipleCreateStructure.IsLinkingTableInsertionRequired))
                    {
                        PopulateReferencingFields(
                            sqlMetadataProvider: sqlMetadataProvider,
                            multipleCreateStructure: referencingRelationshipMultipleCreateStructure,
                            fkDefinition: referencingEntityFKDefinition,
                            computedRelationshipFields: multipleCreateStructure.CurrentEntityCreatedValues,
                            isLinkingTable: referencingRelationshipMultipleCreateStructure.IsLinkingTableInsertionRequired,
                            entityName: entityName);
                    }

                    ProcessMultipleCreateInputField(context, node.Value, sqlMetadataProvider, referencingRelationshipMultipleCreateStructure, nestingLevel + 1);
                }
            }
        }

        /// <summary>
        /// Builds and executes the insert database query necessary for creating an item in the table
        /// the entity.
        /// </summary>
        /// <param name="sqlMetadataProvider">SqlMetadaProvider object for the given database</param>
        /// <param name="entityName">Current entity name</param>
        /// <param name="parentEntityName">Parent entity name</param>
        /// <param name="parameters">Dictionary containing the data ncessary to create a record in the table</param>
        /// <param name="sourceDefinition">Entity's source definition object</param>
        /// <param name="isLinkingEntity">Indicates whether the entity is a linking entity</param>
        /// <param name="nestingLevel">Current depth of nesting in the multiple-create request</param>
        /// <returns>Created record in the database as a dictionary</returns>
        private Dictionary<string, object?> BuildAndExecuteInsertDbQueries(ISqlMetadataProvider sqlMetadataProvider,
                                                                           string entityName,
                                                                           string parentEntityName,
                                                                           IDictionary<string, object?> parameters,
                                                                           SourceDefinition sourceDefinition,
                                                                           bool isLinkingEntity,
                                                                           int nestingLevel)
        {
            SqlInsertStructure sqlInsertStructure = new(
                                                     entityName: entityName,
                                                     sqlMetadataProvider: sqlMetadataProvider,
                                                     authorizationResolver: _authorizationResolver,
                                                     gQLFilterParser: _gQLFilterParser,
                                                     mutationParams: parameters,
                                                     httpContext: GetHttpContext(),
                                                     isLinkingEntity: isLinkingEntity);

            IQueryBuilder queryBuilder = _queryManagerFactory.GetQueryBuilder(sqlMetadataProvider.GetDatabaseType());
            IQueryExecutor queryExecutor = _queryManagerFactory.GetQueryExecutor(sqlMetadataProvider.GetDatabaseType());

            // When the entity is a linking entity, the parent entity's name is used to get the
            // datasource name. Otherwise, the entity's name is used.
            string dataSourceName = isLinkingEntity ? _runtimeConfigProvider.GetConfig().GetDataSourceNameFromEntityName(parentEntityName)
                                                    : _runtimeConfigProvider.GetConfig().GetDataSourceNameFromEntityName(entityName);
            string queryString = queryBuilder.Build(sqlInsertStructure);
            Dictionary<string, DbConnectionParam> queryParameters = sqlInsertStructure.Parameters;

            List<string> exposedColumnNames = new();
            if (sqlMetadataProvider.TryGetExposedFieldToBackingFieldMap(entityName, out IReadOnlyDictionary<string, string>? exposedFieldToBackingFieldMap))
            {
                exposedColumnNames = exposedFieldToBackingFieldMap.Keys.ToList();
            }

            DbResultSet? dbResultSet;
            DbResultSetRow? dbResultSetRow;
            dbResultSet = queryExecutor.ExecuteQuery(
                queryString,
                queryParameters,
                queryExecutor.ExtractResultSetFromDbDataReader,
                GetHttpContext(),
                EnumerableUtilities.IsNullOrEmpty(exposedColumnNames) ? sourceDefinition.Columns.Keys.ToList() : exposedColumnNames,
                dataSourceName);

            dbResultSetRow = dbResultSet is not null ? (dbResultSet.Rows.FirstOrDefault() ?? new DbResultSetRow()) : null;
            if (dbResultSetRow is null || dbResultSetRow.Columns.Count == 0)
            {
                if (isLinkingEntity)
                {
                    throw new DataApiBuilderException(
                        message: $"Could not insert row with given values in the linking table joining entities: {entityName} and {parentEntityName} at nesting level : {nestingLevel}",
                        statusCode: HttpStatusCode.InternalServerError,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed);
                }
                else
                {
                    if (dbResultSetRow is null)
                    {
                        throw new DataApiBuilderException(
                            message: "No data returned back from database.",
                            statusCode: HttpStatusCode.InternalServerError,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed);
                    }
                    else
                    {
                        throw new DataApiBuilderException(
                            message: $"Could not insert row with given values for entity: {entityName} at nesting level : {nestingLevel}",
                            statusCode: HttpStatusCode.Forbidden,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.DatabasePolicyFailure);
                    }
                }
            }

            return dbResultSetRow.Columns;
        }

        /// <summary>
        /// Helper method to extract the primary key fields from all the fields of the entity.
        /// </summary>
        /// <param name="sqlMetadataProvider">SqlMetadaProvider object for the given database</param>
        /// <param name="entityName">Name of the entity</param>
        /// <param name="createdValuesForEntityItem">Field::Value dictionary of entity created in the database.</param>
        /// <returns>Primary Key fields</returns>
        private static Dictionary<string, object?> FetchPrimaryKeyFieldValues(ISqlMetadataProvider sqlMetadataProvider, string entityName, Dictionary<string, object?> createdValuesForEntityItem)
        {
            Dictionary<string, object?> pkFields = new();
            SourceDefinition sourceDefinition = sqlMetadataProvider.GetSourceDefinition(entityName);
            foreach (string primaryKey in sourceDefinition.PrimaryKey)
            {
                if (sqlMetadataProvider.TryGetExposedColumnName(entityName, primaryKey, out string? name)
                    && createdValuesForEntityItem.TryGetValue(name, out object? value)
                    && value != null)
                {
                    pkFields.Add(primaryKey, value);
                }
                else
                {
                    throw new DataApiBuilderException(message: $"Primary key field {name} has null value but it is expected to have a non-null value",
                                                      statusCode: HttpStatusCode.InternalServerError,
                                                      subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError);
                }
            }

            return pkFields;
        }

        /// <summary>
        /// Helper method to populate the referencing fields in LinkingEntityParams or CurrentEntityParams depending on whether the current entity is a linking entity or not. 
        /// </summary>
        /// <param name="sqlMetadataProvider">SqlMetadaProvider object for the given database.</param>
        /// <param name="fkDefinition">Foreign Key metadata constructed during engine start-up.</param>
        /// <param name="multipleCreateStructure">Wrapper object assisting with the multiple create operation.</param>
        /// <param name="computedRelationshipFields">Relationship fields obtained as a result of creation of current or parent entity item.</param>
        /// <param name="isLinkingTable">Indicates whether referencing fields are populated for a linking entity.</param>
        /// <param name="entityName">Name of the entity.</param>
        private static void PopulateReferencingFields(ISqlMetadataProvider sqlMetadataProvider, MultipleCreateStructure multipleCreateStructure, ForeignKeyDefinition fkDefinition, Dictionary<string, object?>? computedRelationshipFields, bool isLinkingTable, string? entityName = null)
        {
            if (computedRelationshipFields is null)
            {
                return;
            }

            for (int i = 0; i < fkDefinition.ReferencingColumns.Count; i++)
            {
                string referencingColumnName = fkDefinition.ReferencingColumns[i];
                string referencedColumnName = fkDefinition.ReferencedColumns[i];
                string exposedReferencedColumnName;
                if (isLinkingTable)
                {
                    multipleCreateStructure.LinkingTableParams![referencingColumnName] = computedRelationshipFields[referencedColumnName];
                }
                else
                {
                    if (entityName is not null
                        && sqlMetadataProvider.TryGetExposedColumnName(entityName, referencedColumnName, out string? exposedColumnName))
                    {
                        exposedReferencedColumnName = exposedColumnName;
                    }
                    else
                    {
                        exposedReferencedColumnName = referencedColumnName;
                    }

                    multipleCreateStructure.CurrentEntityParams![referencingColumnName] = computedRelationshipFields[exposedReferencedColumnName];
                }
            }
        }

        /// <summary>
        /// Helper method that looks at the input fields of a given entity and
        /// identifies, classifies the related entities into referenced and referencing entities.
        /// </summary>
        /// <param name="context">Hotchocolate context</param>
        /// <param name="multipleCreateStructure">Wrapper object for the current entity for performing
        /// the multiple create mutation operation</param>
        /// <param name="sqlMetadataProvider">SqlMetadaProvider object for the given database</param>
        /// <param name="topLevelEntityRelationships">Relationship metadata of the source entity</param>
        /// <param name="sourceEntityFields">Field object nodes of the source entity</param>
        private static void DetermineReferencedAndReferencingRelationships(
            IMiddlewareContext context,
            MultipleCreateStructure multipleCreateStructure,
            ISqlMetadataProvider sqlMetadataProvider,
            Dictionary<string, EntityRelationship>? topLevelEntityRelationships,
            List<ObjectFieldNode> sourceEntityFields)
        {

            if (topLevelEntityRelationships is null)
            {
                return;
            }

            // Ideally, this condition should not become true.
            // The input parameters being null should be caught earlier in the flow.
            // Nevertheless, this check is added as a guard against cases where the input parameters are null
            // and is not caught.
            if (multipleCreateStructure.InputMutParams is null)
            {
                throw new DataApiBuilderException(
                    message: "The mutation parameters cannot be null.",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }

            foreach ((string relationshipName, object? relationshipFieldValues) in (Dictionary<string, object?>)multipleCreateStructure.InputMutParams)
            {
                if (topLevelEntityRelationships.TryGetValue(relationshipName, out EntityRelationship? entityRelationship)
                    && entityRelationship is not null)
                {
                    // The linking object not being null indicates that the relationship is a many-to-many relationship.
                    // For M:N realtionship, new item(s) have to be created in the linking table
                    // in addition to the source and target tables.
                    // Creation of item(s) in the linking table is handled when processing the target entity.
                    // To be able to create item(s) in the linking table, PKs of the source and target items are required.
                    // Indirectly, the target entity depends on the PKs of the source entity.
                    // Hence, the target entity is added as a referencing entity.
                    if (!string.IsNullOrWhiteSpace(entityRelationship.LinkingObject))
                    {
                        multipleCreateStructure.ReferencingRelationships.Add(new Tuple<string, object?>(relationshipName, relationshipFieldValues) { });
                        continue;
                    }

                    string targetEntityName = entityRelationship.TargetEntity;
                    Dictionary<string, IValueNode?> columnDataInSourceBody = MultipleCreateOrderHelper.GetBackingColumnDataFromFields(context, multipleCreateStructure.EntityName, sourceEntityFields, sqlMetadataProvider);
                    IValueNode? targetNode = GraphQLUtils.GetFieldNodeForGivenFieldName(objectFieldNodes: sourceEntityFields, fieldName: relationshipName);

                    // In this function call, nestingLevel parameter is set as 0 which might not be accurate.
                    // However, it is irrelevant because nestingLevel is used only for logging error messages
                    // and we do not expect any errors to occur here.
                    // All errors are expected to be caught during request validation.
                    string referencingEntityName = MultipleCreateOrderHelper.GetReferencingEntityName(
                                                                                    context: context,
                                                                                    sourceEntityName: multipleCreateStructure.EntityName,
                                                                                    targetEntityName: targetEntityName,
                                                                                    relationshipName: relationshipName,
                                                                                    metadataProvider: sqlMetadataProvider,
                                                                                    nestingLevel: 0,
                                                                                    columnDataInSourceBody: columnDataInSourceBody,
                                                                                    targetNodeValue: targetNode);

                    if (string.Equals(multipleCreateStructure.EntityName, referencingEntityName, StringComparison.OrdinalIgnoreCase))
                    {
                        multipleCreateStructure.ReferencedRelationships.Add(new Tuple<string, object?>(relationshipName, relationshipFieldValues) { });
                    }
                    else
                    {
                        multipleCreateStructure.ReferencingRelationships.Add(new Tuple<string, object?>(relationshipName, relationshipFieldValues) { });
                    }
                }
            }
        }

        /// <summary>
        /// Helper method which traverses the input fields for a given record and populates the fields/values into the appropriate data structures
        /// storing the field/values belonging to the current entity and the linking entity.
        /// Consider the below multiple create mutation request 
        /// mutation{
        /// createbook(item: {
        ///        title: "Harry Potter and the Goblet of Fire",
        ///        publishers:{
        ///            name: "Bloomsbury"
        ///        }
        ///        authors:[
        ///             {
        ///                name: "J.K Rowling",
        ///                birthdate: "1965-07-31",
        ///                royalty_percentage: 100.0
        ///             }
        ///        ]})
        ///   {
        ///           ...
        ///   }
        ///  The mutation request consists of fields belonging to the
        ///  1. Top Level Entity - Book:
        ///     a) Title
        ///  2. Related Entity - Publisher, Author
        ///  In M:N relationship, the field(s)(e.g. royalty_percentage) belonging to the
        ///  linking entity(book_author_link) is a property of the related entity's input object.
        ///  So, this method identifies and populates 
        ///  1.  multipleCreateStructure.CurrentEntityParams with the current entity's fields.
        ///  2.  multipleCreateStructure.LinkingEntityParams with the linking entity's fields.
        /// </summary>
        /// <param name="multipleCreateStructure">Wrapper object for the current entity for performing the multiple create mutation operation</param>
        /// <param name="sqlMetadataProvider">SqlMetadaProvider object for the given database</param>
        /// <param name="topLevelEntityRelationships">Relationship metadata of the source entity</param>
        private static void PopulateCurrentAndLinkingEntityParams(
                MultipleCreateStructure multipleCreateStructure,
                ISqlMetadataProvider sqlMetadataProvider,
                Dictionary<string, EntityRelationship>? topLevelEntityRelationships)
        {

            if (multipleCreateStructure.InputMutParams is null)
            {
                return;
            }

            foreach ((string fieldName, object? fieldValue) in (Dictionary<string, object?>)multipleCreateStructure.InputMutParams)
            {
                if (topLevelEntityRelationships is not null && topLevelEntityRelationships.ContainsKey(fieldName))
                {
                    continue;
                }

                if (sqlMetadataProvider.TryGetBackingColumn(multipleCreateStructure.EntityName, fieldName, out _))
                {
                    multipleCreateStructure.CurrentEntityParams[fieldName] = fieldValue;
                }
                else
                {
                    multipleCreateStructure.LinkingTableParams[fieldName] = fieldValue;
                }
            }
        }

        /// <summary>
        /// Parse the mutation parameters from Hotchocolate input types to Dictionary of field names and values.
        /// </summary>
        /// <param name="context">GQL middleware context used to resolve the values of arguments</param>
        /// <param name="rootFieldName">GQL field from which to extract the parameters. It is either "item" or "items".</param>
        /// <param name="mutationParameters">Dictionary of mutation parameters</param>
        /// <returns>Parsed input mutation parameters.</returns>
        internal static object? GQLMultipleCreateArgumentToDictParams(
                IMiddlewareContext context,
                string rootFieldName,
                IDictionary<string, object?> mutationParameters)
        {
            if (mutationParameters.TryGetValue(rootFieldName, out object? inputParameters))
            {
                IObjectField fieldSchema = context.Selection.Field;
                IInputField itemsArgumentSchema = fieldSchema.Arguments[rootFieldName];
                InputObjectType inputObjectType = ExecutionHelper.InputObjectTypeFromIInputField(itemsArgumentSchema);
                return GQLMultipleCreateArgumentToDictParamsHelper(context, inputObjectType, inputParameters);
            }
            else
            {
                throw new DataApiBuilderException(
                    message: $"Expected root mutation input field: '{rootFieldName}'.",
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest,
                    statusCode: HttpStatusCode.BadRequest);
            }
        }

        /// <summary>
        /// Helper function to parse the mutation parameters from Hotchocolate input types to
        /// Dictionary of field names and values. The parsed input types will not contain
        /// any hotchocolate types such as IValueNode, ObjectFieldNode, etc.
        /// For multiple create mutation, the input types of a field can be a scalar, object or list type.
        /// This function recursively parses each input type.
        /// Consider the following multiple create mutation requests:
        /// 1. mutation pointMultipleCreateExample{
        ///      createbook(
        ///         item: {
        ///            title: "Harry Potter and the Goblet of Fire",
        ///            publishers: { name: "Bloomsbury" },
        ///            authors: [{ name: "J.K Rowling", birthdate: "1965-07-31", royalty_percentage: 100.0 }],
        ///            reviews: [ {content: "Great book" }, {content: "Wonderful read"}]
        ///         })
        ///      {
        ///         //selection set (not relevant in this function)
        ///      }
        ///    }
        ///    
        /// 2. mutation manyMultipleCreateExample{  
        ///      createbooks(
        ///        items:[{ fieldName0: "fieldValue0"},{fieldNameN: "fieldValueN"}]){  
        ///           //selection set (not relevant in this function)  
        ///        }  
        ///      }  
        /// </summary>
        /// <param name="context">GQL middleware context used to resolve the values of arguments.</param>
        /// <param name="inputObjectType">Type of the input object field.</param>
        /// <param name="inputParameters">Mutation input parameters retrieved from IMiddleware context</param>
        /// <returns>Parsed mutation parameters as either
        /// 1. Dictionary<string, object?> or
        /// 2. List<Dictionary<string, object?>>
        /// </returns>
        internal static object? GQLMultipleCreateArgumentToDictParamsHelper(
            IMiddlewareContext context,
            InputObjectType inputObjectType,
            object? inputParameters)
        {
            // This condition is met for input types that accept an array of values
            // where the mutation input field is 'items' such as 
            // 1. Many-type multiple create operation ---> createbooks, createBookmarks_Multiple:
            // For the mutation manyMultipleCreateExample (outlined in the method summary),
            // the following conditions will evalaute to true for root field 'items'.
            // 2. Input types for 1:N and M:N relationships:
            // For the mutation pointMultipleCreateExample (outlined in the method summary),
            // the following condition will evaluate to true for fields 'authors' and 'reviews'.
            // For both the cases, each element in the input object can be a combination of
            // scalar and relationship fields.
            // The parsing logic is run distinctly for each element by recursively calling the same function.
            // Each parsed input result is stored in a list and finally this list is returned.
            if (inputParameters is List<IValueNode> inputFields)
            {
                List<IDictionary<string, object?>> parsedInputFieldItems = new();
                foreach (IValueNode inputField in inputFields)
                {
                    object? parsedInputFieldItem = GQLMultipleCreateArgumentToDictParamsHelper(
                                            context: context,
                                            inputObjectType: inputObjectType,
                                            inputParameters: inputField.Value);
                    if (parsedInputFieldItem is not null)
                    {
                        parsedInputFieldItems.Add((IDictionary<string, object?>)parsedInputFieldItem);
                    }
                }

                return parsedInputFieldItems;
            }

            // This condition is met when the mutation input is a single item where the
            // mutation input field is 'item' such as
            // 1. Point multiple create operation --> createbook.
            // For the mutation pointMultipleCreateExample (outlined in the method summary),
            // the following condition will evaluate to true for root field 'item'.
            // The inputParameters will contain ObjectFieldNode objects for
            // fields : ['title', 'publishers', 'authors', 'reviews']
            // 2. Relationship fields that are of object type:
            // For the mutation pointMultipleCreateExample (outlined in the method summary),
            // when processing the field 'publishers'. For 'publishers' field, 
            // inputParameters will contain ObjectFieldNode objects for fields: ['name']
            else if (inputParameters is List<ObjectFieldNode> inputFieldNodes)
            {
                Dictionary<string, object?> parsedInputFields = new();
                foreach (ObjectFieldNode inputFieldNode in inputFieldNodes)
                {
                    string fieldName = inputFieldNode.Name.Value;
                    // For the mutation pointMultipleCreateExample (outlined in the method summary),
                    // the following condition will evaluate to true for fields 'authors' and 'reviews'.
                    // Fields 'authors'/'reviews' can again consist of combination of scalar and relationship fields.
                    // So, the input object type for 'authors'/'reviews' is fetched and the same function is
                    // invoked with the fetched input object type again to parse the input fields of 'authors'/'reviews'.
                    if (inputFieldNode.Value.Kind == SyntaxKind.ListValue)
                    {
                        parsedInputFields.Add(
                            fieldName,
                            GQLMultipleCreateArgumentToDictParamsHelper(
                                context,
                                GetInputObjectTypeForAField(fieldName, inputObjectType.Fields),
                                inputFieldNode.Value.Value));
                    }
                    // For the mutation pointMultipleCreateExample (outlined in the method summary),
                    // the following condition will evaluate to true for fields 'publishers'.
                    // Field 'publishers' can again consist of combination of scalar and relationship fields.
                    // So, the input object type for 'publishers' is fetched and the same function is
                    // invoked with the fetched input object type again to parse the input fields of 'publishers'.
                    else if (inputFieldNode.Value.Kind == SyntaxKind.ObjectValue)
                    {
                        parsedInputFields.Add(
                            fieldName,
                            GQLMultipleCreateArgumentToDictParamsHelper(
                                context,
                                GetInputObjectTypeForAField(fieldName, inputObjectType.Fields),
                                inputFieldNode.Value.Value));
                    }
                    // The flow enters this block for all scalar input fields.
                    else
                    {
                        object? fieldValue = ExecutionHelper.ExtractValueFromIValueNode(
                            value: inputFieldNode.Value,
                            argumentSchema: inputObjectType.Fields[fieldName],
                            variables: context.Variables);

                        parsedInputFields.Add(fieldName, fieldValue);
                    }
                }

                return parsedInputFields;
            }
            else
            {
                throw new DataApiBuilderException(
                    message: "Unsupported input type found in the mutation request",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }
        }

        /// <summary>
        /// Extracts the InputObjectType for a given field.
        /// Consider the following multiple create mutation 
        /// mutation multipleCreateExample{  
        ///  createbook(
        ///    item: {
        ///      title: "Harry Potter and the Goblet of Fire", 
        ///      publishers: { name: "Bloomsbury" },  
        ///      authors: [{ name: "J.K Rowling", birthdate: "1965-07-31", royalty_percentage: 100.0 }]}){  
        ///        selection set (not relevant in this function)  
        ///      }
        ///   }  
        /// }
        /// When parsing this mutation request, the flow will reach this function two times.
        /// 1. For the field 'publishers'.
        ///    - The function will get invoked with params
        ///         fieldName: 'publishers',
        ///         fields: All the fields present in CreateBookInput input object
        ///    - The function will return `CreatePublisherInput`
        /// 2. For the field 'authors'.
        ///     - The function will get invoked with params
        ///         fieldName: 'authors',
        ///         fields: All the fields present in CreateBookInput input object
        ///     - The function will return `CreateAuthorInput`
        /// </summary>
        /// <param name="fieldName">Field name for which the input object type is to be extracted.</param>
        /// <param name="fields">Fields present in the input object type.</param>
        /// <returns>The input object type for the given field.</returns>
        /// <exception cref="DataApiBuilderException"></exception>
        private static InputObjectType GetInputObjectTypeForAField(string fieldName, FieldCollection<InputField> fields)
        {
            if (fields.TryGetField(fieldName, out IInputField? field))
            {
                return ExecutionHelper.InputObjectTypeFromIInputField(field);
            }

            throw new ArgumentException($"Field {fieldName} not found in the list of fields provided.");
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
                    dataReaderHandler: queryExecutor.GetResultPropertiesAsync,
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
            if (mutationOperation is EntityActionOperation.Create && _runtimeConfigProvider.GetConfig().IsMultipleCreateOperationEnabled())
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
        ///                         authors: [{ birthdate: "1997-09-03", name: "Red house authors", royal_percentage: 4.6 }]
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
        ///                         authors: [{ birthdate: "1997-09-03", name: "Red house authors", royal_percentage: 4.9 }]
        ///                     },
        ///                     {
        ///                         title: "book #2",
        ///                         reviews: [{ content: "Awesome book." }, { content: "Average book." }],
        ///                         publishers: { name: "Pearson Education" },
        ///                         authors: [{ birthdate: "1990-11-04", name: "Penguin Random House", royal_percentage: 8.2  }]
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
        /// <seealso cref="https://learn.microsoft.com/en-us/dotnet/api/system.transactions.transactionscopeoption#fields" />
        /// <seealso cref="https://learn.microsoft.com/en-us/dotnet/api/system.transactions.transactionscopeasyncflowoption#fields" />
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
