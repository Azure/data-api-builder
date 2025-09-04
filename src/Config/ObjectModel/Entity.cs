// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.HealthCheck;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Defines the Entities that are exposed.
/// </summary>
/// <param name="Health">Health check configuration for the entity.</param>
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
/// <param name="Health">Defines whether to enable comprehensive health check for the entity
/// and how many rows to return in query and under what threshold-ms.</param>
/// <param name="Description">Optional description for the entity. Used for API documentation and GraphQL schema comments.</param>
public record Entity
{
    public const string PROPERTY_PATH = "path";
    public const string PROPERTY_METHODS = "methods";
    public string? Description { get; init; }
    public EntitySource Source { get; init; }
    public EntityGraphQLOptions GraphQL { get; init; }
    public EntityRestOptions Rest { get; init; }
    public EntityPermission[] Permissions { get; init; }
    public Dictionary<string, string>? Mappings { get; init; }
    public Dictionary<string, EntityRelationship>? Relationships { get; init; }
    public EntityCacheOptions? Cache { get; init; }

    public EntityHealthCheckConfig? Health { get; init; }

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
        bool IsLinkingEntity = false,
        EntityHealthCheckConfig? Health = null,
        string? Description = null)
    {
        this.Health = Health;
        this.Source = Source;
        this.GraphQL = GraphQL;
        this.Rest = Rest;
        this.Permissions = Permissions;
        this.Mappings = Mappings;
        this.Relationships = Relationships;
        this.Cache = Cache;
        this.IsLinkingEntity = IsLinkingEntity;
        this.Description = Description;
    }

    /// <summary>
    /// Resolves the value of Entity.Cache property if present, default is false.
    /// Caching is enabled only when explicitly set to true.
    /// </summary>
    /// <returns>Whether caching is enabled for the entity.</returns>
    [JsonIgnore]
    [MemberNotNullWhen(true, nameof(Cache))]
    public bool IsCachingEnabled => Cache?.Enabled is true;

    [JsonIgnore]
    public bool IsEntityHealthEnabled =>
        Health is null || Health.Enabled;

    [JsonIgnore]
    public bool IsRestEnabled =>
        Rest is null || Rest.Enabled is true;

    [JsonIgnore]
    public bool IsGraphQLEnabled =>
        GraphQL is null || GraphQL.Enabled is true;

    [JsonIgnore]
    public int EntityThresholdMs
    {
        get
        {
            if (Health == null || Health?.ThresholdMs == null)
            {
                return HealthCheckConstants.DEFAULT_THRESHOLD_RESPONSE_TIME_MS;
            }
            else
            {
                return Health.ThresholdMs;
            }
        }
    }

    [JsonIgnore]
    public int EntityFirst
    {
        get
        {
            if (Health == null || Health?.First == null)
            {
                return HealthCheckConstants.DEFAULT_FIRST_VALUE;
            }
            else
            {
                return Health.First;
            }
        }
    }
}
