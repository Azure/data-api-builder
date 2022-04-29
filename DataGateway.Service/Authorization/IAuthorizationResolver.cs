using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace Azure.DataGateway.Service.Authorization
{
    public interface IAuthorizationResolver
    {
        // Whether X-DG-Role Http Request Header is present in httpContext.Identity.Claims.Roles
        public bool IsValidRoleContext(HttpContext httpContext);

        // Whether X-DG-Role Http Request Header value is present in DeveloperConfig:Entity
        // This should fail if entity does not exist. For now: should be 403 Forbidden instead of 404
        // to avoid leaking Schema data.
        public bool IsRoleDefinedForEntity(string roleName, string entityName);

        // Whether Entity.Role has action defined
        public bool IsActionAllowedForRole(string roleName, string entityName, string action);

        // Compare columns in request body to columns in entity.Role.Action.AllowedColumns
        public bool AreColumnsAllowedForAction(string roleName, string entityName, string action, List<string> columns);

        //  Parse policy into query predicate for request
        public bool DidProcessDBPolicy(string action, string roleName, HttpContext httpContext);
    }
}
