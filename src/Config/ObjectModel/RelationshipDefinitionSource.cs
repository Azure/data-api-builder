// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Identifies the source of the relationship.
/// </summary>
public enum RelationshipDefinitionSource
{
    /// <summary>
    /// Relationship defined in config either:
    /// - exclusively
    /// - overrides database foreign key definition.
    /// </summary>
    Config,
    /// <summary>
    /// Relationship defined in and resolved from
    /// a database foreign key definition.
    /// </summary>
    DatabaseSchema
}
