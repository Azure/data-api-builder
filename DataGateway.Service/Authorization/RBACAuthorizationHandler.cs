using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Azure.DataGateway.Service.Authorization
{
    /// <summary>
    /// Authorization handler to process a request against permissions defined
    /// in the Developer configuration.
    /// </summary>
    public class RBACAuthorizationHandler : AuthorizationHandler<EnforceConfiguredPermissionsRequirement>
    {
        private const string ROLE_CONTEXT_HEADER = "X-DG-ROLE";

        private IAuthorizationResolver _authorizationResolver;
        private readonly SqlGraphQLFileMetadataProvider _configurationProvider;
        private IHttpContextAccessor _contextAccessor;

        public RBACAuthorizationHandler(
            IAuthorizationResolver authZResolver,
            IGraphQLMetadataProvider configProvider,
            IHttpContextAccessor httpContextAccessor)
        {
            _authorizationResolver = authZResolver;
            _configurationProvider = (SqlGraphQLFileMetadataProvider) configProvider;
            _contextAccessor = httpContextAccessor;
        }

        protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, EnforceConfiguredPermissionsRequirement requirement)
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

            // Pull request contents
            string desiredRoleContext = httpContext.Request.Headers[ROLE_CONTEXT_HEADER];
            string requestActionType = httpContext.Request.Method;
            string entityName = httpContext.Request.RouteValues.First().Key;
            // Get X-DG-Role from Http Header and match against httpContext.Identity role claims

            // For Now, break into separate if statements for easy debugging.
            // This will indicate which check failed.
            // Ideally, this will be one IF statement with all checks OR'd.
            // the first failure (!authZResolver.Check()) will resolve to true and fail authorization.
            if (!_authorizationResolver.IsValidRoleContext(httpRequestData: httpContext.Request))
            {
                context.Fail();
            }

            if (!_authorizationResolver.IsRoleDefinedForEntity(desiredRoleContext, entityName ))
            {
                context.Fail();
            }

            if (!_authorizationResolver.IsActionAllowedForRole(action: requestActionType, roleName: desiredRoleContext))
            {
                context.Fail();
            }

            if (!_authorizationResolver.IsColumnSetAllowedForAction())
            {
                context.Fail();
            }

            if (!_authorizationResolver.DidProcessDBPolicy(action: requestActionType, roleName: desiredRoleContext, httpContext: httpContext))
            {
                context.Fail();
            }

            context.Succeed(requirement);

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// A requirement implements IAuthorizationRequirement, which is an empty marker interface.
    /// https://docs.microsoft.com/aspnet/core/security/authorization/policies?view=aspnetcore-6.0#requirements
    /// </summary>
    public class EnforceConfiguredPermissionsRequirement : IAuthorizationRequirement { }
}
