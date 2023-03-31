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

public record EntitySource(string Object, EntityType Type, Dictionary<string, object> Parameters, string[] KeyFields);

[JsonConverter(typeof(EntityGraphQLOptionsConverter))]
public record EntityGraphQLOptions(string? Singular = null, string? Plural = null, bool Enabled = true);

[JsonConverter(typeof(EntityRestOptionsConverter))]
public record EntityRestOptions(string? Path, string[] Methods, bool Enabled = true);

public record EntityActionFields(string[] Include, string[] Exclude);
public record EntityActionPolicy(string Database);
public record EntityAction(string Action, EntityActionFields Fields, EntityActionPolicy Policy);
public record EntityPermission(string Role, EntityAction[] Actions);

public record EntityRelationship(
    string Cardinality,
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
