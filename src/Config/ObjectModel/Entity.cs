// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

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
/// <param name="Cache">Defines whether to allow caching for a read operation's response and
/// how long that response should be valid in the cache.</param>
public record Entity
{
    public const string PROPERTY_PATH = "path";
    public const string PROPERTY_METHODS = "methods";

    public EntitySource Source { get; init; }
    public EntityGraphQLOptions GraphQL { get; init; }
    public EntityRestOptions Rest { get; init; }
    public EntityPermission[] Permissions { get; init; }
    public Dictionary<string, string>? Mappings { get; init; }
    public Dictionary<string, EntityRelationship>? Relationships { get; init; }
    public EntityCacheOptions? Cache { get; init; }

    [JsonIgnore]
    public bool IsLinkingEntity { get; init; }

    [JsonConstructor]
    public Entity(
        EntitySource Source,
        EntityGraphQLOptions GraphQL,
        EntityRestOptions Rest,
        EntityPermission[] Permissions,
        Dictionary<string, string>? Mappings,
        Dictionary<string, EntityRelationship>? Relationships,
        EntityCacheOptions? Cache = null,
        bool IsLinkingEntity = false)
    {
        this.Source = Source;
        this.GraphQL = GraphQL;
        this.Rest = Rest;
        this.Permissions = Permissions;
        this.Mappings = Mappings;
        this.Relationships = Relationships;
        this.Cache = Cache;
        this.IsLinkingEntity = IsLinkingEntity;
    }

    /// <summary>
    /// Resolves the value of Entity.Cache property if present, default is false.
    /// Caching is enabled only when explicitly set to true.
    /// </summary>
    /// <returns>Whether caching is enabled for the entity.</returns>
    [JsonIgnore]
    [MemberNotNullWhen(true, nameof(Cache))]
    public bool IsCachingEnabled =>
        Cache is not null &&
        Cache.Enabled is not null &&
        Cache.Enabled is true;
}
