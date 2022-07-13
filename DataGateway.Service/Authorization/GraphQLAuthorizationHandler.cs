using System;
using System.Threading.Tasks;
using Azure.DataGateway.Auth;
using HotChocolate.Resolvers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Azure.DataGateway.Service.Authorization
{
    public class GraphQLAuthorizationHandler : AuthorizationHandler<TheGraphQLREQ, IResolverContext>
    {
        //private IAuthorizationResolver _authorizationResolver;
        //private IHttpContextAccessor _contextAccessor;

        public GraphQLAuthorizationHandler(
            // IAuthorizationResolver authorizationResolver,
            // IHttpContextAccessor httpContextAccessor
            )
        {
            //_authorizationResolver = authorizationResolver;
            //_contextAccessor = httpContextAccessor;
        }

        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, TheGraphQLREQ requirement, IResolverContext resource)
        {
            Console.WriteLine("HERE");
            context.Succeed(requirement);
            return Task.CompletedTask;
        }
    }

    public class TheGraphQLREQ : IAuthorizationRequirement
    {
    }
}
