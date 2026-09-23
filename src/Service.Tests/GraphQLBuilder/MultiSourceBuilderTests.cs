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
using Azure.DataApiBuilder.Service.GraphQLBuilder.Directives;
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

        /// <summary>
        /// When a CosmosDB relationship's source and target entities are backed by the same container,
        /// the target's data is embedded in the source document. Grafting the configured relationship
        /// must strip only the embedded projection of the target (fields whose GraphQL type is the target
        /// type) and must NOT drop the source's own scalar fields that merely share a name with a target
        /// field. Here both <c>Planet</c> and <c>Character</c> map to <c>graphqldb.planet</c>, and
        /// <c>Character</c> also has <c>id</c>/<c>name</c> scalars, so a name-based strip would wrongly
        /// remove the planet's own <c>id</c>/<c>name</c>. This is the regression guard for that bug.
        /// </summary>
        [TestMethod]
        public async Task CosmosRelationship_SharedContainer_PreservesSourceScalars_AndStripsEmbeddedTargetProjection()
        {
            // Planet and Character share the same container; Planet declares a one-cardinality
            // relationship named "character" targeting Character.
            ObjectTypeDefinitionNode planet = await BuildCosmosPlanetTypeAsync(
                characterContainer: "graphqldb.planet",
                relationshipName: "character",
                cardinality: "one");

            HashSet<string> fieldNames = planet.Fields.Select(f => f.Name.Value).ToHashSet();

            // The source's own scalar fields survive even though Character also defines id/name.
            Assert.IsTrue(fieldNames.Contains("id"), "Planet's own 'id' scalar must be preserved.");
            Assert.IsTrue(fieldNames.Contains("name"), "Planet's own 'name' scalar must be preserved.");
            Assert.IsTrue(fieldNames.Contains("age"), "Planet's own 'age' scalar must be preserved.");
            Assert.IsTrue(fieldNames.Contains("dimension"), "Planet's own 'dimension' scalar must be preserved.");

            // 'stars' is typed [Star], not the target type, so it is not part of the embedded
            // Character projection and must be preserved.
            Assert.IsTrue(fieldNames.Contains("stars"), "Fields typed as a non-target type must be preserved.");

            // The 'character' field is now the configured relationship (replacing the embedded projection).
            FieldDefinitionNode characterField = planet.Fields.Single(f => f.Name.Value == "character");
            Assert.IsTrue(
                characterField.Directives.Any(d => d.Name.Value == RelationshipDirectiveType.DirectiveName),
                "The 'character' field should carry the @relationship directive.");
            Assert.AreEqual(
                "Character",
                characterField.Type.NamedType().Name.Value,
                "A one-cardinality relationship field should be typed as the target type.");
        }

        /// <summary>
        /// When the source and target entities are backed by different containers, the target is NOT
        /// embedded in the source document, so no type-based stripping should occur. Only an
        /// equally-named existing field is replaced by the configured relationship. Here Planet and
        /// Character live in different containers and the relationship is named "characters" (distinct
        /// from the embedded "character" field), so the embedded <c>character: Character</c> field must
        /// remain untouched alongside the new relationship field.
        /// </summary>
        [TestMethod]
        public async Task CosmosRelationship_DifferentContainers_DoesNotStripTargetTypedFields()
        {
            ObjectTypeDefinitionNode planet = await BuildCosmosPlanetTypeAsync(
                characterContainer: "graphqldb.character",
                relationshipName: "characters",
                cardinality: "many");

            HashSet<string> fieldNames = planet.Fields.Select(f => f.Name.Value).ToHashSet();

            // No shared container => the embedded Character projection is left in place.
            Assert.IsTrue(fieldNames.Contains("character"), "Embedded 'character' field must be preserved across containers.");
            Assert.IsTrue(fieldNames.Contains("id"), "Planet's own 'id' scalar must be preserved.");
            Assert.IsTrue(fieldNames.Contains("name"), "Planet's own 'name' scalar must be preserved.");
            Assert.IsTrue(fieldNames.Contains("stars"), "Planet's 'stars' field must be preserved.");

            // The configured relationship field is still added.
            FieldDefinitionNode charactersField = planet.Fields.Single(f => f.Name.Value == "characters");
            Assert.IsTrue(
                charactersField.Directives.Any(d => d.Name.Value == RelationshipDirectiveType.DirectiveName),
                "The 'characters' field should carry the @relationship directive.");
        }

        /// <summary>
        /// When a single entity declares multiple relationships that target the SAME type backed by the
        /// SAME container as the source, every grafted relationship field must be preserved. The
        /// shared-container path strips embedded projections of the target type (fields typed as the
        /// target); this must not remove relationship fields grafted on earlier iterations, which are
        /// themselves typed as the target. Prior to the fix only the last relationship survived. Here
        /// Planet declares both "characterA" and "characterB" targeting Character (same container), so
        /// both relationship fields must exist. This is the regression guard for that bug.
        /// </summary>
        [TestMethod]
        public async Task CosmosRelationship_MultipleRelationshipsToSameSharedContainerTarget_AllPreserved()
        {
            string configContents = BuildCosmosMultiRelationshipConfig(
                characterContainer: "graphqldb.planet",
                relationshipNames: new[] { "characterA", "characterB" });
            string cosmosSchemaContents = await File.ReadAllTextAsync("schema.gql");

            IFileSystem fs = new MockFileSystem(new Dictionary<string, MockFileData>()
            {
                { "dab-config.json", new MockFileData(configContents) },
                { "schema.gql", new MockFileData(cosmosSchemaContents) }
            });

            FileSystemRuntimeConfigLoader loader = new(fs);
            RuntimeConfigProvider provider = new(loader);

            Mock<ILogger<RuntimeConfigValidator>> loggerValidator = new();
            RuntimeConfigValidator validator = new(provider, fs, loggerValidator.Object);

            Mock<IAbstractQueryManagerFactory> queryManagerFactory = new();
            Mock<IQueryEngineFactory> queryEngineFactory = new();
            Mock<IMutationEngineFactory> mutationEngineFactory = new();
            Mock<ILogger<ISqlMetadataProvider>> logger = new();
            IMetadataProviderFactory metadataProviderFactory = new MetadataProviderFactory(provider, validator, queryManagerFactory.Object, logger.Object, fs, handler: null);
            Mock<IAuthorizationResolver> authResolver = new();

            GraphQLSchemaCreator creator = new(provider, queryEngineFactory.Object, mutationEngineFactory.Object, metadataProviderFactory, authResolver.Object);
            (DocumentNode root, _) = creator.GenerateGraphQLObjects();

            ObjectTypeDefinitionNode planet = root.Definitions
                .OfType<ObjectTypeDefinitionNode>()
                .Single(d => d.Name.Value == "Planet");

            HashSet<string> fieldNames = planet.Fields.Select(f => f.Name.Value).ToHashSet();

            // Both relationship fields targeting the same shared-container type must survive.
            Assert.IsTrue(fieldNames.Contains("characterA"), "First relationship field 'characterA' must be preserved.");
            Assert.IsTrue(fieldNames.Contains("characterB"), "Second relationship field 'characterB' must be preserved.");

            foreach (string relationshipName in new[] { "characterA", "characterB" })
            {
                FieldDefinitionNode field = planet.Fields.Single(f => f.Name.Value == relationshipName);
                Assert.IsTrue(
                    field.Directives.Any(d => d.Name.Value == RelationshipDirectiveType.DirectiveName),
                    $"The '{relationshipName}' field should carry the @relationship directive.");
                Assert.AreEqual(
                    "Character",
                    field.Type.NamedType().Name.Value,
                    $"The '{relationshipName}' relationship field should be typed as the target type.");
            }
        }

        /// <summary>
        /// Builds the Cosmos GraphQL schema for a Planet/Character/Star configuration and returns the
        /// generated <c>Planet</c> object type. The Character entity's container and the Planet-&gt;Character
        /// relationship are parameterized so callers can exercise the shared- and separate-container paths.
        /// </summary>
        private static async Task<ObjectTypeDefinitionNode> BuildCosmosPlanetTypeAsync(
            string characterContainer,
            string relationshipName,
            string cardinality)
        {
            string configContents = BuildCosmosRelationshipConfig(characterContainer, relationshipName, cardinality);
            string cosmosSchemaContents = await File.ReadAllTextAsync("schema.gql");

            IFileSystem fs = new MockFileSystem(new Dictionary<string, MockFileData>()
            {
                { "dab-config.json", new MockFileData(configContents) },
                { "schema.gql", new MockFileData(cosmosSchemaContents) }
            });

            FileSystemRuntimeConfigLoader loader = new(fs);
            RuntimeConfigProvider provider = new(loader);

            Mock<ILogger<RuntimeConfigValidator>> loggerValidator = new();
            RuntimeConfigValidator validator = new(provider, fs, loggerValidator.Object);

            Mock<IAbstractQueryManagerFactory> queryManagerFactory = new();
            Mock<IQueryEngineFactory> queryEngineFactory = new();
            Mock<IMutationEngineFactory> mutationEngineFactory = new();
            Mock<ILogger<ISqlMetadataProvider>> logger = new();
            IMetadataProviderFactory metadataProviderFactory = new MetadataProviderFactory(provider, validator, queryManagerFactory.Object, logger.Object, fs, handler: null);
            Mock<IAuthorizationResolver> authResolver = new();

            GraphQLSchemaCreator creator = new(provider, queryEngineFactory.Object, mutationEngineFactory.Object, metadataProviderFactory, authResolver.Object);
            (DocumentNode root, _) = creator.GenerateGraphQLObjects();

            return root.Definitions
                .OfType<ObjectTypeDefinitionNode>()
                .Single(d => d.Name.Value == "Planet");
        }

        /// <summary>
        /// Produces a minimal CosmosDB runtime config with Planet (PlanetAlias), Character and Star
        /// entities sharing the <c>schema.gql</c> schema. Planet maps to <c>graphqldb.planet</c> and
        /// declares the supplied relationship to Character; Character maps to the supplied container,
        /// which the caller sets equal to Planet's container to exercise the embedded (shared-container) path.
        /// </summary>
        /// <summary>
        /// Produces a minimal CosmosDB runtime config with a Planet (PlanetAlias) entity that declares
        /// multiple one-cardinality relationships (named per <paramref name="relationshipNames"/>) all
        /// targeting the Character entity, plus the Character and Star entities. Character maps to the
        /// supplied container, which callers set equal to Planet's container (graphqldb.planet) to
        /// exercise the shared-container embedded-projection stripping path.
        /// </summary>
        private static string BuildCosmosMultiRelationshipConfig(string characterContainer, string[] relationshipNames)
        {
            // Local CosmosDB emulator well-known endpoint/key; not a secret.
            const string connectionString = "AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

            string relationshipsJson = string.Join(",\n", relationshipNames.Select(name => $$"""
                    "{{name}}": {
                      "cardinality": "one",
                      "target.entity": "Character"
                    }
            """));

            return $$"""
            {
              "$schema": "https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch/dab.draft.schema.json",
              "data-source": {
                "database-type": "cosmosdb_nosql",
                "connection-string": "{{connectionString}}",
                "options": {
                  "database": "graphqldb",
                  "container": "planet",
                  "schema": "schema.gql"
                }
              },
              "runtime": {
                "rest": { "enabled": false, "path": "/api" },
                "graphql": { "enabled": true, "path": "/graphql", "allow-introspection": true },
                "host": { "authentication": { "provider": "StaticWebApps" }, "mode": "development" }
              },
              "entities": {
                "PlanetAlias": {
                  "source": { "object": "graphqldb.planet" },
                  "graphql": { "enabled": true, "type": { "singular": "Planet", "plural": "Planets" } },
                  "rest": { "enabled": false },
                  "permissions": [ { "role": "anonymous", "actions": [ { "action": "*" } ] } ],
                  "relationships": {
            {{relationshipsJson}}
                  }
                },
                "Character": {
                  "source": { "object": "{{characterContainer}}" },
                  "graphql": { "enabled": true, "type": { "singular": "Character", "plural": "Characters" } },
                  "rest": { "enabled": false },
                  "permissions": [ { "role": "anonymous", "actions": [ { "action": "*" } ] } ]
                },
                "Star": {
                  "source": { "object": "graphqldb.star" },
                  "graphql": { "enabled": true, "type": { "singular": "Star", "plural": "Stars" } },
                  "rest": { "enabled": false },
                  "permissions": [ { "role": "anonymous", "actions": [ { "action": "*" } ] } ]
                }
              }
            }
            """;
        }

        private static string BuildCosmosRelationshipConfig(string characterContainer, string relationshipName, string cardinality)
        {
            // Local CosmosDB emulator well-known endpoint/key; not a secret.
            const string connectionString = "AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

            return $$"""
            {
              "$schema": "https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch/dab.draft.schema.json",
              "data-source": {
                "database-type": "cosmosdb_nosql",
                "connection-string": "{{connectionString}}",
                "options": {
                  "database": "graphqldb",
                  "container": "planet",
                  "schema": "schema.gql"
                }
              },
              "runtime": {
                "rest": { "enabled": false, "path": "/api" },
                "graphql": { "enabled": true, "path": "/graphql", "allow-introspection": true },
                "host": { "authentication": { "provider": "StaticWebApps" }, "mode": "development" }
              },
              "entities": {
                "PlanetAlias": {
                  "source": { "object": "graphqldb.planet" },
                  "graphql": { "enabled": true, "type": { "singular": "Planet", "plural": "Planets" } },
                  "rest": { "enabled": false },
                  "permissions": [ { "role": "anonymous", "actions": [ { "action": "*" } ] } ],
                  "relationships": {
                    "{{relationshipName}}": {
                      "cardinality": "{{cardinality}}",
                      "target.entity": "Character"
                    }
                  }
                },
                "Character": {
                  "source": { "object": "{{characterContainer}}" },
                  "graphql": { "enabled": true, "type": { "singular": "Character", "plural": "Characters" } },
                  "rest": { "enabled": false },
                  "permissions": [ { "role": "anonymous", "actions": [ { "action": "*" } ] } ]
                },
                "Star": {
                  "source": { "object": "graphqldb.star" },
                  "graphql": { "enabled": true, "type": { "singular": "Star", "plural": "Stars" } },
                  "rest": { "enabled": false },
                  "permissions": [ { "role": "anonymous", "actions": [ { "action": "*" } ] } ]
                }
              }
            }
            """;
        }
    }
}
