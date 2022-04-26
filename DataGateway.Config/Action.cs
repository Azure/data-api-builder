using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace Azure.DataGateway.Config
{
    /// <summary>
    /// A detailed version of the action describing what policy to apply
    /// and fields to include and/or exclude.
    /// </summary>
    /// <param name="Name">What kind of action is allowed.</param>
    /// <param name="Policy">Details about item-level security rules.</param>
    /// <param name="Fields">Details what fields to include or exclude</param>
    public record Action(
        [property: JsonPropertyName("action")]
        string Name,
        Policy? Policy,
        Field? Fields);

    /// <summary>
    /// The operations supported by the service.
    /// </summary>
    public enum Operation
    {
        None,
        // *
        All,
        // Common Operations
        Find, Delete,

        // Cosmos operations
        Upsert, Create,

        // Sql operations
        Insert, Update,

        // Additional
        UpsertIncremental, UpdateIncremental
    }

    /// <summary>
    /// Details about what fields to include or exclude.
    /// Exclusions have precedence over inclusions.
    /// The * can be used as the wildcard character to indicate all fields.
    /// </summary>
    /// <param name="Include">All the fields specified here are included.</param>
    /// <param name="Exclude">All the fields specified here are excluded.</param>
    public record Field(
        string[]? Include,
        string[]? Exclude);

    /// <summary>
    /// Details the item-level security rules.
    /// </summary> 
    /// <param name="Request">A rule to be checked before
    /// sending any request to the database.</param>
    /// <param name="Database">An OData style filter rule
    /// (predicate) that will be injected in the query sent to the database.</param>
    public record Policy(
        string? Request,
        string? Database);

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

        public static OperationAuthorizationRequirement
            GetVerb(string action) => action switch
            {
                "create" => POST,
                "read" => GET,
                "update" => PATCH,
                "delete" => DELETE,
                _ => throw new NotSupportedException($"{action} is not supported.");
            };
    }
}
