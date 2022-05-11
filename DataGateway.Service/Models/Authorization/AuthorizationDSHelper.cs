using System.Collections.Generic;

namespace Azure.DataGateway.Service.Models.Authorization
{
    /// <summary>
    /// Represents the permission metadata of an entity.
    /// An entity's top-level permission structure is a collection
    /// of roles.
    /// </summary>
    class EntityDS
    {
        /// <summary>
        /// Given the key (roleName) returns the associated RoleDS object.
        /// </summary>
        public Dictionary<string, RoleDS> RoleToActionMap = new();
    }

    /// <summary>
    /// Represents the permission metadata of a role
    /// A role's top-level permission structure is a collection of
    /// actions allowed for that role: Create, Read, Update, Delete, * (wildcard)
    /// </summary>
    class RoleDS
    {
        /// <summary>
        /// Given the key (actionName) returns the associated ActionDS object.
        /// </summary>
        public Dictionary<string, ActionDS> ActionToColumnMap = new();
    }

    /// <summary>
    /// Represents the permission metadata of an action
    /// An action lists both columns that are included and/or exluded
    /// for that action.
    /// </summary>
    class ActionDS
    {
        public HashSet<string> included = new();
        public HashSet<string> excluded = new();
    }
}
