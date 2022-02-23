using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace Azure.DataGateway.Service.Models
{
    /// <summary>
    /// The operations supported by the service.
    /// </summary>
    public enum Operation
    {
        None,
        // Common Operations
        Find, Delete,

        // Cosmos operations
        Upsert, Create,

        // Sql operations
        Insert, Update,

        // Additional
        UpsertIncremental
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

        public static OperationAuthorizationRequirement DELETE =
            new() { Name = nameof(DELETE) };

        public static OperationAuthorizationRequirement PUT =
            new() { Name = nameof(PUT) };

        public static OperationAuthorizationRequirement PATCH =
            new() { Name = nameof(PATCH) };
    }
}

