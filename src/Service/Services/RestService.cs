using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Authorization;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Models;
using Azure.DataApiBuilder.Service.Parsers;
using Azure.DataApiBuilder.Service.Resolvers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Azure.DataApiBuilder.Service.Services
{
    /// <summary>
    /// Service providing REST Api executions.
    /// </summary>
    public class RestService
    {
        private readonly IQueryEngine _queryEngine;
        private readonly IMutationEngine _mutationEngine;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IAuthorizationService _authorizationService;
        private readonly ISqlMetadataProvider _sqlMetadataProvider;
        private readonly IAuthorizationResolver _authorizationResolver;
        private readonly RuntimeConfigProvider _runtimeConfigProvider;

        public RestService(
            IQueryEngine queryEngine,
            IMutationEngine mutationEngine,
            ISqlMetadataProvider sqlMetadataProvider,
            IHttpContextAccessor httpContextAccessor,
            IAuthorizationService authorizationService,
            IAuthorizationResolver authorizationResolver,
            RuntimeConfigProvider runtimeConfigProvider
            )
        {
            _queryEngine = queryEngine;
            _mutationEngine = mutationEngine;
            _httpContextAccessor = httpContextAccessor;
            _authorizationService = authorizationService;
            _sqlMetadataProvider = sqlMetadataProvider;
            _authorizationResolver = authorizationResolver;
            _runtimeConfigProvider = runtimeConfigProvider;
        }

        /// <summary>
        /// Invokes the request parser to identify major components of the RestRequestContext
        /// and executes the given operation.
        /// </summary>
        /// <param name="entityName">The entity name.</param>
        /// <param name="operationType">The kind of operation to execute.</param>
        /// <param name="primaryKeyRoute">The primary key route. e.g. customerName/Xyz/saleOrderId/123</param>
        public async Task<IActionResult?> ExecuteAsync(
            string entityName,
            Operation operationType,
            string? primaryKeyRoute)
        {
            RequestValidator.ValidateEntity(entityName, _sqlMetadataProvider.EntityToDatabaseObject.Keys);
            DatabaseObject dbObject = _sqlMetadataProvider.EntityToDatabaseObject[entityName];

            await AuthorizationCheckForRequirementAsync(resource: entityName, requirement: new EntityRoleOperationPermissionsRequirement());

            QueryString? query = GetHttpContext().Request.QueryString;
            string queryString = query is null ? string.Empty : GetHttpContext().Request.QueryString.ToString();

            string requestBody = string.Empty;
            using (StreamReader reader = new(GetHttpContext().Request.Body))
            {
                requestBody = await reader.ReadToEndAsync();
            }

            RestRequestContext context;

            // If request has resolved to a stored procedure entity, initialize and validate appropriate request context
            if (dbObject.SourceType is SourceType.StoredProcedure)
            {
                PopulateStoredProcedureContext(operationType,
                    dbObject,
                    entityName,
                    queryString,
                    primaryKeyRoute,
                    requestBody,
                    out context);
            }
            else
            {
                switch (operationType)
                {
                    case Operation.Read:
                        context = new FindRequestContext(
                            entityName,
                            dbo: dbObject,
                            isList: string.IsNullOrEmpty(primaryKeyRoute));
                        break;
                    case Operation.Insert:
                        JsonElement insertPayloadRoot = RequestValidator.ValidateInsertRequest(queryString, requestBody);
                        context = new InsertRequestContext(
                            entityName,
                            dbo: dbObject,
                            insertPayloadRoot,
                            operationType);
                        if (context.DatabaseObject.SourceType is SourceType.Table)
                        {
                            RequestValidator.ValidateInsertRequestContext(
                            (InsertRequestContext)context,
                            _sqlMetadataProvider);
                        }

                        break;
                    case Operation.Delete:
                        RequestValidator.ValidateDeleteRequest(primaryKeyRoute);
                        context = new DeleteRequestContext(entityName,
                                                           dbo: dbObject,
                                                           isList: false);
                        break;
                    case Operation.Update:
                    case Operation.UpdateIncremental:
                    case Operation.Upsert:
                    case Operation.UpsertIncremental:
                        JsonElement upsertPayloadRoot = RequestValidator.ValidateUpdateOrUpsertRequest(primaryKeyRoute, requestBody);
                        context = new UpsertRequestContext(
                            entityName,
                            dbo: dbObject,
                            upsertPayloadRoot,
                            operationType);
                        if (context.DatabaseObject.SourceType is SourceType.Table)
                        {
                            RequestValidator.
                                ValidateUpsertRequestContext((UpsertRequestContext)context, _sqlMetadataProvider);
                        }

                        break;
                    default:
                        throw new DataApiBuilderException(
                            message: "This operation is not supported.",
                            statusCode: HttpStatusCode.BadRequest,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                }

                if (!string.IsNullOrEmpty(primaryKeyRoute))
                {
                    // After parsing primary key, the Context will be populated with the
                    // correct PrimaryKeyValuePairs.
                    RequestParser.ParsePrimaryKey(primaryKeyRoute, context);
                    RequestValidator.ValidatePrimaryKey(context, _sqlMetadataProvider);
                }

                if (!string.IsNullOrWhiteSpace(queryString))
                {
                    context.ParsedQueryString = HttpUtility.ParseQueryString(queryString);
                    RequestParser.ParseQueryString(context, _sqlMetadataProvider);
                }
            }

            string role = GetHttpContext().Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER];
            Operation operation = HttpVerbToOperations(GetHttpContext().Request.Method);
            string dbPolicy = _authorizationResolver.TryProcessDBPolicy(entityName, role, operation, GetHttpContext());
            if (!string.IsNullOrEmpty(dbPolicy))
            {
                // Since dbPolicy is nothing but filters to be added by virtue of database policy, we prefix it with
                // ?$filter= so that it conforms with the format followed by other filter predicates.
                // This helps the ODataVisitor helpers to parse the policy text properly.
                dbPolicy = "?$filter=" + dbPolicy;

                // Parse and save the values that are needed to later generate queries in the given RestRequestContext.
                // DbPolicyClause is an Abstract Syntax Tree representing the parsed policy text.
                context.DbPolicyClause = _sqlMetadataProvider.GetODataParser().GetFilterClause(dbPolicy, $"{context.EntityName}.{context.DatabaseObject.FullName}");
            }

            // At this point for DELETE, the primary key should be populated in the Request Context.
            RequestValidator.ValidateRequestContext(context, _sqlMetadataProvider);

            // The final authorization check on columns occurs after the request is fully parsed and validated.
            // Stored procedures do not yet have semantics defined for column-level permissions
            if (dbObject.SourceType is not SourceType.StoredProcedure)
            {
                await AuthorizationCheckForRequirementAsync(resource: context, requirement: new ColumnsPermissionsRequirement());
            }

            switch (operationType)
            {
                case Operation.Read:
                    return await DispatchQuery(context);
                case Operation.Insert:
                case Operation.Delete:
                case Operation.Update:
                case Operation.UpdateIncremental:
                case Operation.Upsert:
                case Operation.UpsertIncremental:
                    return await DispatchMutation(context);
                default:
                    throw new NotSupportedException("This operation is not yet supported.");
            };
        }

        /// <summary>
        /// Dispatch execution of a request context to the query engine
        /// The two overloads to ExecuteAsync take FindRequestContext and StoredProcedureRequestContext
        /// </summary>
        private Task<IActionResult> DispatchQuery(RestRequestContext context)
        {
            return context switch
            {
                FindRequestContext => _queryEngine.ExecuteAsync((FindRequestContext)context),
                StoredProcedureRequestContext => _queryEngine.ExecuteAsync((StoredProcedureRequestContext)context),
                _ => throw new NotSupportedException("This operation is not yet supported."),
            };
        }

        /// <summary>
        /// Dispatch execution of a request context to the mutation engine
        /// The two overloads to ExecuteAsync take StoredProcedureRequestContext and RestRequestContext
        /// </summary>
        private Task<IActionResult?> DispatchMutation(RestRequestContext context)
        {
            return context switch
            {
                StoredProcedureRequestContext => _mutationEngine.ExecuteAsync((StoredProcedureRequestContext)context),
                _ => _mutationEngine.ExecuteAsync(context)
            };
        }

        /// <summary>
        /// Helper method to populate the context in case the database object for this request is a stored procedure
        /// </summary>
        private void PopulateStoredProcedureContext(Operation operationType,
            DatabaseObject dbObject,
            string entityName,
            string queryString,
            string? primaryKeyRoute,
            string requestBody,
            out RestRequestContext context)
        {
            switch (operationType)
            {

                case Operation.Read:
                    // Parameters passed in query string, request body is ignored for find requests
                    context = new StoredProcedureRequestContext(
                        entityName,
                        dbo: dbObject,
                        requestPayloadRoot: null,
                        operationType);

                    // Don't want to use RequestParser.ParseQueryString here since for all non-sp requests,
                    // arbitrary keys shouldn't be allowed/recognized in the querystring.
                    // So, for the time being, filter, select, etc. fields aren't populated for sp requests
                    // So, $filter will be treated as any other parameter (inevitably will raise a Bad Request)
                    if (!string.IsNullOrWhiteSpace(queryString))
                    {
                        context.ParsedQueryString = HttpUtility.ParseQueryString(queryString);
                    }

                    break;
                case Operation.Insert:
                case Operation.Delete:
                case Operation.Update:
                case Operation.UpdateIncremental:
                case Operation.Upsert:
                case Operation.UpsertIncremental:
                    // Stored procedure call is semantically identical for all methods except Find, so we can
                    // effectively reuse the ValidateInsertRequest - throws error if query string is nonempty
                    // and parses the body into json
                    JsonElement requestPayloadRoot = RequestValidator.ValidateUpdateOrUpsertRequest(primaryKeyRoute, requestBody);
                    context = new StoredProcedureRequestContext(
                        entityName,
                        dbo: dbObject,
                        requestPayloadRoot,
                        operationType);
                    break;
                default:
                    throw new DataApiBuilderException(message: "This operation is not supported.",
                                                   statusCode: HttpStatusCode.BadRequest,
                                                   subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }

            // Throws bad request if primaryKeyRoute set
            RequestValidator.ValidateStoredProcedureRequest(primaryKeyRoute);

            // At this point, either query string or request body is populated with params, so resolve which will be passed
            ((StoredProcedureRequestContext)context).PopulateResolvedParameters();

            // Validate the request parameters
            RequestValidator.ValidateStoredProcedureRequestContext(
                (StoredProcedureRequestContext)context, _sqlMetadataProvider);
        }

        /// <summary>
        /// Tries to get the Entity name and primary key route
        /// from the provided string that starts with the REST
        /// path. If the provided string does not start with
        /// the given REST path, we throw an exception. We then
        /// return the entity name via a lookup using the string
        /// up until the next '/' if one exists, and the primary
        /// key as the substring following the '/'. For example
        /// a request route shoud be of the form
        /// {RESTPath}/{EntityPath}/{PKColumn}/{PkValue}/{PKColumn}/{PKValue}...
        /// </summary>
        /// <param name="route">The request route, containing REST path + entity path
        /// (and optionally primary key).</param>
        /// <returns>entity name associated with entity path
        /// and primary key route.</returns>
        /// <exception cref="DataApiBuilderException"></exception>
        public (string, string) GetEntityNameAndPrimaryKeyRouteFromRoute(string route)
        {
            // route will ignore leading '/' so we trim here to allow for restPath
            // that start with '/'
            string restPath = _runtimeConfigProvider.RestPath.TrimStart('/');
            if (!route.StartsWith(restPath))
            {
                throw new DataApiBuilderException(
                    message: $"Invalid Path for route: {route}.",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }

            // entity's path comes after the restPath, so get substring starting from
            // the end of restPath. If restPath is not empty we trim the '/' following the path.
            string routeAfterPath = string.IsNullOrEmpty(restPath) ? route : route.Substring(restPath.Length).TrimStart('/');
            // Split routeAfterPath on the first occurrence of '/', if we get back 2 elements
            // this means we have a non empty primary key route which we save. Otherwise, save
            // primary key route as empty string. Entity Path will always be the element at index 0.
            // ie: {EntityPath}/{PKColumn}/{PkValue}/{PKColumn}/{PKValue}...
            // splits into [{EntityPath}] when there is an empty primary key route and into
            // [{EntityPath}, {Primarykeyroute}] when there is a non empty primary key route.
            int maxNumberOfElementsFromSplit = 2;
            string[] entityPathAndPKRoute = routeAfterPath.Split(new[] { '/' }, maxNumberOfElementsFromSplit);
            string entityPath = entityPathAndPKRoute[0];
            string primaryKeyRoute = entityPathAndPKRoute.Length == maxNumberOfElementsFromSplit ? entityPathAndPKRoute[1] : string.Empty;

            if (!_sqlMetadataProvider.TryGetEntityNameFromPath(entityPath, out string? entityName))
            {
                throw new DataApiBuilderException(
                    message: $"Invalid Entity path: {entityPath}.",
                    statusCode: HttpStatusCode.NotFound,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.EntityNotFound);
            }

            return (entityName, primaryKeyRoute);
        }

        private HttpContext GetHttpContext()
        {
            return _httpContextAccessor.HttpContext!;
        }

        /// <summary>
        /// Performs authorization check for REST with a single requirement.
        /// Called when the relevant metadata has been parsed from the request.
        /// </summary>
        /// <param name="resource">Request metadata object (RestRequestContext, DatabaseObject, or null)</param>
        /// <param name="requirement">The authorization check to perform.</param>
        /// <returns>No return value. If this method succeeds, the request is authorized for the requirement.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the resource is null
        /// for requirements which need one.</exception>
        /// <exception cref="DataApiBuilderException">Thrown when authorization fails.
        /// Results in server returning 403 Unauthorized.</exception>
        public async Task AuthorizationCheckForRequirementAsync(object? resource, IAuthorizationRequirement requirement)
        {
            if (requirement is not RoleContextPermissionsRequirement && resource is null)
            {
                throw new ArgumentNullException(paramName: "resource", message: $"Resource can't be null for the requirement: {requirement.GetType}");
            }

            AuthorizationResult authorizationResult = await _authorizationService.AuthorizeAsync(
                user: GetHttpContext().User,
                resource: resource,
                requirements: new[] { requirement });

            if (!authorizationResult.Succeeded)
            {
                // Authorization failed so the request terminates.
                throw new DataApiBuilderException(
                    message: "Authorization Failure: Access Not Allowed.",
                    statusCode: HttpStatusCode.Forbidden,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed);
            }
        }

        /// <summary>
        /// Converts httpverb type of a RestRequestContext object to the
        /// matching CRUD operation, to facilitate authorization checks.
        /// </summary>
        /// <param name="httpVerb"></param>
        /// <returns>The CRUD operation for the given httpverb.</returns>
        public static Operation HttpVerbToOperations(string httpVerbName)
        {
            switch (httpVerbName)
            {
                case "POST":
                    return Operation.Create;
                case "PUT":
                case "PATCH":
                    // Please refer to the use of this method, which is to look out for policy based on crud operation type.
                    // Since create doesn't have filter predicates, PUT/PATCH would resolve to update operation.
                    return Operation.Update;
                case "DELETE":
                    return Operation.Delete;
                case "GET":
                    return Operation.Read;
                default:
                    throw new DataApiBuilderException(
                        message: "Unsupported operation type.",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest
                    );
            }
        }
    }
}
