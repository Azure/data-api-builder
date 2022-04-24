namespace Azure.DataGateway.Config
{
    /// <summary>
    /// Defines who (in terms of roles) can access the entity and using which actions.
    /// </summary>
    /// <param name="Role">Name of the role to which defined permission applies.</param>
    /// <param name="Actions">Either a mixed-type array of a string or an object
    /// that details what actions are allowed to related roles.
    /// In a simple case, the array members are one of the following:
    /// *, create, read, update, delete </param>
    public record PermissionSettings(
        string Role,
        object[] Actions);
}
