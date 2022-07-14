using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using Azure.DataGateway.Auth;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Authorization;
using Azure.DataGateway.Service.Configurations;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Parsers;
using Azure.DataGateway.Service.Resolvers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace Azure.DataGateway.Service.Services
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

            await AuthorizationCheckForRequirementAsync(resource: entityName, requirement: new EntityRoleActionPermissionsRequirement());

            QueryString? query = GetHttpContext().Request.QueryString;
            string queryString = query is null ? string.Empty : GetHttpContext().Request.QueryString.ToString();

            string requestBody = string.Empty;
            using (StreamReader reader = new(GetHttpContext().Request.Body))
            {
                requestBody = await reader.ReadToEndAsync();
            }

            RestRequestContext context;
            switch (operationType)
            {
                case Operation.Find:
                    context = new FindRequestContext(entityName,
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
                    RequestValidator.ValidateInsertRequestContext(
                        (InsertRequestContext)context,
                        _sqlMetadataProvider);
                    break;
                case Operation.Delete:
                    context = new DeleteRequestContext(entityName,
                                                       dbo: dbObject,
                                                       isList: false);
                    RequestValidator.ValidateDeleteRequest(primaryKeyRoute);
                    break;
                case Operation.Update:
                case Operation.UpdateIncremental:
                case Operation.Upsert:
                case Operation.UpsertIncremental:
                    JsonElement upsertPayloadRoot = RequestValidator.ValidateUpdateOrUpsertRequest(primaryKeyRoute, requestBody);
                    context = new UpsertRequestContext(entityName,
                                                       dbo: dbObject,
                                                       upsertPayloadRoot,
                                                       operationType);
                    RequestValidator.ValidateUpsertRequestContext((UpsertRequestContext)context, _sqlMetadataProvider);
                    break;
                default:
                    throw new DataGatewayException(message: "This operation is not supported.",
                                                   statusCode: HttpStatusCode.BadRequest,
                                                   subStatusCode: DataGatewayException.SubStatusCodes.BadRequest);
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

            string role = GetHttpContext().Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER];
            string action = HttpVerbToActions(GetHttpContext().Request.Method);
            string dbPolicy = _authorizationResolver.TryProcessDBPolicy(entityName, role, action, GetHttpContext());
            if (!string.IsNullOrEmpty(dbPolicy))
            {
                // Since dbPolicy is nothing but filters to be added by virtue of database policy, we prefix it with
                // ?$filter= so that it conforms with the format followed by other filter predicates.
                // This helps the ODataVisitor helpers to parse the policy text properly.
                dbPolicy = "?$filter=" + dbPolicy;

                // Parse and save the values that are needed to later generate queries in the given RestRequestContext.
                // FilterClauseInDbPolicy is an Abstract Syntax Tree representing the parsed policy text.
                context.DbPolicyClause = _sqlMetadataProvider.GetODataFilterParser().GetFilterClause(dbPolicy, $"{context.EntityName}.{context.DatabaseObject.FullName}");
            }

            // At this point for DELETE, the primary key should be populated in the Request Context.
            RequestValidator.ValidateRequestContext(context, _sqlMetadataProvider);

            // The final authorization check on columns occurs after the request is fully parsed and validated.
            await AuthorizationCheckForRequirementAsync(resource: context, requirement: new ColumnsPermissionsRequirement());

            switch (operationType)
            {
                case Operation.Find:
                    return await _queryEngine.ExecuteAsync(context);
                case Operation.Insert:
                case Operation.Delete:
                case Operation.Update:
                case Operation.UpdateIncremental:
                case Operation.Upsert:
                case Operation.UpsertIncremental:
                    return await _mutationEngine.ExecuteAsync(context);
                default:
                    throw new NotSupportedException("This operation is not yet supported.");
            };
        }

        /// <summary>
        /// Tries to get the Entity name and primary key route
        /// from the provided string that starts with the REST
        /// path. If the provided string does not start with
        /// the given REST path, we throw an exception. We then
        /// return the entity name as the string up until the next
        /// '/' if one exists, and the primary key as the substring
        /// following the '/'.
        /// </summary>
        /// <param name="route">String containing path + entity name
        /// (and optionally primary key).</param>
        /// <returns>entity name after path.</returns>
        /// <exception cref="DataGatewayException"></exception>
        public (string, string) GetEntityNameAndPrimaryKeyRouteFromRoute(string route)
        {
            string path = _runtimeConfigProvider.RestPath.TrimStart('/');
            if (!route.StartsWith(path))
            {
                throw new DataGatewayException(message: $"Invalid Path for route: {route}.",
                                               statusCode: HttpStatusCode.BadRequest,
                                               subStatusCode: DataGatewayException.SubStatusCodes.BadRequest);
            }

            // entity name comes after the path, so get substring starting from
            // the end of path. If path is not empty we trim the '/' following the path.
            string routeAfterPath = string.IsNullOrEmpty(path) ? route : route.Substring(path.Length).TrimStart('/');
            string primaryKeyRoute = string.Empty;
            string entityName;
            // a '/' remaining in this substring means we have a primary key route
            if (routeAfterPath.Contains('/'))
            {
                // primary key route is what follows the first '/', we trim this an any
                // additional '/'
                primaryKeyRoute = routeAfterPath.Substring(routeAfterPath.IndexOf('/')).TrimStart('/');
                // save entity name as string up until first '/'
                entityName = routeAfterPath[..routeAfterPath.IndexOf('/')];
            }
            else
            {
                entityName = routeAfterPath;
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
        /// <exception cref="DataGatewayException">Thrown when authorization fails.
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
                throw new DataGatewayException(
                    message: "Authorization Failure: Access Not Allowed.",
                    statusCode: HttpStatusCode.Forbidden,
                    subStatusCode: DataGatewayException.SubStatusCodes.AuthorizationCheckFailed);
            }
        }

        /// <summary>
        /// Converts httpverb type of a RestRequestContext object to the
        /// matching CRUD operation, to facilitate authorization checks.
        /// </summary>
        /// <param name="httpVerb"></param>
        /// <returns>The CRUD operation for the given httpverb.</returns>
        public static string HttpVerbToActions(string httpVerbName)
        {
            switch (httpVerbName)
            {
                case "POST":
                    return "create";
                case "PUT":
                case "PATCH":
                    // Please refer to the use of this method, which is to look out for policy based on crud operation type.
                    // Since create doesn't have filter predicates, PUT/PATCH would resolve to update operation.
                    return "update";
                case "DELETE":
                    return "delete";
                case "GET":
                    return "read";
                default:
                    throw new DataGatewayException(
                        message: "Unsupported operation type.",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataGatewayException.SubStatusCodes.BadRequest
                    );
            }
        }
    }
}
