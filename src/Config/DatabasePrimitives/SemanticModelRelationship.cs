// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.DatabasePrimitives;

/// <summary>
/// Represents a relationship discovered from a semantic model via TMSCHEMA_RELATIONSHIPS.
/// </summary>
/// <param name="FromTable">The table name on the "from" side.</param>
/// <param name="FromColumn">The column name on the "from" side.</param>
/// <param name="FromCardinality">1=One, 2=Many.</param>
/// <param name="ToTable">The table name on the "to" side.</param>
/// <param name="ToColumn">The column name on the "to" side.</param>
/// <param name="ToCardinality">1=One, 2=Many.</param>
/// <param name="IsActive">Whether the relationship is active (only active relationships are used by DAX).</param>
/// <param name="CrossFilteringBehavior">1=SingleDirection, 2=BothDirections.</param>
public record SemanticModelRelationship(
    string FromTable,
    string FromColumn,
    int FromCardinality,
    string ToTable,
    string ToColumn,
    int ToCardinality,
    bool IsActive,
    int CrossFilteringBehavior);
