// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.Json;
using System.Web;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Parsers;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Azure.DataApiBuilder.Core.Services
{
    /// <summary>
    /// Service providing REST Api executions.
    /// </summary>
    public class RestService
    {
        private readonly IQueryEngineFactory _queryEngineFactory;
        private readonly IMutationEngineFactory _mutationEngineFactory;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IAuthorizationService _authorizationService;
        private readonly IMetadataProviderFactory _sqlMetadataProviderFactory;
        private readonly RuntimeConfigProvider _runtimeConfigProvider;
        private readonly RequestValidator _requestValidator;

        public RestService(
            IQueryEngineFactory queryEngineFactory,
            IMutationEngineFactory mutationEngineFactory,
            IMetadataProviderFactory sqlMetadataProviderFactory,
            IHttpContextAccessor httpContextAccessor,
            IAuthorizationService authorizationService,
            RuntimeConfigProvider runtimeConfigProvider,
            RequestValidator requestValidator
            )
        {
            _queryEngineFactory = queryEngineFactory;
            _mutationEngineFactory = mutationEngineFactory;
            _httpContextAccessor = httpContextAccessor;
            _authorizationService = authorizationService;
            _sqlMetadataProviderFactory = sqlMetadataProviderFactory;
            _runtimeConfigProvider = runtimeConfigProvider;
            _requestValidator = requestValidator;
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
            EntityActionOperation operationType,
            string? primaryKeyRoute)
        {
            _requestValidator.ValidateEntity(entityName);
            string dataSourceName = _runtimeConfigProvider.GetConfig().GetDataSourceNameFromEntityName(entityName);
            ISqlMetadataProvider sqlMetadataProvider = _sqlMetadataProviderFactory.GetMetadataProvider(dataSourceName);
            DatabaseObject dbObject = sqlMetadataProvider.EntityToDatabaseObject[entityName];

            if (dbObject.SourceType is not EntitySourceType.StoredProcedure)
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

            RestRequestContext context;

            // If request has resolved to a stored procedure entity, initialize and validate appropriate request context
            if (dbObject.SourceType is EntitySourceType.StoredProcedure)
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
                    case EntityActionOperation.Read:
                        context = new FindRequestContext(
                            entityName,
                            dbo: dbObject,
                            isList: string.IsNullOrEmpty(primaryKeyRoute));
                        break;
                    case EntityActionOperation.Insert:
                        RequestValidator.ValidatePrimaryKeyRouteAndQueryStringInURL(operationType, primaryKeyRoute, queryString);
                        JsonElement insertPayloadRoot = RequestValidator.ValidateAndParseRequestBody(requestBody);
                        context = new InsertRequestContext(
                            entityName,
                            dbo: dbObject,
                            insertPayloadRoot,
                            operationType);
                        if (context.DatabaseObject.SourceType is EntitySourceType.Table)
                        {
                            _requestValidator.ValidateInsertRequestContext((InsertRequestContext)context);
                        }

                        break;
                    case EntityActionOperation.Delete:
                        RequestValidator.ValidatePrimaryKeyRouteAndQueryStringInURL(operationType, primaryKeyRoute);
                        context = new DeleteRequestContext(entityName,
                                                           dbo: dbObject,
                                                           isList: false);
                        break;
                    case EntityActionOperation.Update:
                    case EntityActionOperation.UpdateIncremental:
                    case EntityActionOperation.Upsert:
                    case EntityActionOperation.UpsertIncremental:
                        RequestValidator.ValidatePrimaryKeyRouteAndQueryStringInURL(operationType, primaryKeyRoute);
                        JsonElement upsertPayloadRoot = RequestValidator.ValidateAndParseRequestBody(requestBody);
                        context = new UpsertRequestContext(
                            entityName,
                            dbo: dbObject,
                            upsertPayloadRoot,
                            operationType);
                        if (context.DatabaseObject.SourceType is EntitySourceType.Table)
                        {
                            _requestValidator.ValidateUpsertRequestContext((UpsertRequestContext)context);
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
                    _requestValidator.ValidatePrimaryKey(context);
                }

                if (!string.IsNullOrWhiteSpace(queryString))
                {
                    context.ParsedQueryString = HttpUtility.ParseQueryString(queryString);
                    RequestParser.ParseQueryString(context, sqlMetadataProvider);
                }
            }

            // At this point for DELETE, the primary key should be populated in the Request Context.
            _requestValidator.ValidateRequestContext(context);

            // The final authorization check on columns occurs after the request is fully parsed and validated.
            // Stored procedures do not yet have semantics defined for column-level permissions
            if (dbObject.SourceType is not EntitySourceType.StoredProcedure)
            {
                await AuthorizationCheckForRequirementAsync(resource: context, requirement: new ColumnsPermissionsRequirement());
            }

            switch (operationType)
            {
                case EntityActionOperation.Read:
                    return await DispatchQuery(context, sqlMetadataProvider.GetDatabaseType());
                case EntityActionOperation.Insert:
                case EntityActionOperation.Delete:
                case EntityActionOperation.Update:
                case EntityActionOperation.UpdateIncremental:
                case EntityActionOperation.Upsert:
                case EntityActionOperation.UpsertIncremental:
                    return await DispatchMutation(context, sqlMetadataProvider.GetDatabaseType());
                default:
                    throw new NotSupportedException("This operation is not yet supported.");
            }
        }

        /// <summary>
        /// Dispatch execution of a request context to the query engine
        /// The two overloads to ExecuteAsync take FindRequestContext and StoredProcedureRequestContext
        /// </summary>
        private async Task<IActionResult> DispatchQuery(RestRequestContext context, DatabaseType databaseType)
        {
            IQueryEngine queryEngine = _queryEngineFactory.GetQueryEngine(databaseType);

            string dataSourceName = _runtimeConfigProvider.GetConfig().GetDataSourceNameFromEntityName(context.EntityName);

            if (context is FindRequestContext findRequestContext)
            {
                using JsonDocument? restApiResponse = await queryEngine.ExecuteAsync(findRequestContext);
                return restApiResponse is null ? SqlResponseHelpers.FormatFindResult(JsonDocument.Parse("[]").RootElement.Clone(), findRequestContext, _sqlMetadataProviderFactory.GetMetadataProvider(dataSourceName), _runtimeConfigProvider.GetConfig(), GetHttpContext())
                                               : SqlResponseHelpers.FormatFindResult(restApiResponse.RootElement.Clone(), findRequestContext, _sqlMetadataProviderFactory.GetMetadataProvider(dataSourceName), _runtimeConfigProvider.GetConfig(), GetHttpContext());
            }
            else if (context is StoredProcedureRequestContext storedProcedureRequestContext)
            {
                return await queryEngine.ExecuteAsync(storedProcedureRequestContext, dataSourceName);
            }
            else
            {
                throw new NotSupportedException("This operation is not yet supported.");
            }
        }

        /// <summary>
        /// Dispatch execution of a request context to the mutation engine
        /// The two overloads to ExecuteAsync take StoredProcedureRequestContext and RestRequestContext
        /// </summary>
        private Task<IActionResult?> DispatchMutation(RestRequestContext context, DatabaseType databaseType)
        {
            IMutationEngine mutationEngine = _mutationEngineFactory.GetMutationEngine(databaseType);
            string dataSourceName = _runtimeConfigProvider.GetConfig().GetDataSourceNameFromEntityName(context.EntityName);
            return context switch
            {
                StoredProcedureRequestContext => mutationEngine.ExecuteAsync((StoredProcedureRequestContext)context, dataSourceName),
                _ => mutationEngine.ExecuteAsync(context)
            };
        }

        /// <summary>
        /// Populates the request context when the representative database object is a stored procedure.
        /// Stored procedures support arbitrary keys in the query string, so the read operation behaves differently
        /// than for requests on non-stored procedure entities.
        /// </summary>
        private void PopulateStoredProcedureContext(
            EntityActionOperation operationType,
            DatabaseObject dbObject,
            string entityName,
            string queryString,
            string? primaryKeyRoute,
            string requestBody,
            out RestRequestContext context)
        {
            switch (operationType)
            {

                case EntityActionOperation.Read:
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
                case EntityActionOperation.Insert:
                case EntityActionOperation.Delete:
                case EntityActionOperation.Update:
                case EntityActionOperation.UpdateIncremental:
                case EntityActionOperation.Upsert:
                case EntityActionOperation.UpsertIncremental:
                    // Stored procedure call is semantically identical for all methods except Find.
                    // So, we can effectively treat it as Insert operation - throws error if query string is non-empty.
                    RequestValidator.ValidatePrimaryKeyRouteAndQueryStringInURL(EntityActionOperation.Insert, queryString);
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
            _requestValidator.ValidateStoredProcedureRequestContext((StoredProcedureRequestContext)context);
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
            if (TryGetStoredProcedureRESTVerbs(entityName, out List<SupportedHttpVerb>? httpVerbs))
            {
                HttpContext? httpContext = _httpContextAccessor.HttpContext;
                if (httpContext is not null
                    && Enum.TryParse(httpContext.Request.Method, ignoreCase: true, out SupportedHttpVerb method)
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
        /// <param name="httpVerbs">Out Param: List of http verbs configured for stored procedure backed entity.</param>
        /// <returns>True, with a list of HTTP verbs. False, when entity is not found in config
        /// or entity is not a stored procedure, and httpVerbs will be null.</returns>
        private bool TryGetStoredProcedureRESTVerbs(string entityName, [NotNullWhen(true)] out List<SupportedHttpVerb>? httpVerbs)
        {
            if (_runtimeConfigProvider.TryGetConfig(out RuntimeConfig? runtimeConfig))
            {
                if (runtimeConfig.Entities.TryGetValue(entityName, out Entity? entity))
                {
                    SupportedHttpVerb[] methods;
                    if (entity.Rest.Methods is not null)
                    {
                        methods = entity.Rest.Methods;
                    }
                    else
                    {
                        methods = (entity.Rest.Enabled) ? new SupportedHttpVerb[] { SupportedHttpVerb.Post } : Array.Empty<SupportedHttpVerb>();
                    }

                    httpVerbs = new(methods);
                    return true;
                }
            }

            httpVerbs = null;
            return false;
        }

        /// <summary>
        /// Input route: {pathBase}/{entity}/{pkName}/{pkValue}
        /// Validates that the {pathBase} value matches the configured REST path.
        /// Returns {entity}/{pkName}/{pkValue} after stripping {pathBase}
        /// and the preceding slash /.
        /// </summary>
        /// <param name="route">{pathBase}/{entity}/{pkName}/{pkValue} with no starting '/'.</param>
        /// <returns>Route without pathBase and without a forward slash.</returns>
        /// <exception cref="DataApiBuilderException">Raised when the routes path base
        /// does not match the configured REST path or the global REST endpoint is disabled.</exception>
        public string GetRouteAfterPathBase(string route)
        {
            string configuredRestPathBase = _runtimeConfigProvider.GetConfig().RestPath;

            // Strip the leading '/' from the REST path provided in the runtime configuration
            // because the input argument 'route' has no starting '/'.
            // The RuntimeConfigProvider enforces the expectation that the configured REST path starts with a
            // forward slash '/'.
            configuredRestPathBase = configuredRestPathBase.Substring(1);

            if (!route.StartsWith(configuredRestPathBase))
            {
                throw new DataApiBuilderException(
                    message: $"Invalid Path for route: {route}.",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }

            // Drop {pathBase}/ from {pathBase}/{entityName}/{pkName}/{pkValue}
            // resulting in: {entityName}/{pkName}/{pkValue}
            return route.Substring(configuredRestPathBase.Length).TrimStart('/');
        }

        /// <summary>
        /// When configuration exists and the REST endpoint is enabled,
        /// return the configured REST endpoint path.
        /// </summary>
        /// <param name="configuredRestRoute">The configured REST route path</param>
        /// <returns>True when configuredRestRoute is defined, otherwise false.</returns>
        public bool TryGetRestRouteFromConfig([NotNullWhen(true)] out string? configuredRestRoute)
        {
            if (_runtimeConfigProvider.TryGetConfig(out RuntimeConfig? config) &&
                config.IsRestEnabled)
            {
                configuredRestRoute = config.RestPath;
                return true;
            }

            configuredRestRoute = null;
            return false;
        }

        /// <summary>
        /// Tries to get the Entity name and primary key route from the provided string
        /// returns the entity name via a lookup using the string which includes
        /// characters up until the first '/', and then resolves the primary key
        /// as the substring following the '/'.
        /// For example, a request route should be of the form
        /// {EntityPath}/{PKColumn}/{PkValue}/{PKColumn}/{PKValue}...
        /// </summary>
        /// <param name="routeAfterPathBase">The request route (no '/' prefix) containing the entity path
        /// (and optionally primary key).</param>
        /// <returns>entity name associated with entity path and primary key route.</returns>
        /// <exception cref="DataApiBuilderException"></exception>
        public (string, string) GetEntityNameAndPrimaryKeyRouteFromRoute(string routeAfterPathBase)
        {

            RuntimeConfig runtimeConfig = _runtimeConfigProvider.GetConfig();

            // Split routeAfterPath on the first occurrence of '/', if we get back 2 elements
            // this means we have a non-empty primary key route which we save. Otherwise, save
            // primary key route as empty string. Entity Path will always be the element at index 0.
            // ie: {EntityPath}/{PKColumn}/{PkValue}/{PKColumn}/{PKValue}...
            // splits into [{EntityPath}] when there is an empty primary key route and into
            // [{EntityPath}, {Primarykeyroute}] when there is a non-empty primary key route.
            int maxNumberOfElementsFromSplit = 2;
            string[] entityPathAndPKRoute = routeAfterPathBase.Split(new[] { '/' }, maxNumberOfElementsFromSplit);
            string entityPath = entityPathAndPKRoute[0];
            string primaryKeyRoute = entityPathAndPKRoute.Length == maxNumberOfElementsFromSplit ? entityPathAndPKRoute[1] : string.Empty;

            if (!runtimeConfig.TryGetEntityNameFromPath(entityPath, out string? entityName))
            {
                throw new DataApiBuilderException(
                    message: $"Invalid Entity path: {entityPath}.",
                    statusCode: HttpStatusCode.NotFound,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.EntityNotFound);
            }

            return (entityName!, primaryKeyRoute);
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
                throw new ArgumentNullException(paramName: "resource", message: $"Resource can't be null for the requirement: {requirement.GetType()}");
            }

            AuthorizationResult authorizationResult = await _authorizationService.AuthorizeAsync(
                user: GetHttpContext().User,
                resource: resource,
                requirements: new[] { requirement });

            if (!authorizationResult.Succeeded)
            {
                // Authorization failed so the request terminates.
                throw new DataApiBuilderException(
                    message: DataApiBuilderException.AUTHORIZATION_FAILURE,
                    statusCode: HttpStatusCode.Forbidden,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed);
            }
        }

        /// <summary>
        /// Converts http verb type of RestRequestContext object to the
        /// matching CRUD operation, to facilitate authorization checks.
        /// </summary>
        /// <returns>The CRUD operation for the given http verb.</returns>
        public static EntityActionOperation HttpVerbToOperations(string httpVerbName)
        {
            switch (httpVerbName)
            {
                case "POST":
                    return EntityActionOperation.Create;
                case "PUT":
                case "PATCH":
                    // Please refer to the use of this method, which is to look out for policy based on crud operation type.
                    // Since create doesn't have filter predicates, PUT/PATCH would resolve to update operation.
                    return EntityActionOperation.Update;
                case "DELETE":
                    return EntityActionOperation.Delete;
                case "GET":
                    return EntityActionOperation.Read;
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
