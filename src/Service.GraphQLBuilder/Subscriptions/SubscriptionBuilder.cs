// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Directives;
using HotChocolate.Language;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLNaming;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLUtils;

namespace Azure.DataApiBuilder.Service.GraphQLBuilder.Subscriptions;

public static class SubscriptionBuilder
{
    public const string SUBSCRIPTION_EVENT_INTERFACE_NAME = "SubscriptionEvent";
    public const string EVENT_ID_FIELD_NAME = "eventId";
    public const string UTC_DATE_TIME_FIELD_NAME = "utcDateTime";
    public const string ACTOR_ROLE_FIELD_NAME = "actorRole";
    public const string RECORD_FIELD_NAME = "record";

    public static DocumentNode Build(
        DocumentNode root,
        RuntimeEntities entities,
        Dictionary<string, EntityMetadata>? entityPermissionsMap = null,
        IReadOnlyDictionary<string, DatabaseType>? entityToDatabaseType = null)
    {
        List<IDefinitionNode> definitionNodes = new();
        List<FieldDefinitionNode> subscriptionFields = new();

        foreach (IDefinitionNode definition in root.Definitions)
        {
            if (definition is not ObjectTypeDefinitionNode objectTypeDefinitionNode || !IsModelType(objectTypeDefinitionNode))
            {
                continue;
            }

            string entityName = ObjectTypeToEntityName(objectTypeDefinitionNode);
            Entity entity = entities[entityName];

            if (!IsSubscriptionEnabled(entity) || entity.Source.Type is EntitySourceType.StoredProcedure)
            {
                continue;
            }

            if (entityToDatabaseType is not null &&
                entityToDatabaseType.TryGetValue(entityName, out DatabaseType databaseType) &&
                databaseType is DatabaseType.CosmosDB_NoSQL)
            {
                continue;
            }

            foreach (GraphQLSubscriptionEvent subscriptionEvent in entity.GraphQL.Subscription!.Events.Distinct())
            {
                EntityActionOperation operation = ToPermissionOperation(subscriptionEvent);
                IEnumerable<string> rolesAllowed = IAuthorizationResolver.GetRolesForOperation(entityName, operation, entityPermissionsMap);

                if (!rolesAllowed.Any())
                {
                    continue;
                }

                string eventTypeName = GenerateEventTypeName(objectTypeDefinitionNode.Name.Value, subscriptionEvent);
                definitionNodes.Add(GenerateEventType(eventTypeName, objectTypeDefinitionNode.Name));
                subscriptionFields.Add(GenerateSubscriptionField(entityName, entity, eventTypeName, subscriptionEvent, rolesAllowed));
            }
        }

        if (subscriptionFields.Count is 0)
        {
            return new DocumentNode(Array.Empty<IDefinitionNode>());
        }

        definitionNodes.Insert(0, GenerateSubscriptionEventInterface());
        definitionNodes.Add(new ObjectTypeDefinitionNode(null, new NameNode("Subscription"), null, new List<DirectiveNode>(), new List<NamedTypeNode>(), subscriptionFields));

        return new DocumentNode(definitionNodes);
    }

    public static bool IsSubscriptionEnabled(Entity entity) =>
        entity.GraphQL.Enabled &&
        entity.GraphQL.Subscription is { Enabled: true, Events.Length: > 0 };

    public static string GenerateTopicName(string entityName, GraphQLSubscriptionEvent subscriptionEvent) => $"{entityName}:{subscriptionEvent}";

    public static string GenerateSubscriptionFieldName(string entityName, Entity entity, GraphQLSubscriptionEvent subscriptionEvent) =>
        $"{FormatNameForField(GetDefinedSingularName(entityName, entity))}{subscriptionEvent}";

    private static InterfaceTypeDefinitionNode GenerateSubscriptionEventInterface() =>
        new(
            location: null,
            name: new NameNode(SUBSCRIPTION_EVENT_INTERFACE_NAME),
            description: null,
            directives: new List<DirectiveNode>(),
            interfaces: new List<NamedTypeNode>(),
            fields: GenerateEventMetadataFields());

    private static ObjectTypeDefinitionNode GenerateEventType(string eventTypeName, NameNode recordTypeName) =>
        new(
            location: null,
            name: new NameNode(eventTypeName),
            description: null,
            directives: new List<DirectiveNode>(),
            interfaces: new List<NamedTypeNode> { new(new NameNode(SUBSCRIPTION_EVENT_INTERFACE_NAME)) },
            fields: GenerateEventMetadataFields()
                .Append(new FieldDefinitionNode(
                    location: null,
                    name: new NameNode(RECORD_FIELD_NAME),
                    description: null,
                    arguments: new List<InputValueDefinitionNode>(),
                    type: new NonNullTypeNode(new NamedTypeNode(recordTypeName)),
                    directives: new List<DirectiveNode>()))
                .ToList());

    private static List<FieldDefinitionNode> GenerateEventMetadataFields() =>
        new()
        {
            new FieldDefinitionNode(null, new NameNode(EVENT_ID_FIELD_NAME), null, new List<InputValueDefinitionNode>(), new NonNullTypeNode(new NamedTypeNode("UUID")), new List<DirectiveNode>()),
            new FieldDefinitionNode(null, new NameNode(UTC_DATE_TIME_FIELD_NAME), null, new List<InputValueDefinitionNode>(), new NonNullTypeNode(new NamedTypeNode("DateTime")), new List<DirectiveNode>()),
            new FieldDefinitionNode(null, new NameNode(ACTOR_ROLE_FIELD_NAME), null, new List<InputValueDefinitionNode>(), new NonNullTypeNode(new NamedTypeNode("String")), new List<DirectiveNode>())
        };

    private static FieldDefinitionNode GenerateSubscriptionField(
        string entityName,
        Entity entity,
        string eventTypeName,
        GraphQLSubscriptionEvent subscriptionEvent,
        IEnumerable<string> rolesAllowed)
    {
        List<DirectiveNode> directives = new();
        if (CreateAuthorizationDirectiveIfNecessary(rolesAllowed, out DirectiveNode? authorizeDirective))
        {
            directives.Add(authorizeDirective!);
        }

        directives.Add(new DirectiveNode(ModelDirective.Names.MODEL, new ArgumentNode(ModelDirective.Names.NAME_ARGUMENT, entityName)));

        return new FieldDefinitionNode(
            location: null,
            name: new NameNode(GenerateSubscriptionFieldName(entityName, entity, subscriptionEvent)),
            description: null,
            arguments: new List<InputValueDefinitionNode>(),
            type: new NamedTypeNode(eventTypeName),
            directives: directives);
    }

    private static string GenerateEventTypeName(string graphQLTypeName, GraphQLSubscriptionEvent subscriptionEvent) => $"{graphQLTypeName}{subscriptionEvent}Event";

    private static EntityActionOperation ToPermissionOperation(GraphQLSubscriptionEvent subscriptionEvent) =>
        subscriptionEvent switch
        {
            GraphQLSubscriptionEvent.Created => EntityActionOperation.Create,
            GraphQLSubscriptionEvent.Updated => EntityActionOperation.Update,
            GraphQLSubscriptionEvent.Deleted => EntityActionOperation.Delete,
            _ => EntityActionOperation.None
        };
}
