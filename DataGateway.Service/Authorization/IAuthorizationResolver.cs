using Microsoft.AspNetCore.Http;

namespace Azure.DataGateway.Service.Authorization
{
    public interface IAuthorizationResolver
    {
        // Whether X-DG-Role Http Request Header is present in httpContext.Identity.Claims.Roles
        public bool IsValidRoleContext(HttpRequest httpRequestData);

        // Whether X-DG-Role Http Request Header value is present in DeveloperConfig:Entity
        // This should fail if entity does not exist. For now: should be 403 Forbidden instead of 404
        // to avoid leaking Schema data.
        public bool IsRoleDefinedForEntity(string roleName, string entityName);

        // Whether Entity.Role has action defined
        public bool IsActionAllowedForRole(string action, string roleName);

        // No-Op for now -> compare columns in request body to columns in entity.Role.Action.AllowedColumns
        public bool IsColumnSetAllowedForAction();

        public bool DidProcessDBPolicy(string action, string roleName, HttpContext httpContext);
    }
}
