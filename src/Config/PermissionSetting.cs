using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config
{
    /// <summary>
    /// Defines which operations (Creat, Read, Update, Delete, Execute) are permitted for a given role.
    /// </summary>
    public class PermissionSetting
    {
        /// <summary>
        /// Creates a single permission mapping one role to its supported operations.
        /// </summary>
        /// <param name="role">Name of the role to which defined permission applies.</param>
        /// <param name="operations">Either a mixed-type array of a string or an object
        /// that details what operations are allowed to related roles.
        /// In a simple case, the array members are one of the following:
        /// create, read, update, delete, *.
        /// The Operation.All (wildcard *) can be used to represent all options supported for that entity's source type.</param>
        public PermissionSetting(string role, object[] operations)
        {
            Role = role;
            Operations = operations;
        }
        [property: JsonPropertyName("role")]
        public string Role { get; }
        [property: JsonPropertyName("actions")]
        public object[] Operations { get; set; }
    }
}
