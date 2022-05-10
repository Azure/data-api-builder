using System.Collections.Generic;

namespace Azure.DataGateway.Service.Models.Authorization
{
    // contains all the helper classes needed for creating the Authorization data structure.
    class EntityDS
    {
        //key : roleName
        public Dictionary<string, RoleDS> RoleToActionMap = new();
    }

    class RoleDS
    {
        //key : actionName
        public Dictionary<string, ActionDS> ActionToColumnMap = new();
    }

    class ActionDS
    {
        public HashSet<string> included = new();
        public HashSet<string> excluded = new();
    }

    public record AuthorizationMetadata(string? RoleName, string? EntityName, string? ActionName, List<string>? Columns);
}
