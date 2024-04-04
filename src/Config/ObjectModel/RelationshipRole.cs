// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Denotes the role of a referencing or referenced table in a relationship.
/// Default value is None because the role is only resolved in some scenarios
/// such as self-referencing relationships.
/// </summary>
public enum RelationshipRole
{
    None,
    Source,
    Target,
    Linking
}
