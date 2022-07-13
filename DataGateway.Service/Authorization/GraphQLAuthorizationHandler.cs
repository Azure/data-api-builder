using System.Threading.Tasks;
using HotChocolate.Resolvers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Azure.DataGateway.Service.Authorization
{
    public class GraphQLAuthorizationHandler : AuthorizationHandler<TheGraphQLREQ, IResolverContext>
    {
        private IHttpContextAccessor _contextAccessor;

        public GraphQLAuthorizationHandler(
            IHttpContextAccessor httpContextAccessor
            )
        {
            _contextAccessor = httpContextAccessor;
        }

        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, TheGraphQLREQ requirement, IResolverContext resource)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }
    }

    public class TheGraphQLREQ : IAuthorizationRequirement
    {
    }
}
