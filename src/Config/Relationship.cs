// **************************************
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
//
// @file: Relationship.cs
// **************************************

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config
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
    /// This only represents the right (target, e.g. books) side of the relationship
    /// when viewing the enclosing entity as the left (source, e.g. publisher) side.
    /// e.g. publisher can publish "Many" books.
    /// To get the cardinality of the other side, the runtime needs to flip the sides
    /// and find the cardinality of the original source (e.g. publisher)
    /// is with respect to the original target (e.g. books):
    /// e.g. book can have only "One" publisher.
    /// Hence, its a Many-To-One relationship from publisher-books
    /// i.e. a One-Many relationship from books-publisher.
    /// The various combinations of relationships this leads to are:
    /// (1) One-To-One (2) Many-One (3) One-To-Many (4) Many-To-Many.
    /// </summary>
    public enum Cardinality
    {
        One,
        Many
    }
}
