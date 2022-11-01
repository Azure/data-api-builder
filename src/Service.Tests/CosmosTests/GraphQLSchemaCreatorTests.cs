using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.DataApiBuilder.Service.Services;
using Azure.DataApiBuilder.Service.Services.MetadataProviders;
using Azure.DataApiBuilder.Service.Tests.GraphQLBuilder.Helpers;
using HotChocolate;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Snapshooter.MSTest;

namespace Azure.DataApiBuilder.Service.Tests.CosmosTests
{
    [TestClass]
    public class GraphQLSchemaCreatorTests
    {
        [TestMethod]
        public void SingleObjectSchema()
        {
            string graphql = @"
                type Author @model {
                    id : ID,
                    first_name : String,
                    middle_name: String,
                    last_name: String,
                }";

            Dictionary<string, Entity> entities = new()
            {
                {"Author", new Entity("test", null, "Author", new[] { new PermissionSetting(Operation.Read.ToString(), new[] { "*" }), new PermissionSetting(Operation.Create.ToString(), new[] { "*" })}, null, null) }
            };
            DataSource dataSource = new(DatabaseType.cosmos);
            CosmosDbOptions cosmosDb = new("test", "test", "c:\\schema.graphql", graphql);
            RuntimeConfig config = new(graphql, dataSource, cosmosDb, null, null, null, null, entities);
            Mock<ILogger<RuntimeConfigProvider>> configProviderLogger = new();
            RuntimeConfigProvider runtimeConfigProvider = new(config, configProviderLogger.Object);
            MockFileSystem fs = new();
            CosmosSqlMetadataProvider metadataProvider = new(runtimeConfigProvider, fs);
            Mock<IAuthorizationResolver> authResolver = new();
            Dictionary<string, EntityMetadata> entityMap = GraphQLTestHelpers.CreateStubEntityPermissionsMap(
                    entityNames: new[] { "Author" },
                    operations: new Operation[] { Operation.Create, Operation.Read, Operation.Update, Operation.Delete },
                    roles: new string[] { "anonymous", "authenticated" }
                );
            authResolver.Setup(x => x.EntityPermissionsMap).Returns(entityMap);

            GraphQLSchemaCreator schemaCreator = new(
                runtimeConfigProvider,
                null,
                null,
                metadataProvider,
                authResolver.Object);

            ISchemaBuilder schemaBuilder = schemaCreator.InitializeSchemaAndResolvers(new SchemaBuilder());
            ISchema schema = schemaBuilder.Create();

            Snapshot.Match(schema.ToString());
        }

        [TestMethod]
        public void MultipleObjectSchema()
        {
            string graphql = @"
                type Author @model {
                    id : ID,
                    first_name : String,
                    middle_name: String,
                    last_name: String,
                }

                type Book @model {
                    id : ID,
                    title : String
                }";

            Dictionary<string, Entity> entities = new()
            {
                {"Author", new Entity("test", null, "Author", new[] { new PermissionSetting(Operation.Read.ToString(), new[] { "*" }), new PermissionSetting(Operation.Create.ToString(), new[] { "*" })}, null, null) },
                {"Book", new Entity("test", null, "Author", new[] { new PermissionSetting(Operation.Read.ToString(), new[] { "*" }), new PermissionSetting(Operation.Create.ToString(), new[] { "*" })}, null, null) }
            };
            DataSource dataSource = new(DatabaseType.cosmos);
            CosmosDbOptions cosmosDb = new("test", "test", "c:\\schema.graphql", graphql);
            RuntimeConfig config = new(graphql, dataSource, cosmosDb, null, null, null, null, entities);
            Mock<ILogger<RuntimeConfigProvider>> configProviderLogger = new();
            RuntimeConfigProvider runtimeConfigProvider = new(config, configProviderLogger.Object);
            MockFileSystem fs = new();
            CosmosSqlMetadataProvider metadataProvider = new(runtimeConfigProvider, fs);
            Mock<IAuthorizationResolver> authResolver = new();
            Dictionary<string, EntityMetadata> entityMap = GraphQLTestHelpers.CreateStubEntityPermissionsMap(
                    entityNames: new[] { "Author", "Book" },
                    operations: new Operation[] { Operation.Create, Operation.Read, Operation.Update, Operation.Delete },
                    roles: new string[] { "anonymous", "authenticated" }
                );
            authResolver.Setup(x => x.EntityPermissionsMap).Returns(entityMap);

            GraphQLSchemaCreator schemaCreator = new(
                runtimeConfigProvider,
                null,
                null,
                metadataProvider,
                authResolver.Object);

            ISchemaBuilder schemaBuilder = schemaCreator.InitializeSchemaAndResolvers(new SchemaBuilder());
            ISchema schema = schemaBuilder.Create();

            Snapshot.Match(schema.ToString());
        }

        [TestMethod]
        public void OneWayRelationship()
        {
            string graphql = @"
                type Author @model {
                    id : ID,
                    first_name : String,
                    middle_name: String,
                    last_name: String,
                }

                type Book @model {
                    id : ID,
                    title : String
                    Author: Author!
                }";

            Dictionary<string, Entity> entities = new()
            {
                {"Author", new Entity("test", null, "Author", new[] { new PermissionSetting(Operation.Read.ToString(), new[] { "*" }), new PermissionSetting(Operation.Create.ToString(), new[] { "*" })}, null, null) },
                {"Book", new Entity("test", null, "Author", new[] { new PermissionSetting(Operation.Read.ToString(), new[] { "*" }), new PermissionSetting(Operation.Create.ToString(), new[] { "*" })}, null, null) }
            };
            DataSource dataSource = new(DatabaseType.cosmos);
            CosmosDbOptions cosmosDb = new("test", "test", "c:\\schema.graphql", graphql);
            RuntimeConfig config = new(graphql, dataSource, cosmosDb, null, null, null, null, entities);
            Mock<ILogger<RuntimeConfigProvider>> configProviderLogger = new();
            RuntimeConfigProvider runtimeConfigProvider = new(config, configProviderLogger.Object);
            MockFileSystem fs = new();
            CosmosSqlMetadataProvider metadataProvider = new(runtimeConfigProvider, fs);
            Mock<IAuthorizationResolver> authResolver = new();
            Dictionary<string, EntityMetadata> entityMap = GraphQLTestHelpers.CreateStubEntityPermissionsMap(
                    entityNames: new[] { "Author", "Book" },
                    operations: new Operation[] { Operation.Create, Operation.Read, Operation.Update, Operation.Delete },
                    roles: new string[] { "anonymous", "authenticated" }
                );
            authResolver.Setup(x => x.EntityPermissionsMap).Returns(entityMap);

            GraphQLSchemaCreator schemaCreator = new(
                runtimeConfigProvider,
                null,
                null,
                metadataProvider,
                authResolver.Object);

            ISchemaBuilder schemaBuilder = schemaCreator.InitializeSchemaAndResolvers(new SchemaBuilder());
            ISchema schema = schemaBuilder.Create();

            Snapshot.Match(schema.ToString());
        }

        [TestMethod]
        public void RecursiveRelationship()
        {
            string graphql = @"
                type Author @model {
                    id : ID,
                    first_name : String,
                    middle_name: String,
                    last_name: String,
                    Books: [Book]
                }

                type Book @model {
                    id : ID,
                    title : String,
                    Authors: [Author]
                }";

            Dictionary<string, Entity> entities = new()
            {
                {"Author", new Entity("test", null, "Author", new[] { new PermissionSetting(Operation.Read.ToString(), new[] { "*" }), new PermissionSetting(Operation.Create.ToString(), new[] { "*" })}, null, null) },
                {"Book", new Entity("test", null, "Author", new[] { new PermissionSetting(Operation.Read.ToString(), new[] { "*" }), new PermissionSetting(Operation.Create.ToString(), new[] { "*" })}, null, null) }
            };
            DataSource dataSource = new(DatabaseType.cosmos);
            CosmosDbOptions cosmosDb = new("test", "test", "c:\\schema.graphql", graphql);
            RuntimeConfig config = new(graphql, dataSource, cosmosDb, null, null, null, null, entities);
            Mock<ILogger<RuntimeConfigProvider>> configProviderLogger = new();
            RuntimeConfigProvider runtimeConfigProvider = new(config, configProviderLogger.Object);
            MockFileSystem fs = new();
            CosmosSqlMetadataProvider metadataProvider = new(runtimeConfigProvider, fs);
            Mock<IAuthorizationResolver> authResolver = new();
            Dictionary<string, EntityMetadata> entityMap = GraphQLTestHelpers.CreateStubEntityPermissionsMap(
                    entityNames: new[] { "Author", "Book" },
                    operations: new Operation[] { Operation.Create, Operation.Read, Operation.Update, Operation.Delete },
                    roles: new string[] { "anonymous", "authenticated" }
                );
            authResolver.Setup(x => x.EntityPermissionsMap).Returns(entityMap);

            GraphQLSchemaCreator schemaCreator = new(
                runtimeConfigProvider,
                null,
                null,
                metadataProvider,
                authResolver.Object);

            ISchemaBuilder schemaBuilder = schemaCreator.InitializeSchemaAndResolvers(new SchemaBuilder());
            ISchema schema = schemaBuilder.Create();

            Snapshot.Match(schema.ToString());
        }

        [TestMethod]
        public void RecursiveNonModelRelationship()
        {
            string graphql = @"
                type Author @model {
                    id : ID,
                    first_name : String,
                    middle_name: String,
                    last_name: String,
                    Books: [Book]
                }

                type Book {
                    id : ID,
                    title : String,
                    Authors: [Author]
                }";

            Dictionary<string, Entity> entities = new()
            {
                {"Author", new Entity("test", null, "Author", new[] { new PermissionSetting(Operation.Read.ToString(), new[] { "*" }), new PermissionSetting(Operation.Create.ToString(), new[] { "*" })}, null, null) },
                {"Book", new Entity("test", null, "Author", new[] { new PermissionSetting(Operation.Read.ToString(), new[] { "*" }), new PermissionSetting(Operation.Create.ToString(), new[] { "*" })}, null, null) }
            };
            DataSource dataSource = new(DatabaseType.cosmos);
            CosmosDbOptions cosmosDb = new("test", "test", "c:\\schema.graphql", graphql);
            RuntimeConfig config = new(graphql, dataSource, cosmosDb, null, null, null, null, entities);
            Mock<ILogger<RuntimeConfigProvider>> configProviderLogger = new();
            RuntimeConfigProvider runtimeConfigProvider = new(config, configProviderLogger.Object);
            MockFileSystem fs = new();
            CosmosSqlMetadataProvider metadataProvider = new(runtimeConfigProvider, fs);
            Mock<IAuthorizationResolver> authResolver = new();
            Dictionary<string, EntityMetadata> entityMap = GraphQLTestHelpers.CreateStubEntityPermissionsMap(
                    entityNames: new[] { "Author", "Book" },
                    operations: new Operation[] { Operation.Create, Operation.Read, Operation.Update, Operation.Delete },
                    roles: new string[] { "anonymous", "authenticated" }
                );
            authResolver.Setup(x => x.EntityPermissionsMap).Returns(entityMap);

            GraphQLSchemaCreator schemaCreator = new(
                runtimeConfigProvider,
                null,
                null,
                metadataProvider,
                authResolver.Object);

            ISchemaBuilder schemaBuilder = schemaCreator.InitializeSchemaAndResolvers(new SchemaBuilder());
            ISchema schema = schemaBuilder.Create();

            Snapshot.Match(schema.ToString());
        }
    }
}
