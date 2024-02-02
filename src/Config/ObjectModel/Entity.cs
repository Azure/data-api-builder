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

    // String used as a prefix for the name of a linking entity.
    private const string LINKING_ENTITY_PREFIX = "LinkingEntity";

    // Delimiter used to separate linking entity prefix/source entity name/target entity name, in the name of a linking entity.
    private const string ENTITY_NAME_DELIMITER = "$";

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

    /// <summary>
    /// Helper method to generate the linking entity name using the source and target entity names.
    /// </summary>
    /// <param name="source">Source entity name.</param>
    /// <param name="target">Target entity name.</param>
    /// <returns>Name of the linking entity.</returns>
    public static string GenerateLinkingEntityName(string source, string target)
    {
        return LINKING_ENTITY_PREFIX + ENTITY_NAME_DELIMITER + source + ENTITY_NAME_DELIMITER + target;
    }

    /// <summary>
    /// Helper method to decode the names of source and target entities from the name of a linking entity.
    /// </summary>
    public static Tuple<string, string> GetSourceAndTargetEntityNameFromLinkingEntityName(string linkingEntityName)
    {
        if (!linkingEntityName.StartsWith(LINKING_ENTITY_PREFIX+ENTITY_NAME_DELIMITER))
        {
            throw new Exception("The provided entity name is an invalid linking entity name.");
        }

        string entityNameWithLinkingEntityPrefix = linkingEntityName.Substring(LINKING_ENTITY_PREFIX.Length + ENTITY_NAME_DELIMITER.Length);
        string[] sourceTargetEntityNames = entityNameWithLinkingEntityPrefix.Split(ENTITY_NAME_DELIMITER, StringSplitOptions.RemoveEmptyEntries);

        if (sourceTargetEntityNames.Length != 2)
        {
            throw new Exception("The provided entity name is an invalid linking entity name.");
        }

        return new(sourceTargetEntityNames[0], sourceTargetEntityNames[1]);
    }
}
