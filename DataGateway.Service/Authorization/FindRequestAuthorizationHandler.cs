using System.Net.Http;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Resolvers;
using Azure.DataGateway.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace Azure.DataGateway.Service.Authorization
{
    /// <summary>
    /// Enumeration of Supported Authorization Types
    /// </summary>
    public enum AuthorizationType
    {
        Anonymous,
        Authenticated,
        Roles,
        Attributes
    }

    /// <summary>
    /// Checks the provided AuthorizationContext and FindRequestContext to ensure user is allowed to
    /// operate (GET, POST, etc.) on the entity (table).
    /// </summary>
    public class FindRequestAuthorizationHandler : AuthorizationHandler<OperationAuthorizationRequirement, FindRequestContext>
    {
        private readonly IMetadataStoreProvider _configurationProvider;

        public FindRequestAuthorizationHandler(IMetadataStoreProvider metadataStoreProvider)
        {
            _configurationProvider = metadataStoreProvider;
        }
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context,
                                                  OperationAuthorizationRequirement requirement,
                                                  FindRequestContext resource)
        {
            //Request is validated before Authorization, so table will exist.
            TableDefinition tableDefinition = _configurationProvider.GetTableDefinition(resource.EntityName);

            //Check current operation against tableDefinition supported operations.
            if (tableDefinition.Operations.ContainsKey(HttpMethod.Get.ToString()))
            {
                switch (tableDefinition.Operations[HttpMethod.Get.ToString()].AuthorizationType)
                {
                    case AuthorizationType.Anonymous:
                        context.Succeed(requirement);
                        break;
                    case AuthorizationType.Authenticated:
                        if (context.User.Identity.IsAuthenticated)
                        {
                            context.Succeed(requirement);
                        }

                        break;
                    default:
                        break;
                }
            }

            //If we don't explicitly call Succeed(), the Authorization fails.
            return Task.CompletedTask;
        }
    }
}
