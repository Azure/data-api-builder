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
            //      Order not maintained by pendingRequirements as ASP.NET Core implementation is HashSet.
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
                    DatabaseObject dbObject = (DatabaseObject)context.Resource;

                    if (dbObject is null)
                    {
                        throw new DataGatewayException(
                            message: "DbObject Resource Null, Something went wrong",
                            statusCode: System.Net.HttpStatusCode.Unauthorized,
                            subStatusCode: DataGatewayException.SubStatusCodes.UnexpectedError
                        );
                    }

                    string entityName = dbObject.TableDefinition.SourceEntityRelationshipMap.Keys.First();
                    string roleName = httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER];
                    List<string> actions = HttpVerbToActions(httpContext.Request.Method);

                    foreach (string action in actions)
                    {
                        bool isAuthorized = _authorizationResolver.AreRoleAndActionDefinedForEntity(entityName, roleName, action);
                        if (!isAuthorized)
                        {
                            context.Fail();
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
                    // Get column list to authorize
                    // FIND MANY: If includedColumns is *, add table definition columns to list.
                    // If a requested column equals a listed exclude col, then
                    // don't add that.
                    // if included columns is finite/explicit, just add those columns.
                    // All other request types will have columns listed, so no empty column checks will occur. Maybe check for this.
                    RestRequestContext restContext = (RestRequestContext)context.Resource;
                    string entityName = restContext.DatabaseObject.TableDefinition.SourceEntityRelationshipMap.Keys.First();
                    string roleName = httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER];
                    List<string> actions = HttpVerbToActions(httpContext.Request.Method);

                    if (restContext.TryCalculateCumulativeColumns())
                    {
                        // Two actions must be checked when HTTP operation is PUT or PATCH.
                        // PUT and PATCH resolve to actions 'create' and 'update'.
                        // A user must fulfill both actions' permissions requirements to proceed.
                        foreach (string action in actions)
                        {
                            List<string> columnsToCheck;

                            // No cumulative columns indicates that this is a FindMany request.
                            // To resolve columns to return, check permissions for role.
                            if (restContext.CumulativeColumns.Count == 0)
                            {
                                columnsToCheck = _authorizationResolver.GetAllowedColumns(entityName, roleName, action);
                            }
                            else
                            {
                                columnsToCheck = restContext.CumulativeColumns.ToList();
                            }

                            if (_authorizationResolver.AreColumnsAllowedForAction(entityName, roleName, action, columnsToCheck))
                            {
                                // This check catches the FindMany variant with no filters or column references.
                                if (restContext.FieldsToBeReturned.Count == 0 && restContext.IsMany)
                                {
                                    // Union performed to avoid duplicate field names in FieldsToBeReturned.
                                    restContext.FieldsToBeReturned.Union(columnsToCheck);
                                }
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
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Converts httpverb type of a RestRequestContext object to the
        /// matching CRUD operation(s), to facilitate authorization checks.
        /// </summary>
        /// <param name="httpVerb"></param>
        /// <returns></returns>
        private static List<string> HttpVerbToActions(string httpVerb)
        {
            switch (httpVerb)
            {
                case "POST":
                    return new List<string>(new string[] { "create" });
                case "PUT":
                case "PATCH":
                    return new List<string>(new string[] { "create", "update" });
                case "DELETE":
                    return new List<string>(new string[] { "delete" });
                case "GET":
                    return new List<string>(new string[] { "read" });
                default:
                    break;
            }

            return new List<string>();
        }
    }
}
