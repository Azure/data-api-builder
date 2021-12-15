using System.Threading.Tasks;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Resolvers;
using Azure.DataGateway.Services;
using Microsoft.AspNetCore.Authorization;

namespace Azure.DataGateway.Service.Authorization
{
    public enum AuthorizationType
    {
        Anonymous,
        Authenticated,
        Roles,
        Attributes
    }
    public class FindRequestAuthorizationHandler : AuthorizationHandler<IsAuthenticatedRequirement, FindRequestContext>
    {
        private readonly IMetadataStoreProvider _configurationProvider;

        public FindRequestAuthorizationHandler(IMetadataStoreProvider metadataStoreProvider)
        {
            _configurationProvider = metadataStoreProvider;
        }
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context,
                                                  IsAuthenticatedRequirement requirement,
                                                  FindRequestContext resource)
        {
            //Reference the TableDefinition from the configuration metadata.
            TableDefinition tableDefinition = _configurationProvider.GetTableDefinition(resource.EntityName);

            if(tableDefinition != null)
            {
                //Check current operation against tableDefinition supported operations.
                string requestOperationType = "Get";
                if (tableDefinition.Operations.ContainsKey(requestOperationType))
                {
                    switch (tableDefinition.Operations[requestOperationType].AuthorizationType)
                    {
                        case AuthorizationType.Anonymous:
                            context.Succeed(requirement);
                            break;
                        case AuthorizationType.Authenticated:
                            //Require General Authentication to succeed.
                            if (context.User.Identity.IsAuthenticated)
                            {
                                context.Succeed(requirement);
                            }
                            else
                            {
                                context.Fail();
                            }

                            break;
                        default:
                            break;
                    }
                }
                else
                {
                    //TODO: currently fail authZ if table is not scoped as visible within config
                    //This should actually trigger a 404 not found instead of 401/403 as unauthorized would leak
                    //database table entity existance knowledge.
                    context.Fail();
                }
            }
            else
            {
                context.Fail();
            }

            //Check table
            return Task.CompletedTask;
        }
    }

    //Marker Interface: https://stackoverflow.com/questions/1023068/what-is-the-purpose-of-a-marker-interface
    public class IsAuthenticatedRequirement : IAuthorizationRequirement { }
}
