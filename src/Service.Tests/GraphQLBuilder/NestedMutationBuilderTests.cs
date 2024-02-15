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
    /// <summary>
    /// Parent class containing tests to validate different aspects of schema generation for nested mutations for different type of
    /// relational database flavours supported by DAB.
    /// </summary>
    [TestClass]
    public abstract class NestedMutationBuilderTests
    {
        // Stores the type of database - MsSql, MySql, PgSql, DwSql. Currently nested mutations are only supported for MsSql.
        protected static string databaseEngine;
        private static RuntimeConfig _runtimeConfig;
        private static DocumentNode _objectsNode;
        private static DocumentNode _mutationsNode;
        private static IEnumerable<IHasName> _objectDefinitions;

        #region Test setup
        public static async Task InitializeAsync()
        {
            (GraphQLSchemaCreator schemaCreator, _runtimeConfig) = await SetUpGQLSchemaCreatorAndConfig(databaseEngine);
            (_objectsNode, Dictionary<string, InputObjectTypeDefinitionNode> inputTypes) = schemaCreator.GenerateGraphQLObjects();
            _objectDefinitions = _objectsNode.Definitions.Where(d => d is IHasName).Cast<IHasName>();
            (_, _mutationsNode) = schemaCreator.GenerateQueryAndMutationNodes(_objectsNode, inputTypes);
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
        #endregion
        #region Nested Create tests

        /// <summary>
        /// Test to validate that we don't expose the object definitions inferred for linking entity/table to the end user as that is an information
        /// leak. These linking object definitions are only used to generate the final source->target linking object definitions for entities
        /// having an M:N relationship between them.
        /// </summary>
        /// <param name="sourceEntityName">Name of the source entity for which the configuration is provided in the config.</param>
        /// <param name="targetEntityName">Name of the target entity which is related to the source entity via a relationship defined in the 'relationships'
        /// section in the configuration of the source entity.</param>
        [DataTestMethod]
        [DataRow("Book", "Author", DisplayName = "Validate absence of linking object for Book->Author M:N relationship")]
        [DataRow("Author", "Book", DisplayName = "Validate absence of linking object for Author->Book M:N relationship")]
        [DataRow("BookNF", "AuthorNF", DisplayName = "Validate absence of linking object for BookNF->AuthorNF M:N relationship")]
        [DataRow("AuthorNF", "BookNF", DisplayName = "Validate absence of linking object for AuthorNF->BookNF M:N relationship")]
        public void ValidateAbsenceOfLinkingObjectDefinitionsInObjectsNodeForMNRelationships(string sourceEntityName, string targetEntityName)
        {
            string linkingEntityName = Entity.GenerateLinkingEntityName(sourceEntityName, targetEntityName);
            ObjectTypeDefinitionNode linkingObjectTypeDefinitionNode = GetObjectTypeDefinitionNode(new NameNode(linkingEntityName));

            // Assert that object definition for linking entity/table is null here.
            // The object definition being null here implies that the object definition is not exposed in the objects node.
            Assert.IsNull(linkingObjectTypeDefinitionNode);
        }

        /// <summary>
        /// Test to validate that that we create a source -> target linking object definition for every pair of (source, target) entities which
        /// are related via an M:N relationship.
        /// </summary>
        /// <param name="sourceEntityName">Name of the source entity for which the configuration is provided in the config.</param>
        /// <param name="targetEntityName">Name of the target entity which is related to the source entity via a relationship defined in the 'relationships'
        /// section in the configuration of the source entity.</param>
        [DataTestMethod]
        [DataRow("Book", "Author", DisplayName = "Validate presence of source->target linking object for Book->Author M:N relationship")]
        [DataRow("Author", "Book", DisplayName = "Validate presence of source->target linking object for Author->Book M:N relationship")]
        [DataRow("BookNF", "AuthorNF", DisplayName = "Validate presence of source->target linking object for BookNF->AuthorNF M:N relationship")]
        [DataRow("AuthorNF", "BookNF", DisplayName = "Validate presence of source->target linking object for AuthorNF->BookNF M:N relationship")]
        public void ValidatePresenceOfSourceTargetLinkingObjectDefinitionsInObjectsNodeForMNRelationships(string sourceEntityName, string targetEntityName)
        {
            NameNode sourceTargetLinkingNodeName = new(GenerateLinkingNodeName(
                        GetDefinedSingularName(sourceEntityName, _runtimeConfig.Entities[sourceEntityName]),
                        GetDefinedSingularName(targetEntityName, _runtimeConfig.Entities[targetEntityName])));
            ObjectTypeDefinitionNode sourceTargetLinkingObjectTypeDefinitionNode = GetObjectTypeDefinitionNode(sourceTargetLinkingNodeName);

            // Validate that we have indeed inferred the object type definition for the source->target linking object.
            Assert.IsNotNull(sourceTargetLinkingObjectTypeDefinitionNode);
        }

        /// <summary>
        /// Test to validate that we add a Foriegn key directive to the list of directives for every column in an entity/table,
        /// which holds a foreign key reference to some other entity in the config.
        /// </summary>
        /// <param name="referencingEntityName">Name of the referencing entity.</param>
        /// <param name="referencingColumns">List of referencing columns.</param>
        [DataTestMethod]
        [DataRow("Book", new string[] { "publisher_id" },
            DisplayName = "Validate FK directive for referencing columns in Book entity for Book->Publisher relationship.")]
        [DataRow("Review", new string[] { "book_id" },
            DisplayName = "Validate FK directive for referencing columns in Review entity for Review->Book relationship.")]
        [DataRow("stocks_price", new string[] { "categoryid", "pieceid" },
            DisplayName = "Validate FK directive for referencing columns in stocks_price entity for stocks_price->Stock relationship.")]
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

        /// <summary>
        /// Test to validate that both create one, and create multiple mutations are created for entities.
        /// </summary>
        [DataTestMethod]
        [DataRow("Book", DisplayName = "Validate creation of create one and create multiple mutations for Book entity.")]
        [DataRow("Publisher", DisplayName = "Validate creation of create one and create multiple mutations for Publisher entity.")]
        [DataRow("Stock", DisplayName = "Validate creation of create one and create multiple mutations for Stock entity.")]
        public void ValidateCreationOfPointAndMultipleCreateMutations(string entityName)
        {
            string createOneMutationName = CreateMutationBuilder.GetPointCreateMutationNodeName(entityName, _runtimeConfig.Entities[entityName]);
            string createMultipleMutationName = CreateMutationBuilder.GetMultipleCreateMutationNodeName(entityName, _runtimeConfig.Entities[entityName]);

            ObjectTypeDefinitionNode mutationObjectDefinition = (ObjectTypeDefinitionNode)_mutationsNode.Definitions
                .Where(d => d is IHasName).Cast<IHasName>()
                .FirstOrDefault(d => d.Name.Value == "Mutation");

            // The index of create one mutation not being equal to -1 indicates that we successfully created the mutation.
            int indexOfCreateOneMutationField = mutationObjectDefinition.Fields.ToList().FindIndex(f => f.Name.Value.Equals(createOneMutationName));
            Assert.AreNotEqual(-1, indexOfCreateOneMutationField);

            // The index of create multiple mutation not being equal to -1 indicates that we successfully created the mutation.
            int indexOfCreateMultipleMutationField = mutationObjectDefinition.Fields.ToList().FindIndex(f => f.Name.Value.Equals(createMultipleMutationName));
            Assert.AreNotEqual(-1, indexOfCreateMultipleMutationField);
        }

        /// <summary>
        /// Test to validate that in addition to column fields, relationship fields are also processed for creating the 'create' input object types.
        /// This test validates that in the create'' input object type for the entity:
        /// 1. A relationship field is created for every relationship defined in the 'relationships' section of the entity.
        /// 2. The type of the relationship field is nullable. This ensures that we don't mandate the end user to provide input for relationship fields.
        /// 3. For relationships with cardinality 'Many', the relationship field type is a list type - to allow creating multiple records in the target entity.
        /// For relationships with cardinality 'One', the relationship field type should not be a list type (and hence should be an object type).
        /// </summary>
        /// <param name="entityName">Name of the entity.</param>
        [DataTestMethod]
        [DataRow("Book", DisplayName = "Validate relationship fields in the input type for Book entity.")]
        [DataRow("Publisher", DisplayName = "Validate relationship fields in the input type for Publisher entity.")]
        [DataRow("Stock", DisplayName = "Validate relationship fields in the input type for Stock entity.")]
        public void ValidateRelationshipFieldsInInputType(string entityName)
        {
            Entity entity = _runtimeConfig.Entities[entityName];
            NameNode inputTypeName = CreateMutationBuilder.GenerateInputTypeName(GetDefinedSingularName(entityName, entity));
            InputObjectTypeDefinitionNode inputObjectTypeDefinition = (InputObjectTypeDefinitionNode)_mutationsNode.Definitions
                .Where(d => d is IHasName).Cast<IHasName>().
                FirstOrDefault(d => d.Name.Value.Equals(inputTypeName.Value));
            List<InputValueDefinitionNode> inputFields = inputObjectTypeDefinition.Fields.ToList();
            HashSet<string> inputFieldNames = new(inputObjectTypeDefinition.Fields.Select(field => field.Name.Value));
            foreach ((string relationshipName, EntityRelationship relationship) in entity.Relationships)
            {
                // Assert that the input type for the entity contains a field for the relationship.
                Assert.AreEqual(true, inputFieldNames.Contains(relationshipName));

                int indexOfRelationshipField = inputFields.FindIndex(field => field.Name.Value.Equals(relationshipName));
                InputValueDefinitionNode inputValueDefinitionNode = inputFields[indexOfRelationshipField];

                // Assert that the field should be of nullable type as providing input for relationship fields is optional.
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

        /// <summary>
        /// Test to validate that for entities having an M:N relationship between them, we create a source->target linking input type.
        /// </summary>
        /// <param name="sourceEntityName">Name of the source entity for which the configuration is provided in the config.</param>
        /// <param name="targetEntityName">Name of the target entity which is related to the source entity via a relationship defined in the 'relationships'
        /// section in the configuration of the source entity.</param>
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
            InputObjectTypeDefinitionNode inputObjectTypeDefinition = (InputObjectTypeDefinitionNode)_mutationsNode.Definitions
                .Where(d => d is IHasName).Cast<IHasName>().
                FirstOrDefault(d => d.Name.Value.Equals(inputTypeNameForBook.Value));
            NameNode inputTypeName = CreateMutationBuilder.GenerateInputTypeName(GenerateLinkingNodeName(
                GetDefinedSingularName(sourceEntityName, _runtimeConfig.Entities[sourceEntityName]),
                GetDefinedSingularName(targetEntityName, _runtimeConfig.Entities[targetEntityName])));
            List<InputValueDefinitionNode> inputFields = inputObjectTypeDefinition.Fields.ToList();
            int indexOfRelationshipField = inputFields.FindIndex(field => field.Type.InnerType().NamedType().Name.Value.Equals(inputTypeName.Value));
            InputValueDefinitionNode inputValueDefinitionNode = inputFields[indexOfRelationshipField];
        }

        /// <summary>
        /// Test to validate that in the 'create' input type for an entity, all the columns from the entity which hold a foreign key reference to
        /// some other entity in the config are of nullable type. Making the FK referencing columns nullable allows the user to not specify them.
        /// In such a case, for a valid mutation request, the value for these referencing columns is derived from the insertion in the referenced entity.
        /// </summary>
        /// <param name="referencingEntityName">Name of the referencing entity.</param>
        /// <param name="referencingColumns">List of referencing columns.</param>
        [DataTestMethod]
        [DataRow("Book", new string[] { "publisher_id" }, DisplayName = "Validate nullability of referencing columns in Book entity for Book->Publisher relationship.")]
        [DataRow("Review", new string[] { "book_id" }, DisplayName = "Validate nullability of referencing columns in Review entity for Review->Book relationship.")]
        [DataRow("stocks_price", new string[] { "categoryid", "pieceid" }, DisplayName = "Validate nullability of referencing columns in stocks_price entity for stocks_price->Stock relationship.")]
        public void ValidateNullabilityOfReferencingColumnsInInputType(string referencingEntityName, string[] referencingColumns)
        {
            Entity entity = _runtimeConfig.Entities[referencingEntityName];
            NameNode inputTypeName = CreateMutationBuilder.GenerateInputTypeName(GetDefinedSingularName(referencingEntityName, entity));
            InputObjectTypeDefinitionNode inputObjectTypeDefinition = (InputObjectTypeDefinitionNode)_mutationsNode.Definitions
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
        #endregion

        #region Helpers
        private static ObjectTypeDefinitionNode GetObjectTypeDefinitionNode(NameNode sourceTargetLinkingNodeName)
        {
            IHasName definition = _objectDefinitions.FirstOrDefault(d => d.Name.Value == sourceTargetLinkingNodeName.Value);
            return definition is ObjectTypeDefinitionNode objectTypeDefinitionNode ? objectTypeDefinitionNode : null;
        }
        #endregion
    }
}
