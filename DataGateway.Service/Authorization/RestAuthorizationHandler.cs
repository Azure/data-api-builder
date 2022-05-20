using System;
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
    public class RestAuthorizationHandler : IAuthorizationHandler
    {
        private IAuthorizationResolver _authorizationResolver;
        private IHttpContextAccessor _contextAccessor;

        public RestAuthorizationHandler(
            IAuthorizationResolver authZResolver,
            IHttpContextAccessor httpContextAccessor)
        {
            _authorizationResolver = authZResolver;
            _contextAccessor = httpContextAccessor;
        }

        public Task HandleAsync(AuthorizationHandlerContext context)
        {
            List<IAuthorizationRequirement> pendingRequirements = context.PendingRequirements.ToList();
            // Catch clause to ensure multiple requirements are not sent at one time, to ensure
            // that requirements are evaluated in order, and fail the request upon first requirement failure.
            //      Order not maintained by pendingRequirements as ASP.NET Core implementation is HashSet.
            // This will prevent extraneous computation on later authZ steps that shouldn't occur for a 403'd request.
            if (pendingRequirements.Count() > 1)
            {
                throw new DataGatewayException(
                    message: "Multiple requirements are not supported.",
                    statusCode: HttpStatusCode.Forbidden,
                    subStatusCode: DataGatewayException.SubStatusCodes.AuthorizationCheckFailed
                );
            }

            // DG requires only 1 requirement be processed at a time.
            IAuthorizationRequirement requirement = pendingRequirements.First();

            if (requirement is Stage1PermissionsRequirement)
            {
                HttpContext? httpContext = _contextAccessor.HttpContext;

                if (httpContext == null)
                {
                    throw new DataGatewayException(
                        message: "HTTP Context Unavailable, Something went wrong",
                        statusCode: System.Net.HttpStatusCode.Unauthorized,
                        subStatusCode: DataGatewayException.SubStatusCodes.UnexpectedError
                        );
                }

                if (_authorizationResolver.IsValidRoleContext(httpContext))
                {
                    context.Succeed(requirement);
                }
            }
            else if (requirement is Stage2PermissionsRequirement)
            {
                if (context.Resource != null)
                {
                    DatabaseObject dbObject = (DatabaseObject)context.Resource;

                    HttpContext? httpContext = _contextAccessor.HttpContext;

                    if (httpContext is null || dbObject is null)
                    {
                        throw new DataGatewayException(
                            message: "HTTP Context Unavailable, Something went wrong",
                            statusCode: System.Net.HttpStatusCode.Unauthorized,
                            subStatusCode: DataGatewayException.SubStatusCodes.UnexpectedError
                        );
                    }

                    string entityName = dbObject.TableDefinition.SourceEntityRelationshipMap.Keys.First();
                    string roleName = httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER];
                    List<string> actions = HttpVerbToCRUD(httpContext.Request.Method);
                    //List<string> actions = OperationToCRUD(restContext.OperationType);

                    foreach(string action in actions)
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
            else if (requirement is Stage3ConfiguredPermissionsRequirement)
            {
                if (context.Resource is not null)
                {
                    // Get column list to authorize
                    // FIND MANY: If includedColumns is *, add table def cols to list, but if a col equals a listed exclude col, then
                    // don't add that.
                    // if included columns is finite/explicit, just add those columns.
                    // All other request types will have columns listed, so no empty column checks will occur. Maybe check for this.
                    RestRequestContext restContext = (RestRequestContext)context.Resource;
                    if (restContext.CumulativeColumns.Count == 0)
                    {
                        // get list of includedColumns from config permissions
                        _authorizationResolver.
                    }
                    bool isAuthorized = _authorizationResolver.AreColumnsAllowedForAction(entityName: , roleName: , actionName: , columns: );
                }
            }

            return Task.CompletedTask;
        }

        private static List<string> OperationToCRUD(Operation operation)
        {
            return operation switch
            {
                Operation.UpsertIncremental => new List<string>(new string[] { "create", "update" }),
                Operation.Upsert => new List<string>(new string[] { "create", "update" }),
                Operation.Find => new List<string>(new string[] { "read"}),
                Operation.Delete => new List<string>(new string[] { "delete" }),
                Operation.Insert => new List<string>(new string[] { "create" }),
                Operation.UpdateIncremental => new List<string>(new string[] { "update" }),
                _ => throw new ArgumentException("Invalid value for operation"),
            };
        }

        private static List<string> HttpVerbToCRUD(string httpVerb)
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
