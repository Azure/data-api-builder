using System.Collections.Generic;

namespace Azure.DataGateway.Service
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
        public Dictionary<string, bool>? included = new() { { "*", true } };
        public Dictionary<string, bool>? excluded = new();
    }
}
