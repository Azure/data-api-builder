// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using HotChocolate;
using HotChocolate.Language;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.GraphQLBuilder
{
    [TestClass]
    public class GraphQLSchemaBuilderTests
    {
        /// <summary>
        /// Validates building of cosmos gql schema.
        /// 1. Loads customer base schema.
        /// 2. Calls on schema builder to build root document and the input type objects.
        /// 3. Root document should be created based on loaded schema.
        /// 4. Input type objects should be created based on entity types and not be duplicated.
        /// </summary>
        [DataTestMethod]
        public async Task CosmosSchemaBuilderTestAsync()
        {
            string fileContents = await File.ReadAllTextAsync("Multidab-config.CosmosDb_NoSql.json");

            string cosmosFileContents = await File.ReadAllTextAsync("schema.gql");

            IFileSystem fs = new MockFileSystem(new Dictionary<string, MockFileData>()
            {
                { "dab-config.json", new MockFileData(fileContents) },
                { "schema.gql", new MockFileData(cosmosFileContents) }
            });

            FileSystemRuntimeConfigLoader loader = new(fs);

            RuntimeConfigProvider provider = new(loader);

            Mock<ILogger<RuntimeConfigValidator>> loggerValidator = new();
            RuntimeConfigValidator validator = new(provider, fs, loggerValidator.Object);

            Mock<IAbstractQueryManagerFactory> queryManagerfactory = new();
            Mock<IQueryEngineFactory> queryEngineFactory = new();
            Mock<IMutationEngineFactory> mutationEngineFactory = new();
            Mock<ILogger<ISqlMetadataProvider>> logger = new();
            IMetadataProviderFactory metadataProviderFactory = new MetadataProviderFactory(provider, validator, queryManagerfactory.Object, logger.Object, fs, handler: null);
            Mock<IAuthorizationResolver> authResolver = new();

            GraphQLSchemaCreator creator = new(provider, queryEngineFactory.Object, mutationEngineFactory.Object, metadataProviderFactory, authResolver.Object);
            (DocumentNode root, Dictionary<string, InputObjectTypeDefinitionNode> inputTypeObjects) = creator.GenerateGraphQLObjects();

            Assert.IsNotNull(root);
            Assert.IsNotNull(inputTypeObjects);
            Assert.AreEqual(9, inputTypeObjects.Count, $"{nameof(InputObjectTypeDefinitionNode)} is invalid. input Type objects have not been created correctly.");

            // 11 input types generated for the 3 entity types in the schema.gql. IntFilter,StringFilter etc should not be duplicated.
            Assert.AreEqual(13, root.Definitions.Count, $"{nameof(DocumentNode)}:Root is invalid. root definitions count does not match expected count.");
        }

        /// <summary>
        /// Validates that <see cref="GraphQLSchemaCreator.EnsureQueryHasAtLeastOneField"/> injects a
        /// placeholder field into <c>Query</c> when it has no fields. This guards the empty-schema
        /// scenarios (GraphQL globally disabled, all entities opting out, no entities configured)
        /// against HC v16's eager schema validation, which rejects any <c>Query</c> type with zero fields.
        /// </summary>
        [TestMethod]
        public void EnsureQueryHasAtLeastOneField_InjectsPlaceholder_WhenQueryIsEmpty()
        {
            // Arrange: a document with an empty Query and an unrelated Book type alongside it.
            // Including the unrelated type confirms the rewrite preserves the rest of the document
            // without dropping definitions that come after Query.
            ObjectTypeDefinitionNode emptyQuery = new(
                location: null,
                name: new NameNode("Query"),
                description: null,
                directives: new List<DirectiveNode>(),
                interfaces: new List<NamedTypeNode>(),
                fields: new List<FieldDefinitionNode>());

            ObjectTypeDefinitionNode bookType = new(
                location: null,
                name: new NameNode("Book"),
                description: null,
                directives: new List<DirectiveNode>(),
                interfaces: new List<NamedTypeNode>(),
                fields: new List<FieldDefinitionNode>
                {
                    new(
                        location: null,
                        name: new NameNode("id"),
                        description: null,
                        arguments: new List<InputValueDefinitionNode>(),
                        type: new NamedTypeNode(new NameNode("Int")),
                        directives: new List<DirectiveNode>())
                });

            DocumentNode input = new(new IDefinitionNode[] { emptyQuery, bookType });
            ISchemaBuilder schemaBuilder = SchemaBuilder.New();

            // Act
            DocumentNode result = GraphQLSchemaCreator.EnsureQueryHasAtLeastOneField(input, schemaBuilder);

            // Assert: a new document is returned (not the same reference) with the same count of
            // definitions, ordering preserved, and Query now contains exactly the placeholder field.
            Assert.AreNotSame(input, result, "Expected a new DocumentNode when injection occurs.");
            Assert.AreEqual(2, result.Definitions.Count, "Definition count should be preserved.");

            ObjectTypeDefinitionNode resultQuery = (ObjectTypeDefinitionNode)result.Definitions[0];
            Assert.AreEqual("Query", resultQuery.Name.Value);
            Assert.AreEqual(1, resultQuery.Fields.Count, "Query should have the placeholder field.");
            Assert.AreEqual(
                GraphQLSchemaCreator.EMPTY_SCHEMA_PLACEHOLDER_FIELD_NAME,
                resultQuery.Fields[0].Name.Value,
                "Placeholder field name should match the documented constant.");
            Assert.AreEqual("String", ((NamedTypeNode)resultQuery.Fields[0].Type).Name.Value);

            ObjectTypeDefinitionNode resultBook = (ObjectTypeDefinitionNode)result.Definitions[1];
            Assert.AreEqual("Book", resultBook.Name.Value, "Non-Query definitions should be preserved unchanged.");
            Assert.AreSame(bookType, resultBook, "Non-Query definitions should be passed through by reference.");
        }

        /// <summary>
        /// Validates that <see cref="GraphQLSchemaCreator.EnsureQueryHasAtLeastOneField"/> returns the
        /// original <c>DocumentNode</c> by reference (no allocation, no copy) when <c>Query</c>
        /// already contains at least one field. This is the common-case fast path.
        /// </summary>
        [TestMethod]
        public void EnsureQueryHasAtLeastOneField_ReturnsOriginal_WhenQueryHasFields()
        {
            ObjectTypeDefinitionNode populatedQuery = new(
                location: null,
                name: new NameNode("Query"),
                description: null,
                directives: new List<DirectiveNode>(),
                interfaces: new List<NamedTypeNode>(),
                fields: new List<FieldDefinitionNode>
                {
                    new(
                        location: null,
                        name: new NameNode("books"),
                        description: null,
                        arguments: new List<InputValueDefinitionNode>(),
                        type: new NamedTypeNode(new NameNode("String")),
                        directives: new List<DirectiveNode>())
                });

            DocumentNode input = new(new IDefinitionNode[] { populatedQuery });
            ISchemaBuilder schemaBuilder = SchemaBuilder.New();

            DocumentNode result = GraphQLSchemaCreator.EnsureQueryHasAtLeastOneField(input, schemaBuilder);

            Assert.AreSame(input, result, "Expected the original DocumentNode to be returned unchanged.");
            Assert.IsFalse(
                result.Definitions.OfType<ObjectTypeDefinitionNode>()
                    .First(d => d.Name.Value == "Query")
                    .Fields.Any(f => f.Name.Value == GraphQLSchemaCreator.EMPTY_SCHEMA_PLACEHOLDER_FIELD_NAME),
                "Placeholder field should not be added when Query already has real fields.");
        }

        /// <summary>
        /// Validates that <see cref="GraphQLSchemaCreator.EnsureQueryHasAtLeastOneField"/> is a no-op
        /// when the input document has no <c>Query</c> definition at all. This is a defensive edge
        /// case (the production caller always builds Query) but exercises the early-return path.
        /// </summary>
        [TestMethod]
        public void EnsureQueryHasAtLeastOneField_ReturnsOriginal_WhenNoQueryDefinitionPresent()
        {
            ObjectTypeDefinitionNode bookType = new(
                location: null,
                name: new NameNode("Book"),
                description: null,
                directives: new List<DirectiveNode>(),
                interfaces: new List<NamedTypeNode>(),
                fields: new List<FieldDefinitionNode>());

            DocumentNode input = new(new IDefinitionNode[] { bookType });
            ISchemaBuilder schemaBuilder = SchemaBuilder.New();

            DocumentNode result = GraphQLSchemaCreator.EnsureQueryHasAtLeastOneField(input, schemaBuilder);

            Assert.AreSame(input, result, "Expected the original DocumentNode when no Query is present.");
        }
    }
}
