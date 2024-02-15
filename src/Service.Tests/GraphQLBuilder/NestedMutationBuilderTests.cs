// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Core.Models;
using HotChocolate.Language;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Azure.DataApiBuilder.Core.Services.Cache;
using ZiggyCreatures.Caching.Fusion;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Config.ObjectModel;
using System;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLNaming;
using System.Linq;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Directives;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Mutations;

namespace Azure.DataApiBuilder.Service.Tests.GraphQLBuilder
{
    [TestClass]
    public class NestedMutationBuilderTests
    {
        private static RuntimeConfig _runtimeConfig;
        private static DocumentNode _documentNode;
        //private static DocumentNode _mutationNode;
        private static IEnumerable<IHasName> _objectDefinitions;
        private static DocumentNode _mutationNode;
        //private static Dictionary<string, InputObjectTypeDefinitionNode> _inputTypeObjects;

        [ClassInitialize]
        public static async Task SetUpAsync(TestContext context)
        {
            (GraphQLSchemaCreator schemaCreator, _runtimeConfig) = await SetUpGQLSchemaCreatorAndConfig("MsSql");
            (_documentNode, Dictionary<string, InputObjectTypeDefinitionNode> inputTypes) = schemaCreator.GenerateGraphQLObjects();
            _objectDefinitions = _documentNode.Definitions.Where(d => d is IHasName).Cast<IHasName>();
            (_, _mutationNode) = schemaCreator.GenerateQueryAndMutationNodes(_documentNode, inputTypes);
        }

        [DataTestMethod]
        [DataRow("Book", "Author", DisplayName = "Validate absence of linking object for Book->Author M:N relationship")]
        [DataRow("Author", "Book", DisplayName = "Validate absence of linking object for Author->Book M:N relationship")]
        [DataRow("BookNF", "AuthorNF", DisplayName = "Validate absence of linking object for BookNF->AuthorNF M:N relationship")]
        [DataRow("AuthorNF", "BookNF", DisplayName = "Validate absence of linking object for AuthorNF->BookNF M:N relationship")]
        public void ValidateAbsenceOfLinkingObjectDefinitionsInDocumentNodeForMNRelationships(string sourceEntityName, string targetEntityName)
        {
            string linkingEntityName = Entity.GenerateLinkingEntityName(sourceEntityName, targetEntityName);

            // Validate that the document node does not expose the object type definition for linking table.
            ObjectTypeDefinitionNode linkingObjectTypeDefinitionNode = GetObjectTypeDefinitionNode(new NameNode(linkingEntityName));
            Assert.IsNull(linkingObjectTypeDefinitionNode);
        }

        [DataTestMethod]
        [DataRow("Book", "Author", DisplayName = "Validate presence of source->target linking object for Book->Author M:N relationship")]
        [DataRow("Author", "Book", DisplayName = "Validate presence of source->target linking object for Author->Book M:N relationship")]
        [DataRow("BookNF", "AuthorNF", DisplayName = "Validate presence of source->target linking object for BookNF->AuthorNF M:N relationship")]
        [DataRow("AuthorNF", "BookNF", DisplayName = "Validate presence of source->target linking object for AuthorNF->BookNF M:N relationship")]
        public void ValidatePresenceOfSourceTargetLinkingObjectDefinitionsInDocumentNodeForMNRelationships(string sourceEntityName, string targetEntityName)
        {
            // Validate that we have indeed inferred the object type definition for all the source->target linking objects.
            NameNode sourceTargetLinkingNodeName = new(GenerateLinkingNodeName(
                        GetDefinedSingularName(sourceEntityName, _runtimeConfig.Entities[sourceEntityName]),
                        GetDefinedSingularName(targetEntityName, _runtimeConfig.Entities[targetEntityName])));
            ObjectTypeDefinitionNode sourceTargetLinkingObjectTypeDefinitionNode = GetObjectTypeDefinitionNode(sourceTargetLinkingNodeName);
            Assert.IsNotNull(sourceTargetLinkingObjectTypeDefinitionNode);

        }

        [DataTestMethod]
        [DataRow("Book", new string[] { "publisher_id" })]
        [DataRow("Review", new string[] { "book_id" })]
        [DataRow("stocks_price", new string[] { "categoryid", "pieceid" })]
        public void ValidatePresenceOfOneForeignKeyDirectiveOnReferencingColumns(string referencingEntityName, string[] referencingColumns)
        {
            ObjectTypeDefinitionNode objectTypeDefinitionNode = GetObjectTypeDefinitionNode(
                new NameNode(GetDefinedSingularName(
                   entityName: referencingEntityName,
                   configEntity: _runtimeConfig.Entities[referencingEntityName])));
            List<FieldDefinitionNode> fieldsInObjectDefinitionNode = objectTypeDefinitionNode.Fields.ToList();
            foreach (string referencingColumn in referencingColumns)
            {
                int indexOfReferencingField = fieldsInObjectDefinitionNode.FindIndex((field => field.Name.Value.Equals(referencingColumn)));
                FieldDefinitionNode referencingFieldDefinition = fieldsInObjectDefinitionNode[indexOfReferencingField];
                int countOfFkDirectives = referencingFieldDefinition.Directives.Where(directive => directive.Name.Value == ForeignKeyDirectiveType.DirectiveName).Count();
                // The presence of 1 FK directive indicates:
                // 1. The foreign key dependency was successfully inferred from the metadata.
                // 2. The FK directive was added only once. When a relationship between two entities is defined in the configuration of both the entities,
                // we want to ensure that we don't unnecessarily add the FK directive twice for the referencing fields.
                Assert.AreEqual(1, countOfFkDirectives);
            }
        }

        [DataTestMethod]
        [DataRow("Book")]
        [DataRow("Publisher")]
        [DataRow("Stock")]
        public void ValidateCreationOfPointAndMultipleCreateMutations(string entityName)
        {
            string createOneMutationName = CreateMutationBuilder.GetPointCreateMutationNodeName(entityName, _runtimeConfig.Entities[entityName]);
            string createMultipleMutationName = CreateMutationBuilder.GetMultipleCreateMutationNodeName(entityName, _runtimeConfig.Entities[entityName]);

            ObjectTypeDefinitionNode mutationObjectDefinition = (ObjectTypeDefinitionNode)_mutationNode.Definitions
                .Where(d => d is IHasName).Cast<IHasName>()
                .FirstOrDefault(d => d.Name.Value == "Mutation");

            // The index of create one mutation not being equal to -1 indicates that we successfully created the mutation.
            int indexOfCreateOneMutationField = mutationObjectDefinition.Fields.ToList().FindIndex(f => f.Name.Value.Equals(createOneMutationName));
            Assert.AreNotEqual(-1, indexOfCreateOneMutationField);

            // The index of create multiple mutation not being equal to -1 indicates that we successfully created the mutation.
            int indexOfCreateMultipleMutationField = mutationObjectDefinition.Fields.ToList().FindIndex(f => f.Name.Value.Equals(createMultipleMutationName));
            Assert.AreNotEqual(-1, indexOfCreateMultipleMutationField);
        }

        [DataTestMethod]
        [DataRow("Book")]
        [DataRow("Publisher")]
        [DataRow("Stock")]
        public void ValidateRelationshipFieldsInInput(string entityName)
        {
            Entity entity = _runtimeConfig.Entities[entityName];
            NameNode inputTypeName = CreateMutationBuilder.GenerateInputTypeName(GetDefinedSingularName(entityName, entity));
            InputObjectTypeDefinitionNode inputObjectTypeDefinition = (InputObjectTypeDefinitionNode)_mutationNode.Definitions
                .Where(d => d is IHasName).Cast<IHasName>().
                FirstOrDefault(d => d.Name.Value.Equals(inputTypeName.Value));
            List<InputValueDefinitionNode> inputFields = inputObjectTypeDefinition.Fields.ToList();
            HashSet<string> inputFieldNames = new(inputObjectTypeDefinition.Fields.Select(field => field.Name.Value));
            foreach ((string relationshipName, EntityRelationship relationship) in entity.Relationships)
            {
                Assert.AreEqual(true, inputFieldNames.Contains(relationshipName));

                int indexOfRelationshipField = inputFields.FindIndex(field => field.Name.Value.Equals(relationshipName));
                InputValueDefinitionNode inputValueDefinitionNode = inputFields[indexOfRelationshipField];

                // The field should be of nullable type as providing input for relationship fields is optional.
                Assert.AreEqual(true, !inputValueDefinitionNode.Type.IsNonNullType());

                if (relationship.Cardinality is Cardinality.Many)
                {
                    // For relationship with cardinality as 'Many', assert that we create a list input type.
                    Assert.AreEqual(true, inputValueDefinitionNode.Type.IsListType());
                }
                else
                {
                    // For relationship with cardinality as 'One', assert that we don't create a list type,
                    // but an object type.
                    Assert.AreEqual(false, inputValueDefinitionNode.Type.IsListType());
                }
            }
        }

        [DataTestMethod]
        [DataRow("Book", "Author", DisplayName = "Validate creation of source->target linking input object for Book->Author M:N relationship")]
        [DataRow("Author", "Book", DisplayName = "Validate creation of source->target linking input object for Author->Book M:N relationship")]
        [DataRow("BookNF", "AuthorNF", DisplayName = "Validate creation of source->target linking input object for BookNF->AuthorNF M:N relationship")]
        [DataRow("AuthorNF", "BookNF", DisplayName = "Validate creation of source->target linking input object for AuthorNF->BookNF M:N relationship")]
        public void ValidateCreationOfSourceTargetLinkingInputForMNRelationship(string sourceEntityName, string targetEntityName)
        {
            NameNode inputTypeNameForBook = CreateMutationBuilder.GenerateInputTypeName(GetDefinedSingularName(
                sourceEntityName,
                _runtimeConfig.Entities[sourceEntityName]));
            Entity entity = _runtimeConfig.Entities[sourceEntityName];
            InputObjectTypeDefinitionNode inputObjectTypeDefinition = (InputObjectTypeDefinitionNode)_mutationNode.Definitions
                .Where(d => d is IHasName).Cast<IHasName>().
                FirstOrDefault(d => d.Name.Value.Equals(inputTypeNameForBook.Value));
            NameNode inputTypeName = CreateMutationBuilder.GenerateInputTypeName(GenerateLinkingNodeName(
                GetDefinedSingularName(sourceEntityName, _runtimeConfig.Entities[sourceEntityName]),
                GetDefinedSingularName(targetEntityName, _runtimeConfig.Entities[targetEntityName])));
            List<InputValueDefinitionNode> inputFields = inputObjectTypeDefinition.Fields.ToList();
            int indexOfRelationshipField = inputFields.FindIndex(field => field.Type.InnerType().NamedType().Name.Value.Equals(inputTypeName.Value));
            InputValueDefinitionNode inputValueDefinitionNode = inputFields[indexOfRelationshipField];
        }

        [DataTestMethod]
        [DataRow("Book", new string[] { "publisher_id" })]
        [DataRow("Review", new string[] { "book_id" })]
        [DataRow("stocks_price", new string[] { "categoryid", "pieceid" })]
        public void ValidateNullabilityOfReferencingColumnsInInput(string referencingEntityName, string[] referencingColumns)
        {
            Entity entity = _runtimeConfig.Entities[referencingEntityName];
            NameNode inputTypeName = CreateMutationBuilder.GenerateInputTypeName(GetDefinedSingularName(referencingEntityName, entity));
            InputObjectTypeDefinitionNode inputObjectTypeDefinition = (InputObjectTypeDefinitionNode)_mutationNode.Definitions
                .Where(d => d is IHasName).Cast<IHasName>().
                FirstOrDefault(d => d.Name.Value.Equals(inputTypeName.Value));
            List<InputValueDefinitionNode> inputFields = inputObjectTypeDefinition.Fields.ToList();
            foreach (string referencingColumn in referencingColumns)
            {
                int indexOfReferencingColumn = inputFields.FindIndex(field => field.Name.Value.Equals(referencingColumn));
                InputValueDefinitionNode inputValueDefinitionNode = inputFields[indexOfReferencingColumn];

                // The field should be of nullable type as providing input for referencing fields is optional.
                Assert.AreEqual(true, !inputValueDefinitionNode.Type.IsNonNullType());
            }
        }

        private static ObjectTypeDefinitionNode GetObjectTypeDefinitionNode(NameNode sourceTargetLinkingNodeName)
        {
            IHasName definition = _objectDefinitions.FirstOrDefault(d => d.Name.Value == sourceTargetLinkingNodeName.Value);
            return definition is ObjectTypeDefinitionNode objectTypeDefinitionNode ? objectTypeDefinitionNode : null;
        }

        private static async Task<Tuple<GraphQLSchemaCreator, RuntimeConfig>> SetUpGQLSchemaCreatorAndConfig(string databaseType)
        {
            string fileContents = await File.ReadAllTextAsync($"dab-config.{databaseType}.json");

            IFileSystem fs = new MockFileSystem(new Dictionary<string, MockFileData>()
            {
                { "dab-config.json", new MockFileData(fileContents) }
            });

            FileSystemRuntimeConfigLoader loader = new(fs);
            RuntimeConfigProvider provider = new(loader);
            RuntimeConfig runtimeConfig = provider.GetConfig();
            Mock<IHttpContextAccessor> httpContextAccessor = new();
            Mock<ILogger<IQueryExecutor>> qMlogger = new();
            Mock<IFusionCache> cache = new();
            DabCacheService cacheService = new(cache.Object, logger: null, httpContextAccessor.Object);
            IAbstractQueryManagerFactory queryManagerfactory = new QueryManagerFactory(provider, qMlogger.Object, httpContextAccessor.Object);
            Mock<ILogger<ISqlMetadataProvider>> metadatProviderLogger = new();
            IMetadataProviderFactory metadataProviderFactory = new MetadataProviderFactory(provider, queryManagerfactory, metadatProviderLogger.Object, fs);
            await metadataProviderFactory.InitializeAsync();
            GQLFilterParser graphQLFilterParser = new(provider, metadataProviderFactory);
            IAuthorizationResolver authzResolver = new AuthorizationResolver(provider, metadataProviderFactory);
            Mock<ILogger<IQueryEngine>> queryEngineLogger = new();
            IQueryEngineFactory queryEngineFactory = new QueryEngineFactory(
                runtimeConfigProvider: provider,
                queryManagerFactory: queryManagerfactory,
                metadataProviderFactory: metadataProviderFactory,
                cosmosClientProvider: null,
                contextAccessor: httpContextAccessor.Object,
                authorizationResolver: authzResolver,
                gQLFilterParser: graphQLFilterParser,
                logger: queryEngineLogger.Object,
                cache: cacheService);
            Mock<IMutationEngineFactory> mutationEngineFactory = new();

            return new(new GraphQLSchemaCreator(provider, queryEngineFactory, mutationEngineFactory.Object, metadataProviderFactory, authzResolver), runtimeConfig);
        }
    }
}
