using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Models.Authorization;
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

                _authorizationResolver.IsValidRoleContext(httpContext);
            }
            else if (requirement is Stage2PermissionsRequirement)
            {
                if (context.Resource != null)
                {
                    AuthorizationMetadata authData = (AuthorizationMetadata)context.Resource;
                    if (authData.RoleName != null && authData.EntityName != null)
                    {
                        bool status = _authorizationResolver.IsRoleDefinedForEntity(roleName: authData.RoleName, entityName: authData.EntityName);

                        if (status)
                        {
                            context.Succeed(requirement);
                        }
                    }
                }
                else
                {
                    context.Fail();
                }
            }
            else if (requirement is Stage3ConfiguredPermissionsRequirement)
            {

            }
            else if (requirement is Stage4ConfiguredPermissionsRequirement)
            {

            }

            return Task.CompletedTask;
        }
    }
}
