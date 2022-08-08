using Azure.DataApiBuilder.Config;

namespace Azure.DataApiBuilder.Auth
{
    /// <summary>
    /// Represents the permission metadata of an entity.
    /// An entity's top-level permission structure is a collection
    /// of roles.
    /// </summary>
    public class EntityMetadata
    {
        /// <summary>
        /// Given the key (roleName) returns the associated RoleMetadata object.
        /// To retrieve all roles associated with an entity -> RoleToActionMap.Keys()
        /// </summary>
        public Dictionary<string, RoleMetadata> RoleToActionMap { get; set; } = new();

        /// <summary>
        /// Field to action to role mapping.
        /// Given the key (Field aka. column name) returns a key/value collection of action to Roles
        /// i.e. ID column
        /// Key(field): id -> Dictionary(actions)
        ///     each entry in the dictionary contains action to role map.
        ///     Create: permitted in {Role1, Role2, ..., RoleN}
        ///     Delete: permitted in {Role1, RoleN}
        /// </summary>
        public Dictionary<string, Dictionary<Operation, List<string>>> FieldToRolesMap { get; set; } = new();

        /// <summary>
        /// Given the key (action) returns a collection of roles
        /// defining config permissions for the action.
        /// i.e. Read action is permitted in {Role1, Role2, ..., RoleN}
        /// </summary>
        public Dictionary<Operation, List<string>> ActionToRolesMap { get; set; } = new();
    }

    /// <summary>
    /// Represents the permission metadata of a role
    /// A role's top-level permission structure is a collection of
    /// actions allowed for that role: Create, Read, Update, Delete, All (wildcard action)
    /// </summary>
    public class RoleMetadata
    {
        /// <summary>
        /// Given the key (action) returns the associated ActionMetadata object.
        /// </summary>
        public Dictionary<Operation, ActionMetadata> ActionToColumnMap { get; set; } = new();
    }

    /// <summary>
    /// Represents the permission metadata of an action
    /// An action lists both columns that are included and/or exluded
    /// for that action.
    /// </summary>
    public class ActionMetadata
    {
        public string? DatabasePolicy { get; set; }
        public HashSet<string> Included { get; set; } = new();
        public HashSet<string> Excluded { get; set; } = new();
        public HashSet<string> AllowedExposedColumns { get; set; } = new();
    }
}
