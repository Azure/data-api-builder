using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace Azure.DataGateway.Service.Models
{
    /// <summary>
    /// The operations supported by the service.
    /// </summary>
    public enum Operation
    {
        // Common Operations
        Find, Delete,

        // Cosmos operations
        Upsert, Create,

        // Sql operations
        Insert
    }

    /// <summary>
    /// The REST HttpVerbs supported by the service
    /// expressed as authorization requirements.
    /// </summary>
    public static class HttpRestVerbs
    {
        public static OperationAuthorizationRequirement POST =
            new() { Name = nameof(POST) };

        public static OperationAuthorizationRequirement GET =
            new() { Name = nameof(GET) };
    }
}

