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
        /// To retrieve all roles associated with an entity -> RoleToOperationMap.Keys().
        /// Since the roleNames are case insensitive, we use IEqualityComparer for ignoring
        /// the case.
        /// </summary>
        public Dictionary<string, RoleMetadata> RoleToOperationMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Field to operation to role mapping.
        /// Given the key (Field aka. column name) returns a key/value collection of operation to Roles
        /// i.e. ID column
        /// Key(field): id -> Dictionary(operations)
        ///     each entry in the dictionary contains operation to role map.
        ///     Create: permitted in {Role1, Role2, ..., RoleN}
        ///     Delete: permitted in {Role1, RoleN}
        /// </summary>
        public Dictionary<string, Dictionary<Operation, List<string>>> FieldToRolesMap { get; set; } = new();

        /// <summary>
        /// Given the key (operation) returns a collection of roles
        /// defining config permissions for the operation.
        /// i.e. Read operation is permitted in {Role1, Role2, ..., RoleN}
        /// </summary>
        public Dictionary<Operation, List<string>> OperationToRolesMap { get; set; } = new();

        /// <summary>
        /// Set of Http verbs enabled for Stored Procedure/Function entities that have their REST endpoint enabled.
        /// </summary>
        public HashSet<RestMethod> DatabaseExecutableHttpVerbs { get; set; } = new();

        /// <summary>
        /// Defines the type of database object the entity represents.
        /// Examples include Table, View, StoredProcedure, Function
        /// </summary>
        public SourceType ObjectType { get; set; } = SourceType.Table;
    }

    /// <summary>
    /// Represents the permission metadata of a role
    /// A role's top-level permission structure is a collection of
    /// Operations allowed for that role: Create, Read, Update, Delete, All (wildcard operation)
    /// </summary>
    public class RoleMetadata
    {
        /// <summary>
        /// Given the key (operation) returns the associated OperationMetadata object.
        /// </summary>
        public Dictionary<Operation, OperationMetadata> OperationToColumnMap { get; set; } = new();
    }

    /// <summary>
    /// Represents the permission metadata of an operation
    /// An operation lists both columns that are included and/or excluded
    /// for that operation.
    /// </summary>
    public class OperationMetadata
    {
        public string? DatabasePolicy { get; set; }
        public HashSet<string> Included { get; set; } = new();
        public HashSet<string> Excluded { get; set; } = new();
        public HashSet<string> AllowedExposedColumns { get; set; } = new();
    }
}
