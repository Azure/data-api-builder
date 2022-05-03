using Microsoft.AspNetCore.Authorization;

namespace Azure.DataGateway.Service.Authorization
{
    /// <summary>
    /// A requirement implements IAuthorizationRequirement, which is an empty marker interface.
    /// https://docs.microsoft.com/aspnet/core/security/authorization/policies?view=aspnetcore-6.0#requirements
    /// </summary>
    public class Stage1PermissionsRequirement : IAuthorizationRequirement { }
    public class Stage2PermissionsRequirement : IAuthorizationRequirement { }
    public class Stage3ConfiguredPermissionsRequirement : IAuthorizationRequirement { }
    public class Stage4ConfiguredPermissionsRequirement : IAuthorizationRequirement { }
    public class Stage5ConfiguredPermissionsRequirement : IAuthorizationRequirement { }
}
