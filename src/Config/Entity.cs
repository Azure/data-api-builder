using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.Converters;

namespace Azure.DataApiBuilder.Config;

public enum EntityType
{
    Table,
    View,
    [EnumMember(Value = "stored-procedure")] StoredProcedure
}

/// <summary>
/// The operations supported by the service.
/// </summary>
public enum EntityActionOperation
{
    None,

    // *
    [EnumMember(Value = "*")] All,

    // Common Operations
    Delete, Read,

    // cosmosdb_nosql operations
    Upsert, Create,

    // Sql operations
    Insert, Update, UpdateGraphQL,

    // Additional
    UpsertIncremental, UpdateIncremental,

    // Only valid operation for stored procedures
    Execute
}

/// <summary>
/// A subset of the HTTP verb list that is supported by the REST endpoints within the service.
/// </summary>
public enum SupportedHttpVerb
{
    Get,
    Post,
    Put,
    Patch,
    Delete
}

public enum GraphQLOperation
{
    Query,
    Mutation
}

public enum Cardinality
{
    One,
    Many
}

public record EntitySource(string Object, EntityType Type, Dictionary<string, object> Parameters, string[] KeyFields);

[JsonConverter(typeof(EntityGraphQLOptionsConverter))]
public record EntityGraphQLOptions(string Singular, string Plural, bool Enabled = true, GraphQLOperation Operation = GraphQLOperation.Query);

[JsonConverter(typeof(EntityRestOptionsConverter))]
public record EntityRestOptions(string? Path, SupportedHttpVerb[] Methods, bool Enabled = true);
public record EntityActionFields(HashSet<string> Exclude, HashSet<string>? Include = null);
public record EntityActionPolicy(string Database);
public record EntityAction(EntityActionOperation Action, EntityActionFields Fields, EntityActionPolicy Policy)
{
    public static readonly HashSet<EntityActionOperation> ValidPermissionOperations = new() { EntityActionOperation.Create, EntityActionOperation.Read, EntityActionOperation.Update, EntityActionOperation.Delete };
    public static readonly HashSet<EntityActionOperation> ValidStoredProcedurePermissionOperations = new() { EntityActionOperation.Execute };
}
public record EntityPermission(string Role, EntityAction[] Actions);

public record EntityRelationship(
    Cardinality Cardinality,
    [property: JsonPropertyName("target.entity")] string TargetEntity,
    [property: JsonPropertyName("source.fields")] string[] SourceFields,
    [property: JsonPropertyName("target.fields")] string[] TargetFields,
    [property: JsonPropertyName("linking.object")] string? LinkingObject,
    [property: JsonPropertyName("linking.source.fields")] string[] LinkingSourceFields,
    [property: JsonPropertyName("linking.target.fields")] string[] LinkingTargetFields);

public record Entity(
    EntitySource Source,
    EntityGraphQLOptions GraphQL,
    EntityRestOptions Rest,
    EntityPermission[] Permissions,
    Dictionary<string, string> Mappings,
    Dictionary<string, EntityRelationship> Relationships);
