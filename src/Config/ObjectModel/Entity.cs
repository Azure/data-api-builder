// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record Entity(
    EntitySource Source,
    EntityGraphQLOptions GraphQL,
    EntityRestOptions Rest,
    EntityPermission[] Permissions,
    Dictionary<string, string>? Mappings,
    Dictionary<string, EntityRelationship>? Relationships);
