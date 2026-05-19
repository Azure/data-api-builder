// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Text.Json;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Telemetry;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Subscriptions;
using HotChocolate.Execution;
using HotChocolate.Subscriptions;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Core.Services;

public interface IGraphQLSubscriptionEventPublisher
{
    ValueTask<ISourceStream<JsonElement>> SubscribeAsync(string entityName, GraphQLSubscriptionEvent subscriptionEvent, CancellationToken cancellationToken);

    Task PublishAsync(string entityName, GraphQLSubscriptionEvent subscriptionEvent, string actorRole, JsonElement record, CancellationToken cancellationToken = default);

    Task PublishAsync(string entityName, GraphQLSubscriptionEvent subscriptionEvent, string actorRole, IReadOnlyDictionary<string, object?> record, CancellationToken cancellationToken = default);
}

public sealed class GraphQLSubscriptionEventPublisher : IGraphQLSubscriptionEventPublisher
{
    private readonly ITopicEventSender _sender;
    private readonly ITopicEventReceiver _receiver;
    private readonly ILogger<GraphQLSubscriptionEventPublisher> _logger;

    public GraphQLSubscriptionEventPublisher(
        ITopicEventSender sender,
        ITopicEventReceiver receiver,
        ILogger<GraphQLSubscriptionEventPublisher> logger)
    {
        _sender = sender;
        _receiver = receiver;
        _logger = logger;
    }

    public ValueTask<ISourceStream<JsonElement>> SubscribeAsync(string entityName, GraphQLSubscriptionEvent subscriptionEvent, CancellationToken cancellationToken) =>
        _receiver.SubscribeAsync<JsonElement>(SubscriptionBuilder.GenerateTopicName(entityName, subscriptionEvent), cancellationToken);

    public Task PublishAsync(string entityName, GraphQLSubscriptionEvent subscriptionEvent, string actorRole, IReadOnlyDictionary<string, object?> record, CancellationToken cancellationToken = default)
    {
        using JsonDocument document = JsonSerializer.SerializeToDocument(record);
        return PublishAsync(entityName, subscriptionEvent, actorRole, document.RootElement.Clone(), cancellationToken);
    }

    public async Task PublishAsync(string entityName, GraphQLSubscriptionEvent subscriptionEvent, string actorRole, JsonElement record, CancellationToken cancellationToken = default)
    {
        Guid eventId = Guid.NewGuid();
        using JsonDocument payload = JsonSerializer.SerializeToDocument(new
        {
            eventId,
            utcDateTime = DateTimeOffset.UtcNow,
            actorRole,
            record
        });

        using Activity? activity = TelemetryTracesHelper.DABActivitySource.StartActivity("graphql.subscription.publish");
        activity?.SetTag("dab.entity", entityName);
        activity?.SetTag("dab.graphql.subscription.event", subscriptionEvent.ToString());
        activity?.SetTag("dab.actor.role", actorRole);
        activity?.SetTag("dab.graphql.subscription.event_id", eventId);
        activity?.SetTag("dab.graphql.subscription.publish_success", true);

        try
        {
            await _sender.SendAsync(
                SubscriptionBuilder.GenerateTopicName(entityName, subscriptionEvent),
                payload.RootElement.Clone(),
                cancellationToken);
        }
        catch (Exception ex)
        {
            activity?.SetTag("dab.graphql.subscription.publish_success", false);
            _logger.LogWarning(
                ex,
                "GraphQL subscription event publish failed for entity {EntityName}, event {EventType}, actor role {ActorRole}, eventId {EventId}",
                entityName,
                subscriptionEvent,
                actorRole,
                eventId);
            return;
        }

        _logger.LogInformation(
            "GraphQL subscription event published for entity {EntityName}, event {EventType}, actor role {ActorRole}, eventId {EventId}",
            entityName,
            subscriptionEvent,
            actorRole,
            eventId);
    }
}

public sealed class NullGraphQLSubscriptionEventPublisher : IGraphQLSubscriptionEventPublisher
{
    public static readonly NullGraphQLSubscriptionEventPublisher Instance = new();

    private NullGraphQLSubscriptionEventPublisher()
    {
    }

    public ValueTask<ISourceStream<JsonElement>> SubscribeAsync(string entityName, GraphQLSubscriptionEvent subscriptionEvent, CancellationToken cancellationToken) =>
        throw new NotSupportedException("GraphQL subscriptions are not configured.");

    public Task PublishAsync(string entityName, GraphQLSubscriptionEvent subscriptionEvent, string actorRole, JsonElement record, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task PublishAsync(string entityName, GraphQLSubscriptionEvent subscriptionEvent, string actorRole, IReadOnlyDictionary<string, object?> record, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
