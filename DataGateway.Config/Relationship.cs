using System.Text.Json.Serialization;

namespace Azure.DataGateway.Config
{
    /// <summary>
    /// Defines the relationships between entities.
    /// </summary>
    /// <param name="Cardinality">The cardinality of the target entity.</param>
    /// <param name="TargetEntity">Another exposed entity to which the source
    /// entity relates to.</param>
    /// <param name="SourceFields">Can be used to designate which columns
    /// to be used in the source entity.</param>

    /// <param name="TargetFields">Can be used to designate which columns
    /// to be used in the target entity we connect to.</param>

    /// <param name="LinkingObject">Database object that is used in the backend
    /// database to support an M:N relationship.</param>

    /// <param name="LinkingSourceFields">Database fields in the linking object that
    /// will be used to connect to the related item in the source entity.</param>

    /// <param name="LinkingTargetFields">Database fields in the linking object that
    /// will be used to connect to the related item in the target entity.</param>
    public record Relationship(
        Cardinality Cardinality,
        [property: JsonPropertyName("target.entity")]
        string TargetEntity,
        [property: JsonPropertyName("source.fields")]
        string[]? SourceFields,
        [property: JsonPropertyName("target.fields")]
        string[]? TargetFields,
        [property: JsonPropertyName("linking.object")]
        string? LinkingObject,
        [property: JsonPropertyName("linking.source.fields")]
        string[]? LinkingSourceFields,
        [property: JsonPropertyName("linking.target.fields")]
        string[]? LinkingTargetFields);

    /// <summary>
    /// Kinds of relationship cardinality.
    /// </summary>
    public enum Cardinality
    {
        One,
        Many
    }
}
