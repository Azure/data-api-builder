using System.Collections.Generic;

namespace Azure.DataGateway.Service.Models.Authorization
{
    class EntityToRole
    {
        public Dictionary<string, RoleToAction> roleToActionMap = new();
    }

    class RoleToAction
    {
        public Dictionary<string, ActionToColumn> actionToColumnMap = new();
    }

    class ActionToColumn
    {
        public Dictionary<string, bool>? included = new();
        public Dictionary<string, bool>? excluded = new();
    }
}
