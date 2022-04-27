using System.Net;
using System.Threading.Tasks;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Configurations;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace Azure.DataGateway.Service.Authorization
{
    /// <summary>
    /// Checks the provided AuthorizationContext and the RestRequestContext to ensure user is allowed to
    /// operate (GET, POST, etc.) on the entity (table).
    /// </summary>
    public class RequestAuthorizationHandler : AuthorizationHandler<OperationAuthorizationRequirement, RestRequestContext>
    {
        private readonly ISqlMetadataProvider _sqlMetadataProvider;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="metadataStoreProvider">The metadata provider.</param>
        /// <param name="isMock">True, if the provided metadata provider is a mock.</param>
        public RequestAuthorizationHandler(
            IRuntimeConfigProvider runtimeConfigProvider,
            bool isMock = false)
        {
            if (runtimeConfigProvider.GetType() != typeof(ISqlMetadataProvider))
            {
                throw new DataGatewayException(
                    message: "Unable to instantiate the RequestAuthorization Handler.",
                    statusCode: HttpStatusCode.InternalServerError,
                    subStatusCode: DataGatewayException.SubStatusCodes.UnexpectedError);
            }

            _sqlMetadataProvider = (ISqlMetadataProvider)runtimeConfigProvider;
        }

        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context,
                                                  OperationAuthorizationRequirement requirement,
                                                  RestRequestContext resource)
        {
            //Request is validated before Authorization, so table will exist.
            TableDefinition tableDefinition =
                _sqlMetadataProvider.GetTableDefinition(resource.EntityName);

            string requestedOperation = resource.HttpVerb.Name;
            if (tableDefinition.HttpVerbs == null || tableDefinition.HttpVerbs.Count == 0)
            {
                context.Fail();
            }
            //Check current operation against tableDefinition supported operations.
            else if (tableDefinition.HttpVerbs.ContainsKey(requestedOperation))
            {
                switch (tableDefinition.HttpVerbs[requestedOperation].AuthorizationType)
                {
                    case AuthorizationType.NoAccess:
                        context.Fail();
                        break;
                    case AuthorizationType.Anonymous:
                        context.Succeed(requirement);
                        break;
                    case AuthorizationType.Authenticated:
                        if (context.User.Identity != null && context.User.Identity.IsAuthenticated)
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
