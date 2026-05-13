// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Describes GraphQL subscription settings specific to an entity.
/// </summary>
/// <param name="Enabled">Indicates if GraphQL subscriptions are enabled for the entity.</param>
/// <param name="Events">The enabled subscription events.</param>
public record EntityGraphQLSubscriptionOptions(bool Enabled = true, GraphQLSubscriptionEvent[]? Events = null)
{
    public GraphQLSubscriptionEvent[] Events { get; init; } = Events ?? Array.Empty<GraphQLSubscriptionEvent>();
}

