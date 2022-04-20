using System;
using System.Net;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Services;
using Microsoft.AspNetCore.Http;

namespace Azure.DataGateway.Service.Authorization
{
    public class DGAuthorizationResolver : IAuthorizationResolver
    {
        private SqlGraphQLFileMetadataProvider _metadataStoreProvider;
        private object _authZData;

        public DGAuthorizationResolver(IGraphQLMetadataProvider metadataStoreProvider)
        {
            if (metadataStoreProvider.GetType() != typeof(SqlGraphQLFileMetadataProvider))
            {
                throw new DataGatewayException(
                    message: "Unable to instantiate the SQL query engine.",
                    statusCode: HttpStatusCode.InternalServerError,
                    subStatusCode: DataGatewayException.SubStatusCodes.UnexpectedError);
            }

            _metadataStoreProvider = (SqlGraphQLFileMetadataProvider)metadataStoreProvider;

            // Datastructure constructor will pull required properties from metadataprovider.
            // _authZData = new AuthZDataStructure(_metadataStoreProvider);
        }

        // Whether X-DG-Role Http Request Header is present in httpContext.Identity.Claims.Roles
        public bool IsValidRoleContext(HttpRequest httpRequestData)
        {
            // TO-DO #1
            throw new NotImplementedException();
        }

        // Whether X-DG-Role Http Request Header value is present in DeveloperConfig:Entity
        // This should fail if entity does not exist. For now: should be 403 Forbidden instead of 404
        // to avoid leaking Schema data.
        public bool IsRoleDefinedForEntity(string roleName, string entityName)
        {
            // TO-DO #2 pending lock in on DataStructure storing dev config.
            throw new NotImplementedException();
        }

        // Whether Entity.Role has action defined
        public bool IsActionAllowedForRole(string action, string roleName)
        {
            // TO-DO #3 pending lock in on DataStructure storing dev config.
            throw new NotImplementedException();
        }

        // Compare columns in request body to columns in entity.Role.Action.AllowedColumns
        public bool IsColumnSetAllowedForAction()
        {
            //No-Op for now
            throw new NotImplementedException();
        }

        public bool DidProcessDBPolicy(string action, string roleName, HttpContext httpContext)
        {
            string policy = GetPolicyForRoleandAction(action, roleName);

            // the output of this func will be the tokenclaims substituted policy. 
            PolicyHelper.ProcessTokenClaimsForPolicy(policy, httpContext);

            // Write policy to httpContext for use in downstream controllers/services.
            try
            {
                httpContext.Items.Add(
                    key: "X-DG-Policy",
                    value: PolicyHelper.ProcessTokenClaimsForPolicy(policy: policy, context: httpContext)
                );
            }
            catch(Exception)
            {
                return false;
            }

            return true;
        }

        #region Helpers
        private static string GetPolicyForRoleandAction(string action, string roleName)
        {
            // Read AuthZ datastructure to get policy text.
            // return authZData[entity].action[action].role[roleName].policy;
            return "@claims().userID eq publisher_id";
        }
        #endregion
    }
}
