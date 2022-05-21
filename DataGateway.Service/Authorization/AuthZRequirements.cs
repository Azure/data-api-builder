using Microsoft.AspNetCore.Authorization;

namespace Azure.DataGateway.Service.Authorization
{
    /// <summary>
    /// A requirement implements IAuthorizationRequirement, which is an empty marker interface.
    /// https://docs.microsoft.com/aspnet/core/security/authorization/policies?view=aspnetcore-6.0#requirements
    /// </summary>
    public class RoleContextPermissionsRequirement : IAuthorizationRequirement { }
    public class EntityRoleActionPermissionsRequirement : IAuthorizationRequirement { }
    public class ColumnsPermissionsRequirement : IAuthorizationRequirement { }
}
