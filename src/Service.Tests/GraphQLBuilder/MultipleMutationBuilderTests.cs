// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.Cache;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Service.GraphQLBuilder;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Directives;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Mutations;
using HotChocolate.Language;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using ZiggyCreatures.Caching.Fusion;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLNaming;

namespace Azure.DataApiBuilder.Service.Tests.GraphQLBuilder
{
    /// <summary>
    /// Parent class containing tests to validate different aspects of schema generation for multiple mutations for different types of
    /// relational database flavours supported by DAB. All the tests in the class validate the side effect of the GraphQL schema created
    /// as a result of the execution of the InitializeAsync method.
    /// </summary>
    [TestClass]
    public abstract class MultipleMutationBuilderTests
    {
        // Stores the type of database - MsSql, MySql, PgSql, DwSql. Currently multiple mutations are only supported for MsSql.
        protected static string databaseEngine;

        // Stores mutation definitions for entities.
        private static IEnumerable<IHasName> _mutationDefinitions;

        // Stores object definitions for entities.
        private static IEnumerable<IHasName> _objectDefinitions;

        // Runtime config instance.
        private static RuntimeConfig _runtimeConfig;

        #region Multiple Create tests

        /// <summary>
        /// Test to validate that we don't expose the object definitions inferred for linking entity/table to the end user as that is an information
        /// leak. These linking object definitions are only used to generate the final source->target linking object definitions for entities
        /// having an M:N relationship between them.
        /// </summary>
        [TestMethod]
        public void ValidateAbsenceOfLinkingObjectDefinitionsInObjectsNodeForMNRelationships()
        {
            // Name of the source entity for which the configuration is provided in the config.
            string sourceEntityName = "Book";

            // Name of the target entity which is related to the source entity via a relationship defined in the 'relationships'
            // section in the configuration of the source entity.
            string targetEntityName = "Author";
            string linkingEntityName = GraphQLUtils.GenerateLinkingEntityName(sourceEntityName, targetEntityName);
            ObjectTypeDefinitionNode linkingObjectTypeDefinitionNode = GetObjectTypeDefinitionNode(linkingEntityName);

            // Validate absence of linking object for Book->Author M:N relationship.
            // The object definition being null here implies that the object definition is not exposed in the objects node.
            Assert.IsNull(linkingObjectTypeDefinitionNode);
        }

        /// <summary>
        /// Test to validate the functionality of GraphQLSchemaCreator.GenerateSourceTargetLinkingObjectDefinitions() and to ensure that
        /// we create a source -> target linking object definition for every pair of (source, target) entities which
        /// are related via an M:N relationship.
        /// </summary>
        [TestMethod]
        public void ValidatePresenceOfSourceTargetLinkingObjectDefinitionsInObjectsNodeForMNRelationships()
        {
            // Name of the source entity for which the configuration is provided in the config.
            string sourceEntityName = "Book";

            // Name of the target entity which is related to the source entity via a relationship defined in the 'relationships'
            // section in the configuration of the source entity.
            string targetEntityName = "Author";
            string sourceTargetLinkingNodeName = GenerateLinkingNodeName(
                        GetDefinedSingularName(sourceEntityName, _runtimeConfig.Entities[sourceEntityName]),
                        GetDefinedSingularName(targetEntityName, _runtimeConfig.Entities[targetEntityName]));
            ObjectTypeDefinitionNode sourceTargetLinkingObjectTypeDefinitionNode = GetObjectTypeDefinitionNode(sourceTargetLinkingNodeName);

            // Validate presence of source->target linking object for Book->Author M:N relationship.
            Assert.IsNotNull(sourceTargetLinkingObjectTypeDefinitionNode);
        }

        /// <summary>
        /// Test to validate that we add a referencing field directive to the list of directives for every column in an entity/table,
        /// which is a referencing field to another field in any entity in the config.
        /// </summary>
        [TestMethod]
        public void ValidatePresenceOfOneReferencingFieldDirectiveOnReferencingColumns()
        {
            // Name of the referencing entity.
            string referencingEntityName = "Book";

            // List of referencing columns.
            string[] referencingColumns = new string[] { "publisher_id" };
            ObjectTypeDefinitionNode objectTypeDefinitionNode = GetObjectTypeDefinitionNode(
                GetDefinedSingularName(
                    entityName: referencingEntityName,
                    configEntity: _runtimeConfig.Entities[referencingEntityName]));
            List<FieldDefinitionNode> fieldsInObjectDefinitionNode = objectTypeDefinitionNode.Fields.ToList();
            foreach (string referencingColumn in referencingColumns)
            {
                int indexOfReferencingField = fieldsInObjectDefinitionNode.FindIndex((field => field.Name.Value.Equals(referencingColumn)));
                FieldDefinitionNode referencingFieldDefinition = fieldsInObjectDefinitionNode[indexOfReferencingField];
                int countOfReferencingFieldDirectives = referencingFieldDefinition.Directives.Where(directive => directive.Name.Value == ReferencingFieldDirectiveType.DirectiveName).Count();
                // The presence of 1 referencing field directive indicates:
                // 1. The foreign key dependency was successfully inferred from the metadata.
                // 2. The referencing field directive was added only once. When a relationship between two entities is defined in the configuration of both the entities,
                // we want to ensure that we don't unnecessarily add the referencing field directive twice for the referencing fields.
                Assert.AreEqual(1, countOfReferencingFieldDirectives);
            }
        }

        /// <summary>
        /// Test to validate that we don't erroneously add a referencing field directive to the list of directives for every column in an entity/table,
        /// which is not a referencing field to another field in any entity in the config.
        /// </summary>
        [TestMethod]
        public void ValidateAbsenceOfReferencingFieldDirectiveOnNonReferencingColumns()
        {
            // Name of the referencing entity.
            string referencingEntityName = "stocks_price";

            // List of expected referencing columns.
            HashSet<string> expectedReferencingColumns = new() { "categoryid", "pieceid" };
            ObjectTypeDefinitionNode actualObjectTypeDefinitionNode = GetObjectTypeDefinitionNode(
                GetDefinedSingularName(
                    entityName: referencingEntityName,
                    configEntity: _runtimeConfig.Entities[referencingEntityName]));
            List<FieldDefinitionNode> actualFieldsInObjectDefinitionNode = actualObjectTypeDefinitionNode.Fields.ToList();
            foreach (FieldDefinitionNode fieldInObjectDefinitionNode in actualFieldsInObjectDefinitionNode)
            {
                if (!expectedReferencingColumns.Contains(fieldInObjectDefinitionNode.Name.Value))
                {
                    int countOfReferencingFieldDirectives = fieldInObjectDefinitionNode.Directives.Where(directive => directive.Name.Value == ReferencingFieldDirectiveType.DirectiveName).Count();
                    Assert.AreEqual(0, countOfReferencingFieldDirectives, message: "Scalar fields should not have referencing field directives.");
                }
            }
        }

        /// <summary>
        /// Test to validate that both create one, and create multiple mutations are created for entities.
        /// </summary>
        [TestMethod]
        public void ValidateCreationOfPointAndMultipleCreateMutations()
        {
            string entityName = "Publisher";
            string createOneMutationName = CreateMutationBuilder.GetPointCreateMutationNodeName(entityName, _runtimeConfig.Entities[entityName]);
            string createMultipleMutationName = CreateMutationBuilder.GetMultipleCreateMutationNodeName(entityName, _runtimeConfig.Entities[entityName]);

            ObjectTypeDefinitionNode mutationObjectDefinition = (ObjectTypeDefinitionNode)_mutationDefinitions.FirstOrDefault(d => d.Name.Value == "Mutation");

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
        /// 2. The type of the relationship field (which represents input for the target entity) is nullable.
        /// This ensures that providing input for relationship fields is optional.
        /// 3. For relationships with cardinality (for target entity) as 'Many', the relationship field type is a list type -
        /// to allow creating multiple records in the target entity. For relationships with cardinality 'One',
        /// the relationship field type should not be a list type (and hence should be an object type).
        /// </summary>
        [TestMethod]
        public void ValidateRelationshipFieldsInInputType()
        {
            string entityName = "Book";
            Entity entity = _runtimeConfig.Entities[entityName];
            NameNode inputTypeName = CreateMutationBuilder.GenerateInputTypeName(GetDefinedSingularName(entityName, entity));
            InputObjectTypeDefinitionNode inputObjectTypeDefinition = (InputObjectTypeDefinitionNode)_mutationDefinitions.FirstOrDefault(d => d.Name.Value.Equals(inputTypeName.Value));
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
        [TestMethod]
        public void ValidateCreationOfSourceTargetLinkingInputForMNRelationship()
        {
            // Name of the source entity for which the configuration is provided in the config.
            string sourceEntityName = "Book";

            // Name of the target entity which is related to the source entity via a relationship defined in the 'relationships'
            // section in the configuration of the source entity.
            string targetEntityName = "Author";

            NameNode inputTypeNameForBook = CreateMutationBuilder.GenerateInputTypeName(GetDefinedSingularName(
                sourceEntityName,
                _runtimeConfig.Entities[sourceEntityName]));
            InputObjectTypeDefinitionNode inputObjectTypeDefinition = (InputObjectTypeDefinitionNode)_mutationDefinitions.FirstOrDefault(d => d.Name.Value.Equals(inputTypeNameForBook.Value));
            NameNode inputTypeName = CreateMutationBuilder.GenerateInputTypeName(GenerateLinkingNodeName(
                GetDefinedSingularName(sourceEntityName, _runtimeConfig.Entities[sourceEntityName]),
                GetDefinedSingularName(targetEntityName, _runtimeConfig.Entities[targetEntityName])));
            List<InputValueDefinitionNode> inputFields = inputObjectTypeDefinition.Fields.ToList();
            int indexOfRelationshipField = inputFields.FindIndex(field => field.Type.InnerType().NamedType().Name.Value.Equals(inputTypeName.Value));

            // Validate creation of source->target linking input object for Book->Author M:N relationship
            Assert.AreNotEqual(-1, indexOfRelationshipField);
        }

        /// <summary>
        /// Test to validate that the linking input types generated for a source->target relationship contains input fields for:
        /// 1. All the fields belonging to the target entity, and
        /// 2. All the non-relationship fields in the linking entity.
        /// </summary>
        [TestMethod]
        public void ValidateInputForMNRelationship()
        {
            // Name of the source entity for which the configuration is provided in the config.
            string sourceEntityName = "Book";

            // Name of the target entity which is related to the source entity via a relationship defined in the 'relationships'
            // section in the configuration of the source entity.
            string targetEntityName = "Author";
            string linkingObjectFieldName = "royalty_percentage";
            string sourceNodeName = GetDefinedSingularName(sourceEntityName, _runtimeConfig.Entities[sourceEntityName]);
            string targetNodeName = GetDefinedSingularName(targetEntityName, _runtimeConfig.Entities[targetEntityName]);

            // Get input object definition for target entity.
            NameNode targetInputTypeName = CreateMutationBuilder.GenerateInputTypeName(targetNodeName);
            InputObjectTypeDefinitionNode targetInputObjectTypeDefinitionNode = (InputObjectTypeDefinitionNode)_mutationDefinitions.FirstOrDefault(d => d.Name.Value.Equals(targetInputTypeName.Value));

            // Get input object definition for source->target linking node.
            NameNode sourceTargetLinkingInputTypeName = CreateMutationBuilder.GenerateInputTypeName(GenerateLinkingNodeName(sourceNodeName, targetNodeName));
            InputObjectTypeDefinitionNode sourceTargetLinkingInputObjectTypeDefinition = (InputObjectTypeDefinitionNode)_mutationDefinitions.FirstOrDefault(d => d.Name.Value.Equals(sourceTargetLinkingInputTypeName.Value));

            // Collect all input field names in the source->target linking node input object definition.
            HashSet<string> inputFieldNamesInSourceTargetLinkingInput = new(sourceTargetLinkingInputObjectTypeDefinition.Fields.Select(field => field.Name.Value));

            // Assert that all the fields from the target input definition are present in the source->target linking input definition.
            foreach (InputValueDefinitionNode targetInputValueField in targetInputObjectTypeDefinitionNode.Fields)
            {
                Assert.AreEqual(true, inputFieldNamesInSourceTargetLinkingInput.Contains(targetInputValueField.Name.Value));
            }

            // Assert that the fields ('royalty_percentage') from linking object (i.e. book_author_link) is also
            // present in the input fields for the source>target linking input definition.
            Assert.AreEqual(true, inputFieldNamesInSourceTargetLinkingInput.Contains(linkingObjectFieldName));
        }

        /// <summary>
        /// Test to validate that in the 'create' input type for an entity, all the columns from the entity which hold a foreign key reference to
        /// some other entity in the config are of nullable type. Making the FK referencing columns nullable allows the user to not specify them.
        /// In such a case, for a valid mutation request, the value for these referencing columns is derived from the insertion in the referenced entity.
        /// </summary>
        [TestMethod]
        public void ValidateNullabilityOfReferencingColumnsInInputType()
        {
            string referencingEntityName = "Book";

            // Relationship: books.publisher_id -> publishers.id
            string[] referencingColumns = new string[] { "publisher_id" };
            Entity entity = _runtimeConfig.Entities[referencingEntityName];
            NameNode inputTypeName = CreateMutationBuilder.GenerateInputTypeName(GetDefinedSingularName(referencingEntityName, entity));
            InputObjectTypeDefinitionNode inputObjectTypeDefinition = (InputObjectTypeDefinitionNode)_mutationDefinitions.FirstOrDefault(d => d.Name.Value.Equals(inputTypeName.Value));
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

        /// <summary>
        /// Given a node name (singular name for an entity), returns the object definition created for the node.
        /// </summary>
        private static ObjectTypeDefinitionNode GetObjectTypeDefinitionNode(string nodeName)
        {
            IHasName definition = _objectDefinitions.FirstOrDefault(d => d.Name.Value == nodeName);
            return definition is ObjectTypeDefinitionNode objectTypeDefinitionNode ? objectTypeDefinitionNode : null;
        }
        #endregion

        #region Test setup

        /// <summary>
        /// Initializes the class variables to be used throughout the tests.
        /// </summary>
        public static async Task InitializeAsync()
        {
            // Setup runtime config.
            RuntimeConfigProvider runtimeConfigProvider = GetRuntimeConfigProvider();
            _runtimeConfig = runtimeConfigProvider.GetConfig();

            // Collect object definitions for entities.
            GraphQLSchemaCreator schemaCreator = await GetGQLSchemaCreator(runtimeConfigProvider);
            (DocumentNode objectsNode, Dictionary<string, InputObjectTypeDefinitionNode> inputTypes) = schemaCreator.GenerateGraphQLObjects();
            _objectDefinitions = objectsNode.Definitions.Where(d => d is IHasName).Cast<IHasName>();

            // Collect mutation definitions for entities.
            (_, DocumentNode mutationsNode) = schemaCreator.GenerateQueryAndMutationNodes(objectsNode, inputTypes);
            _mutationDefinitions = mutationsNode.Definitions.Where(d => d is IHasName).Cast<IHasName>();
        }

        /// <summary>
        /// Sets up and returns a runtime config provider instance.
        /// </summary>
        private static RuntimeConfigProvider GetRuntimeConfigProvider()
        {
            TestHelper.SetupDatabaseEnvironment(databaseEngine);
            FileSystemRuntimeConfigLoader configPath = TestHelper.GetRuntimeConfigLoader();
            RuntimeConfigProvider provider = new(configPath);

            RuntimeConfig runtimeConfig = provider.GetConfig();

            // Enabling multiple create operation because all the validations in this test file are specific
            // to multiple create operation.
            runtimeConfig = runtimeConfig with
            {
                Runtime = new RuntimeOptions(Rest: runtimeConfig.Runtime.Rest,
                                                                GraphQL: new GraphQLRuntimeOptions(MultipleMutationOptions: new MultipleMutationOptions(new MultipleCreateOptions(enabled: true))),
                                                                Host: runtimeConfig.Runtime.Host,
                                                                BaseRoute: runtimeConfig.Runtime.BaseRoute,
                                                                Telemetry: runtimeConfig.Runtime.Telemetry,
                                                                Cache: runtimeConfig.Runtime.Cache)
            };

            // For testing different aspects of schema generation for multiple create operation, we need to create a RuntimeConfigProvider object which contains a RuntimeConfig object
            // with the multiple create operation enabled.
            // So, another RuntimeConfigProvider object is created with the modified runtimeConfig and returned.
            System.IO.Abstractions.TestingHelpers.MockFileSystem fileSystem = new();
            fileSystem.AddFile(FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME, runtimeConfig.ToJson());
            FileSystemRuntimeConfigLoader loader = new(fileSystem);
            RuntimeConfigProvider runtimeConfigProvider = new(loader);
            return runtimeConfigProvider;
        }

        /// <summary>
        /// Sets up and returns a GraphQL schema creator instance.
        /// </summary>
        private static async Task<GraphQLSchemaCreator> GetGQLSchemaCreator(RuntimeConfigProvider runtimeConfigProvider)
        {
            // Setup mock loggers.
            Mock<IHttpContextAccessor> httpContextAccessor = new();
            Mock<ILogger<IQueryExecutor>> executorLogger = new();
            Mock<ILogger<ISqlMetadataProvider>> metadatProviderLogger = new();
            Mock<ILogger<IQueryEngine>> queryEngineLogger = new();

            // Setup mock cache and cache service.
            Mock<IFusionCache> cache = new();
            DabCacheService cacheService = new(cache: cache.Object, logger: null, httpContextAccessor: httpContextAccessor.Object);

            // Setup query manager factory.
            IAbstractQueryManagerFactory queryManagerfactory = new QueryManagerFactory(
                runtimeConfigProvider: runtimeConfigProvider,
                logger: executorLogger.Object,
                contextAccessor: httpContextAccessor.Object,
                handler: null);

            // Setup metadata provider factory.
            IMetadataProviderFactory metadataProviderFactory = new MetadataProviderFactory(
                runtimeConfigProvider: runtimeConfigProvider,
                queryManagerFactory: queryManagerfactory,
                logger: metadatProviderLogger.Object,
                fileSystem: null,
                handler: null);

            // Collecte all the metadata from the database.
            await metadataProviderFactory.InitializeAsync();

            // Setup GQL filter parser.
            GQLFilterParser graphQLFilterParser = new(runtimeConfigProvider: runtimeConfigProvider, metadataProviderFactory: metadataProviderFactory);

            // Setup Authorization resolver.
            IAuthorizationResolver authorizationResolver = new AuthorizationResolver(
                runtimeConfigProvider: runtimeConfigProvider,
                metadataProviderFactory: metadataProviderFactory);

            // Setup query engine factory.
            IQueryEngineFactory queryEngineFactory = new QueryEngineFactory(
                runtimeConfigProvider: runtimeConfigProvider,
                queryManagerFactory: queryManagerfactory,
                metadataProviderFactory: metadataProviderFactory,
                cosmosClientProvider: null,
                contextAccessor: httpContextAccessor.Object,
                authorizationResolver: authorizationResolver,
                gQLFilterParser: graphQLFilterParser,
                logger: queryEngineLogger.Object,
                cache: cacheService,
                handler: null);

            // Setup mock mutation engine factory.
            Mock<IMutationEngineFactory> mutationEngineFactory = new();

            // Return the setup GraphQL schema creator instance.
            return new GraphQLSchemaCreator(
                runtimeConfigProvider: runtimeConfigProvider,
                queryEngineFactory: queryEngineFactory,
                mutationEngineFactory: mutationEngineFactory.Object,
                metadataProviderFactory: metadataProviderFactory,
                authorizationResolver: authorizationResolver);
        }
        #endregion

        #region Clean up
        [TestCleanup]
        public void CleanupAfterEachTest()
        {
            TestHelper.UnsetAllDABEnvironmentVariables();
        }
        #endregion
    }
}
