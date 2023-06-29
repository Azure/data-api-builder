// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Defines the Entities that are exposed.
/// </summary>
/// <param name="Source">The underlying database object to which the exposed entity is connected to.</param>
/// <param name="Rest">The JSON may represent this as a bool or a string and we use a custom <c>JsonConverter</c> to convert that into the .NET type.</param>
/// <param name="GraphQL">The JSON may represent this as a bool or a string and we use a custom <c>JsonConverter</c> to convert that into the .NET type.</param>
/// <param name="Permissions">Permissions assigned to this entity.</param>
/// <param name="Relationships">Defines how an entity is related to other exposed
/// entities and optionally provides details on what underlying database
/// objects can be used to support such relationships.</param>
/// <param name="Mappings">Defines mappings between database fields and GraphQL and REST fields.</param>
public record Entity(
    EntitySource Source,
    EntityGraphQLOptions GraphQL,
    EntityRestOptions Rest,
    EntityPermission[] Permissions,
    Dictionary<string, string>? Mappings,
    Dictionary<string, EntityRelationship>? Relationships)
{
    public const string PROPERTY_PATH = "path";
    public const string PROPERTY_METHODS = "methods";
}
