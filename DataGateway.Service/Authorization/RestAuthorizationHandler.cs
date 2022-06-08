using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Azure.DataGateway.Service.Authorization
{
    /// <summary>
    /// Make authorization decisions for REST requests.
    /// Checks a requirement against an object(resource) to decide whether
    /// a request should continue processing.
    /// </summary>
    public class RestAuthorizationHandler : IAuthorizationHandler
    {
        private IAuthorizationResolver _authorizationResolver;
        private IHttpContextAccessor _contextAccessor;

        public RestAuthorizationHandler(
            IAuthorizationResolver authorizationResolver,
            IHttpContextAccessor httpContextAccessor
            )
        {
            _authorizationResolver = authorizationResolver;
            _contextAccessor = httpContextAccessor;
        }

        /// <summary>
        /// Executed by the internal ASP.NET authorization engine
        /// whenever client code calls authorizationService.AuthorizeAsync()
        /// Execution of this method calls either 
        ///     .Succeed(IRequirement)
        ///     .Failure()
        /// To set the result of authorization. If Faiure() is called, this
        /// ensures Microsoft.AspNetCore.Authorization.AuthorizationHandlerContext.HasSucceeded
        //  will never return true.
        /// </summary>
        /// <param name="context">Contains the requirement and object(resource)
        /// to determine an authorization decision. </param>
        /// <returns>No object is returned.</returns>
        /// <exception cref="DataGatewayException"></exception>
        public Task HandleAsync(AuthorizationHandlerContext context)
        {
            // Catch clause to ensure multiple requirements are not sent at one time, to ensure
            // that requirements are evaluated in order, and fail the request upon first requirement failure.
            // Order not maintained by pendingRequirements as ASP.NET Core implementation is HashSet.
            // This will prevent extraneous computation on later authorization steps that shouldn't occur for a request
            // that has already been evaluated as Unauthorized.
            if (context.PendingRequirements.Count() > 1)
            {
                throw new DataGatewayException(
                    message: "Multiple requirements are not supported.",
                    statusCode: HttpStatusCode.Forbidden,
                    subStatusCode: DataGatewayException.SubStatusCodes.AuthorizationCheckFailed
                );
            }

            HttpContext? httpContext = _contextAccessor.HttpContext;

            if (httpContext is null)
            {
                throw new DataGatewayException(
                    message: "HTTP Context Unavailable, Something went wrong",
                    statusCode: System.Net.HttpStatusCode.Unauthorized,
                    subStatusCode: DataGatewayException.SubStatusCodes.UnexpectedError
                    );
            }

            // DataGateway requires that only 1 requirement be processed at a time.
            IAuthorizationRequirement requirement = context.PendingRequirements.First();

            if (requirement is RoleContextPermissionsRequirement)
            {
                if (_authorizationResolver.IsValidRoleContext(httpContext))
                {
                    context.Succeed(requirement);
                }
            }
            else if (requirement is EntityRoleActionPermissionsRequirement)
            {
                if (context.Resource is not null)
                {
                    string? entityName = context.Resource as string;

                    if (entityName is null)
                    {
                        throw new DataGatewayException(
                            message: "restContext Resource Null, Something went wrong",
                            statusCode: HttpStatusCode.Unauthorized,
                            subStatusCode: DataGatewayException.SubStatusCodes.UnexpectedError
                        );
                    }

                    string roleName = httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER];
                    IEnumerable<string> actions = HttpVerbToActions(httpContext.Request.Method);

                    foreach (string action in actions)
                    {
                        bool isAuthorized = _authorizationResolver.AreRoleAndActionDefinedForEntity(entityName, roleName, action);
                        if (!isAuthorized)
                        {
                            context.Fail();
                            break;
                        }
                    }

                    // All requirement checks must pass.
                    if (!context.HasFailed)
                    {
                        context.Succeed(requirement);
                    }
                }
                else
                {
                    context.Fail();
                }
            }
            else if (requirement is ColumnsPermissionsRequirement)
            {
                if (context.Resource is not null)
                {
                    RestRequestContext? restContext = context.Resource as RestRequestContext;

                    if (restContext is null)
                    {
                        throw new DataGatewayException(
                            message: "restContext Resource Null, Something went wrong",
                            statusCode: HttpStatusCode.Unauthorized,
                            subStatusCode: DataGatewayException.SubStatusCodes.UnexpectedError
                        );
                    }

                    string entityName = restContext.EntityName;
                    string roleName = httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER];
                    IEnumerable<string> actions = HttpVerbToActions(httpContext.Request.Method);

                    // Delete operations do not have column level restrictions.
                    // If the operation is allowed for the role, the column requirement is implicitly successful,
                    // and the authorization check can be short circuited here.
                    if (actions.Count() == 1 && actions.Contains(ActionType.DELETE))
                    {
                        context.Succeed(requirement);
                        return Task.CompletedTask;
                    }

                    // Attempts to get list of unique columns present in request metadata.
                    restContext.CalculateCumulativeColumns();

                    // Two actions must be checked when HTTP operation is PUT or PATCH,
                    // otherwise, just one action is checked.
                    // PUT and PATCH resolve to actions 'create' and 'update'.
                    // A user must fulfill all actions' permissions requirements to proceed.
                    foreach (string action in actions)
                    {
                        // Get a list of all columns present in a request that need to be authorized.
                        IEnumerable<string> columnsToCheck = restContext.CumulativeColumns;

                        // When Issue #XX for REST Column Aliases is merged, the request field names(which may be aliases)
                        // will be converted to field names denoted in the permissions config.
                        // i.e. columnsToCheck = convertExposedNamesToBackingColumns()

                        // Authorize field names present in a request.
                        if (columnsToCheck.Count() > 0 && _authorizationResolver.AreColumnsAllowedForAction(entityName, roleName, action, columnsToCheck))
                        {
                            // Find operations with no column filter in the query string will have FieldsToBeReturned == 0.
                            // Then, the "allowed columns" resolved, will be set on FieldsToBeReturned.
                            // When FieldsToBeReturned is originally >=1 column, the field is NOT modified here.
                            if (restContext.FieldsToBeReturned.Count == 0 && restContext.OperationType == Operation.Find)
                            {
                                // Union performed to avoid duplicate field names in FieldsToBeReturned.
                                IEnumerable<string> fieldsReturnedForFind = _authorizationResolver.GetAllowedColumns(entityName, roleName, action);
                                restContext.UpdateReturnFields(fieldsReturnedForFind);
                            }
                        }
                        else if (columnsToCheck.Count() == 0 && restContext.OperationType is Operation.Find)
                        {
                            // - Find operations typically return all metadata of a database record.
                            // This check resolves all 'included' columns defined in permissions
                            // so only those included columns are present in the result(s).
                            // - For other operation types, columnsToCheck is a result of identifying
                            // any reference to a column in all parts of a request (body, URL, querystring)
                            IEnumerable<string> fieldsReturnedForFind = _authorizationResolver.GetAllowedColumns(entityName, roleName, action);
                            restContext.UpdateReturnFields(fieldsReturnedForFind);
                        }
                        else
                        {
                            context.Fail();
                        }
                    }

                    if (!context.HasFailed)
                    {
                        context.Succeed(requirement);
                    }
                }
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Converts httpverb type of a RestRequestContext object to the
        /// matching CRUD operation(s), to facilitate authorization checks.
        /// </summary>
        /// <param name="httpVerb"></param>
        /// <returns>A collection of ActionTypes resolved from the http verb type of the request.</returns>
        private static IEnumerable<string> HttpVerbToActions(string httpVerb)
        {
            switch (httpVerb)
            {
                case HttpConstants.POST:
                    return new List<string>(new string[] { ActionType.CREATE });
                case HttpConstants.PUT:
                case HttpConstants.PATCH:
                    return new List<string>(new string[] { ActionType.CREATE, ActionType.UPDATE });
                case HttpConstants.DELETE:
                    return new List<string>(new string[] { ActionType.DELETE });
                case HttpConstants.GET:
                    return new List<string>(new string[] { ActionType.READ });
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
