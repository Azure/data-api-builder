namespace Azure.DataGateway.Service.Models
{
    /// <summary>
    /// Constants representing the CRUD operations utilized in DataGateway
    /// These actions are defined in configuration and utilized in authorization.
    /// </summary>
    public static class ActionType
    {
        public const string CREATE = "create";
        public const string READ = "read";
        public const string UPDATE = "update";
        public const string DELETE = "delete";
    }
}
