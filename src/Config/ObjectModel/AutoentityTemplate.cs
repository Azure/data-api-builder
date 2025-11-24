// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Template used by auto-entities to configure all entities it generates.
/// </summary>
/// <param name="Mcp">MCP endpoint configuration</param>
/// <param name="Rest">REST endpoint configuration</param>
/// <param name="GraphQL">GraphQL endpoint configuration</param>
/// <param name="Health">Health check configuration</param>
/// <param name="Cache">Cache configuration</param>
public record AutoentityTemplate
{
    // TODO: Will add Mcp variable once MCP is supported at an entity level
    // public EntityMcpOptions? Mcp { get; init; }
    public EntityRestOptions Rest { get; init; }
    public EntityGraphQLOptions GraphQL { get; init; }
    public EntityHealthCheckConfig Health { get; init; }
    public EntityCacheOptions Cache { get; init; }

    [JsonConstructor]
    public AutoentityTemplate(
        EntityRestOptions? Rest = null,
        EntityGraphQLOptions? GraphQL = null,
        EntityHealthCheckConfig? Health = null,
        EntityCacheOptions? Cache = null)
    {
        if (Rest is not null)
        {
            this.Rest = Rest;
            UserProvidedRestOptions = true;
        }
        else
        {
            this.Rest = new EntityRestOptions();
        }

        if (GraphQL is not null)
        {
            this.GraphQL = GraphQL;
            UserProvidedGraphQLOptions = true;
        }
        else
        {
            this.GraphQL = new EntityGraphQLOptions(string.Empty, string.Empty);
        }

        if (Health is not null)
        {
            this.Health = Health;
            UserProvidedHealthOptions = true;
        }
        else
        {
            this.Health = new EntityHealthCheckConfig();
        }

        if (Cache is not null)
        {
            this.Cache = Cache;
            UserProvidedCacheOptions = true;
        }
        else
        {
            this.Cache = new EntityCacheOptions(Enabled: true);
        }
    }

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write rest
    /// property and value to the runtime config file.
    /// When user doesn't provide the rest property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// This is because the user's intent is to use DAB's default value which could change
    /// and DAB CLI writing the property and value would lose the user's intent.
    /// This is because if the user were to use the CLI created config, a rest
    /// property/value specified would be interpreted by DAB as "user explicitly set rest."
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(Rest))]
    public bool UserProvidedRestOptions { get; init; } = false;

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write graphql
    /// property and value to the runtime config file.
    /// When user doesn't provide the graphql property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// This is because the user's intent is to use DAB's default value which could change
    /// and DAB CLI writing the property and value would lose the user's intent.
    /// This is because if the user were to use the CLI created config, a graphql
    /// property/value specified would be interpreted by DAB as "user explicitly set graphql."
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(GraphQL))]
    public bool UserProvidedGraphQLOptions { get; init; } = false;

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write health
    /// property and value to the runtime config file.
    /// When user doesn't provide the health property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// This is because the user's intent is to use DAB's default value which could change
    /// and DAB CLI writing the property and value would lose the user's intent.
    /// This is because if the user were to use the CLI created config, a health
    /// property/value specified would be interpreted by DAB as "user explicitly set health."
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(Health))]
    public bool UserProvidedHealthOptions { get; init; } = false;

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write cache
    /// property and value to the runtime config file.
    /// When user doesn't provide the cache property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// This is because the user's intent is to use DAB's default value which could change
    /// and DAB CLI writing the property and value would lose the user's intent.
    /// This is because if the user were to use the CLI created config, a cache
    /// property/value specified would be interpreted by DAB as "user explicitly set cache."
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(Cache))]
    public bool UserProvidedCacheOptions { get; init; } = false;
}
