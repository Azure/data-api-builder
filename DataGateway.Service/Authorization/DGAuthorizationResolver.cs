using System;
using Microsoft.AspNetCore.Http;

namespace Azure.DataGateway.Service.Authorization
{
    public class DGAuthorizationResolver : IAuthorizationResolver
    {
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
    }
}
