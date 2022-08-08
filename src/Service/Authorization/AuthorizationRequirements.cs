using Microsoft.AspNetCore.Authorization;

namespace Azure.DataApiBuilder.Service.Authorization
{
    /// <summary>
    /// Instructs the authorization handler to check that:
    ///    - The client role header maps to a role claim on the authenticated user.
    ///
    /// Implements IAuthorizationRequirement, which is an empty marker interface.
    /// https://docs.microsoft.com/aspnet/core/security/authorization/policies?view=aspnetcore-6.0#requirements
    /// </summary>
    public class RoleContextPermissionsRequirement : IAuthorizationRequirement { }

    /// <summary>
    /// Instructs the authorization handler to check that:
    ///     - The entity has an entry for the role defined in the client role header.
    ///     - The discovered role entry has an entry for the actiontype of the request.
    /// 
    /// Implements IAuthorizationRequirement, which is an empty marker interface.
    /// https://docs.microsoft.com/aspnet/core/security/authorization/policies?view=aspnetcore-6.0#requirements
    /// </summary>
    public class EntityRoleActionPermissionsRequirement : IAuthorizationRequirement { }

    /// <summary>
    /// Instructs the authorization handler to check that:
    ///     - The columns included in the request are allowed to be accessed by the authenticated user.
    /// For requests on *Many requests, restricts the results to only include fields allowed to be
    /// accessed by the authenticated user.
    ///
    /// Implements IAuthorizationRequirement, which is an empty marker interface.
    /// https://docs.microsoft.com/aspnet/core/security/authorization/policies?view=aspnetcore-6.0#requirements
    /// </summary>
    public class ColumnsPermissionsRequirement : IAuthorizationRequirement { }
}
