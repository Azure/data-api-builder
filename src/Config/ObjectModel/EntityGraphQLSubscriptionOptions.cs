// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Describes GraphQL subscription settings specific to an entity.
/// </summary>
public record EntityGraphQLSubscriptionOptions
{
    /// <summary>
    /// Creates a new instance of <see cref="EntityGraphQLSubscriptionOptions"/>.
    /// </summary>
    /// <param name="Enabled">Indicates if GraphQL subscriptions are enabled for the entity.</param>
    /// <param name="Events">The enabled subscription events. Null values are normalized to an empty event array.</param>
    public EntityGraphQLSubscriptionOptions(bool Enabled = true, GraphQLSubscriptionEvent[]? Events = null)
    {
        this.Enabled = Enabled;
        this.Events = Events ?? Array.Empty<GraphQLSubscriptionEvent>();
    }

    public bool Enabled { get; init; }

    public GraphQLSubscriptionEvent[] Events { get; init; }
}
