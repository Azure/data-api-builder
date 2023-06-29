// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Defines which Actions (Create, Read, Update, Delete, Execute) are permitted for a given role.
/// </summary>
/// <param name="Role">Name of the role to which defined permission applies.</param>
/// <param name="Actions">An array of what can be performed against the entity for the actions.
/// This can be written in JSON using shorthand notation, or as a full object, with a custom <c>JsonConverter</c> to convert that into the .NET type.</param>
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
public record EntityPermission(string Role, EntityAction[] Actions);
#pragma warning restore CA1711 // Identifiers should not have incorrect suffix
