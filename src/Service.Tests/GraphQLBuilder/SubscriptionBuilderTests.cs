// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Directives;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Subscriptions;
using HotChocolate.Language;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.GraphQLBuilder
{
    [TestClass]
    public class SubscriptionBuilderTests
    {
        [TestMethod]
        public void BuildReturnsNoDefinitionsWhenNoEventsConfigured()
        {
            DocumentNode result = SubscriptionBuilder.Build(CreateRoot(), CreateEntities(subscription: null), CreatePermissions(EntityActionOperation.Create));

            Assert.AreEqual(0, result.Definitions.Count);
        }

        [TestMethod]
        public void BuildReturnsNoDefinitionsWhenSubscriptionDisabled()
        {
            DocumentNode result = SubscriptionBuilder.Build(
                CreateRoot(),
                CreateEntities(new EntityGraphQLSubscriptionOptions(enabled: false, events: new[] { GraphQLSubscriptionEvent.Created })),
                CreatePermissions(EntityActionOperation.Create));

            Assert.AreEqual(0, result.Definitions.Count);
        }

        [TestMethod]
        public void BuildReturnsOnlyConfiguredEventFields()
        {
            DocumentNode result = SubscriptionBuilder.Build(
                CreateRoot(),
                CreateEntities(new EntityGraphQLSubscriptionOptions(events: new[] { GraphQLSubscriptionEvent.Updated })),
                CreatePermissions(EntityActionOperation.Update));

            ObjectTypeDefinitionNode subscription = result.Definitions.OfType<ObjectTypeDefinitionNode>().Single(node => node.Name.Value == "Subscription");

            Assert.AreEqual(1, subscription.Fields.Count);
            Assert.AreEqual("actorUpdated", subscription.Fields[0].Name.Value);
            Assert.IsTrue(result.Definitions.OfType<ObjectTypeDefinitionNode>().Any(node => node.Name.Value == "ActorUpdatedEvent"));
            Assert.IsTrue(result.Definitions.OfType<InterfaceTypeDefinitionNode>().Any(node => node.Name.Value == "SubscriptionEvent"));
        }

        [TestMethod]
        public void BuildOmitsFieldsWithoutMatchingPermission()
        {
            DocumentNode result = SubscriptionBuilder.Build(
                CreateRoot(),
                CreateEntities(new EntityGraphQLSubscriptionOptions(events: new[] { GraphQLSubscriptionEvent.Deleted })),
                CreatePermissions(EntityActionOperation.Update));

            Assert.AreEqual(0, result.Definitions.Count);
        }

        [TestMethod]
        public void BuildOmitsFieldsForCosmosEntities()
        {
            DocumentNode result = SubscriptionBuilder.Build(
                CreateRoot(),
                CreateEntities(new EntityGraphQLSubscriptionOptions(events: new[] { GraphQLSubscriptionEvent.Created })),
                CreatePermissions(EntityActionOperation.Create),
                new Dictionary<string, DatabaseType> { ["Actor"] = DatabaseType.CosmosDB_NoSQL });

            Assert.AreEqual(0, result.Definitions.Count);
        }

        private static DocumentNode CreateRoot()
        {
            ObjectTypeDefinitionNode actor = new(
                location: null,
                name: new NameNode("Actor"),
                description: null,
                directives: new List<DirectiveNode>
                {
                    new(ModelDirective.Names.MODEL, new ArgumentNode(ModelDirective.Names.NAME_ARGUMENT, "Actor"))
                },
                interfaces: new List<NamedTypeNode>(),
                fields: new List<FieldDefinitionNode>
                {
                    new(null, new NameNode("id"), null, new List<InputValueDefinitionNode>(), new NamedTypeNode("Int"), new List<DirectiveNode>())
                });

            return new DocumentNode(new List<IDefinitionNode> { actor });
        }

        private static RuntimeEntities CreateEntities(EntityGraphQLSubscriptionOptions subscription)
        {
            Entity entity = new(
                Source: new EntitySource("dbo.Actor", EntitySourceType.Table, Parameters: null, KeyFields: null),
                GraphQL: new EntityGraphQLOptions("Actor", "Actors", Subscription: subscription),
                Fields: null,
                Rest: new EntityRestOptions(),
                Permissions: System.Array.Empty<EntityPermission>(),
                Mappings: null,
                Relationships: null);

            return new RuntimeEntities(new ReadOnlyDictionary<string, Entity>(new Dictionary<string, Entity> { ["Actor"] = entity }));
        }

        private static Dictionary<string, EntityMetadata> CreatePermissions(EntityActionOperation operation) =>
            new()
            {
                ["Actor"] = new EntityMetadata
                {
                    OperationToRolesMap = new Dictionary<EntityActionOperation, List<string>>
                    {
                        [operation] = new() { "anonymous" }
                    }
                }
            };
    }
}
