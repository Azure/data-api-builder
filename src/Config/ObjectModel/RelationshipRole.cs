// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Denotes the role of a referencing or referenced table in a relationship.
/// Default value is None because the role is only resolved to help DAB
/// create join predicates for self-referencing relationships.
/// In non self-join relationships, DAB uses the ForeignKeyDefinition.RelationShipPair
/// to determine the referencing/referenced entity. RelationShipPair isn't sufficient
/// for self-join relationships because the DatabaseObjects used to represent the pair
/// reference the same object: e.g. both Referenced/Referencing entity would point to 'dbo.MyTable'
/// </summary>
public enum RelationshipRole
{
    None,
    Source,
    Target,
    Linking
}
