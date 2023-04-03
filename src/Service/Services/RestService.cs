// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
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
        private readonly RuntimeConfigProvider _runtimeConfigProvider;

        public RestService(
            IQueryEngine queryEngine,
            IMutationEngine mutationEngine,
            ISqlMetadataProvider sqlMetadataProvider,
            IHttpContextAccessor httpContextAccessor,
            IAuthorizationService authorizationService,
            RuntimeConfigProvider runtimeConfigProvider
            )
        {
            _queryEngine = queryEngine;
            _mutationEngine = mutationEngine;
            _httpContextAccessor = httpContextAccessor;
            _authorizationService = authorizationService;
            _sqlMetadataProvider = sqlMetadataProvider;
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
            Config.Operation operationType,
            string? primaryKeyRoute)
        {
            RequestValidator.ValidateEntity(entityName, _sqlMetadataProvider.EntityToDatabaseObject.Keys);
            DatabaseObject dbObject = _sqlMetadataProvider.EntityToDatabaseObject[entityName];

            if (dbObject.SourceType is not SourceType.StoredProcedure)
            {
                await AuthorizationCheckForRequirementAsync(resource: entityName, requirement: new EntityRoleOperationPermissionsRequirement());
            }
            else
            {
                await AuthorizationCheckForRequirementAsync(resource: entityName, requirement: new StoredProcedurePermissionsRequirement());
            }

            QueryString? query = GetHttpContext().Request.QueryString;
            string queryString = query is null ? string.Empty : GetHttpContext().Request.QueryString.ToString();

            string requestBody = string.Empty;
            using (StreamReader reader = new(GetHttpContext().Request.Body))
            {
                requestBody = await reader.ReadToEndAsync();
            }

            RequestValidator.ValidateEmptyRequestBodyForFindApi(operationType, requestBody);

            RestRequestContext context;

            // If request has resolved to a stored procedure entity, initialize and validate appropriate request context
            if (dbObject.SourceType is SourceType.StoredProcedure)
            {
                if (!IsHttpMethodAllowedForStoredProcedure(entityName))
                {
                    throw new DataApiBuilderException(
                        message: "This operation is not supported.",
                        statusCode: HttpStatusCode.MethodNotAllowed,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                }

                PopulateStoredProcedureContext(
                    operationType,
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
                    case Config.Operation.Read:
                        context = new FindRequestContext(
                            entityName,
                            dbo: dbObject,
                            isList: string.IsNullOrEmpty(primaryKeyRoute));
                        break;
                    case Config.Operation.Insert:
                        RequestValidator.ValidatePrimaryKeyRouteAndQueryStringInURL(operationType, primaryKeyRoute, queryString);
                        JsonElement insertPayloadRoot = RequestValidator.ValidateAndParseRequestBody(requestBody);
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
                    case Config.Operation.Delete:
                        RequestValidator.ValidatePrimaryKeyRouteAndQueryStringInURL(operationType, primaryKeyRoute);
                        context = new DeleteRequestContext(entityName,
                                                           dbo: dbObject,
                                                           isList: false);
                        break;
                    case Config.Operation.Update:
                    case Config.Operation.UpdateIncremental:
                    case Config.Operation.Upsert:
                    case Config.Operation.UpsertIncremental:
                        RequestValidator.ValidatePrimaryKeyRouteAndQueryStringInURL(operationType, primaryKeyRoute);
                        JsonElement upsertPayloadRoot = RequestValidator.ValidateAndParseRequestBody(requestBody);
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
                case Config.Operation.Read:
                    return await DispatchQuery(context);
                case Config.Operation.Insert:
                case Config.Operation.Delete:
                case Config.Operation.Update:
                case Config.Operation.UpdateIncremental:
                case Config.Operation.Upsert:
                case Config.Operation.UpsertIncremental:
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
        /// Populates the request context when the representative database object is a stored procedure.
        /// Stored procedures support arbitrary keys in the query string, so the read operation behaves differently
        /// than for requests on non-stored procedure entities.
        /// </summary>
        private void PopulateStoredProcedureContext(
            Config.Operation operationType,
            DatabaseObject dbObject,
            string entityName,
            string queryString,
            string? primaryKeyRoute,
            string requestBody,
            out RestRequestContext context)
        {
            switch (operationType)
            {

                case Config.Operation.Read:
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
                case Config.Operation.Insert:
                case Config.Operation.Delete:
                case Config.Operation.Update:
                case Config.Operation.UpdateIncremental:
                case Config.Operation.Upsert:
                case Config.Operation.UpsertIncremental:
                    // Stored procedure call is semantically identical for all methods except Find.
                    // So, we can effectively treat it as Insert operation - throws error if query string is non empty.
                    RequestValidator.ValidatePrimaryKeyRouteAndQueryStringInURL(Config.Operation.Insert, queryString);
                    JsonElement requestPayloadRoot = RequestValidator.ValidateAndParseRequestBody(requestBody);
                    context = new StoredProcedureRequestContext(
                        entityName,
                        dbo: dbObject,
                        requestPayloadRoot,
                        operationType);
                    break;
                default:
                    throw new DataApiBuilderException(
                        message: "This operation is not supported.",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }

            // Throws bad request if primaryKeyRoute set
            RequestValidator.ValidateStoredProcedureRequest(primaryKeyRoute);

            // At this point, either query string or request body is populated with params, so resolve which will be passed
            ((StoredProcedureRequestContext)context).PopulateResolvedParameters();

            // Validate the request parameters
            RequestValidator.ValidateStoredProcedureRequestContext((StoredProcedureRequestContext)context, _sqlMetadataProvider);
        }

        /// <summary>
        /// Returns whether the stored procedure backed entity allows the
        /// request's HTTP method. e.g. when an entity is only configured for "GET"
        /// and the request method is "POST" this method will return false.
        /// </summary>
        /// <param name="entityName">Name of the entity.</param>
        /// <returns>True if the operation is allowed. False, otherwise.</returns>
        private bool IsHttpMethodAllowedForStoredProcedure(string entityName)
        {
            if (TryGetStoredProcedureRESTVerbs(entityName, out List<RestMethod>? httpVerbs))
            {
                HttpContext? httpContext = _httpContextAccessor.HttpContext;
                if (httpContext is not null
                    && Enum.TryParse(httpContext.Request.Method, ignoreCase: true, out RestMethod method)
                    && httpVerbs.Contains(method))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Gets the list of HTTP methods defined for entities representing stored procedures.
        /// When no explicit REST method configuration is present for a stored procedure entity,
        /// the default method "POST" is populated in httpVerbs.
        /// </summary>
        /// <param name="entityName">Name of the entity.</param>
        /// <param name="httpVerbs">Out Param: List of httpverbs configured for stored procedure backed entity.</param>
        /// <returns>True, with a list of HTTP verbs. False, when entity is not found in config
        /// or entity is not a stored procedure, and httpVerbs will be null.</returns>
        private bool TryGetStoredProcedureRESTVerbs(string entityName, [NotNullWhen(true)] out List<RestMethod>? httpVerbs)
        {
            if (_runtimeConfigProvider.TryGetRuntimeConfiguration(out RuntimeConfig? runtimeConfig))
            {
                if (runtimeConfig.Entities.TryGetValue(key: entityName, out Entity? entity) && entity is not null)
                {
                    RestMethod[]? methods = entity.GetRestMethodsConfiguredForStoredProcedure();
                    httpVerbs = methods is not null ? new List<RestMethod>(methods) : new();
                    return true;
                }
            }

            httpVerbs = null;
            return false;
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
            // that start with '/'. We can be assured here that _runtimeConfigProvider.RestPath[0]='/'.
            string restPath = _runtimeConfigProvider.RestPath.Substring(1);
            if (!route.StartsWith(restPath))
            {
                throw new DataApiBuilderException(
                    message: $"Invalid Path for route: {route}.",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }

            // entity's path comes after the restPath, so get substring starting from
            // the end of restPath. If restPath is not empty we trim the '/' following the path.
            string routeAfterPath = route.Substring(restPath.Length).TrimStart('/');
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

        /// <summary>
        /// Gets the httpContext for the current request.
        /// </summary>
        /// <returns>Request's httpContext.</returns>
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
        public static Config.Operation HttpVerbToOperations(string httpVerbName)
        {
            switch (httpVerbName)
            {
                case "POST":
                    return Config.Operation.Create;
                case "PUT":
                case "PATCH":
                    // Please refer to the use of this method, which is to look out for policy based on crud operation type.
                    // Since create doesn't have filter predicates, PUT/PATCH would resolve to update operation.
                    return Config.Operation.Update;
                case "DELETE":
                    return Config.Operation.Delete;
                case "GET":
                    return Config.Operation.Read;
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
