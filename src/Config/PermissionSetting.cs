using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config
{
    /// <summary>
    /// Defines who (in terms of roles) can access the entity and using which operations.
    /// </summary>
    /// <param name="Role">Name of the role to which defined permission applies.</param>
    /// <param name="Operations">Either a mixed-type array of a string or an object
    /// that details what operations are allowed to related roles.
    /// In a simple case, the array members are one of the following:
    /// create, read, update, delete, *.
    /// The Operation.All (wildcard *) can be used to mean all the operations.</param>
    public class PermissionSetting
    {
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
