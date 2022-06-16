using System.Collections.Generic;

namespace Azure.DataGateway.Service.Models.Authorization
{
    /// <summary>
    /// Represents the permission metadata of an entity.
    /// An entity's top-level permission structure is a collection
    /// of roles.
    /// </summary>
    class EntityMetadata
    {
        /// <summary>
        /// Given the key (roleName) returns the associated RoleDS object.
        /// </summary>
        public Dictionary<string, RoleMetadata> RoleToActionMap = new();
    }

    /// <summary>
    /// Represents the permission metadata of a role
    /// A role's top-level permission structure is a collection of
    /// actions allowed for that role: Create, Read, Update, Delete, * (wildcard)
    /// </summary>
    class RoleMetadata
    {
        /// <summary>
        /// Given the key (actionName) returns the associated ActionDS object.
        /// </summary>
        public Dictionary<string, ActionMetadata> ActionToColumnMap = new();
    }

    /// <summary>
    /// Represents the permission metadata of an action
    /// An action lists both columns that are included and/or exluded
    /// for that action.
    /// </summary>
    class ActionMetadata
    {
        public string? databasePolicy;
        public HashSet<string> included = new();
        public HashSet<string> excluded = new();
    }
}
