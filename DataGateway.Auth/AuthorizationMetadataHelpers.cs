namespace Azure.DataGateway.Auth
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
        /// Given the key (actionName) returns a key/value collection of fieldName to Roles
        /// i.e. READ action
        /// Key(field): id -> Value(collection): permitted in {Role1, Role2, ..., RoleN}
        /// Key(field): title -> Value(collection): permitted in {Role1}
        /// </summary>
        public Dictionary<string, Dictionary<string, IEnumerable<string>>> FieldToRolesMap { get; set; } = new();

        /// <summary>
        /// Given the key (actionName) returns a collection of roles
        /// defining config permissions for the action.
        /// i.e. READ action is permitted in {Role1, Role2, ..., RoleN}
        /// </summary>
        public Dictionary<string, List<string>> ActionToRolesMap { get; set; } = new();
    }

    /// <summary>
    /// Represents the permission metadata of a role
    /// A role's top-level permission structure is a collection of
    /// actions allowed for that role: Create, Read, Update, Delete, * (wildcard)
    /// </summary>
    public class RoleMetadata
    {
        /// <summary>
        /// Given the key (actionName) returns the associated ActionMetadata object.
        /// </summary>
        public Dictionary<string, ActionMetadata> ActionToColumnMap { get; set; } = new();
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
        public HashSet<string> Allowed { get; set; } = new();
    }
}
