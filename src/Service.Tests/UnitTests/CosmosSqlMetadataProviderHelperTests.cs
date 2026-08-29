// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Parsers;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Service.Exceptions;
using HotChocolate.Language;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass, TestCategory(TestCategory.COSMOSDBNOSQL)]
    public class CosmosSqlMetadataProviderHelperTests
    {
        [TestMethod]
        public void InterfaceMembers_ThatCosmosDoesNotSupport_Throw()
        {
            CosmosSqlMetadataProvider provider = CreateProvider();

            Assert.ThrowsException<NotImplementedException>(() => _ = provider.PairToFkDefinition);
            Assert.ThrowsException<NotImplementedException>(() => _ = provider.RelationshipToFkDefinition);
            Assert.ThrowsException<NotImplementedException>(() => provider.RelationshipToFkDefinition = new());
            Assert.ThrowsException<NotImplementedException>(() => provider.GetQueryBuilder());
            Assert.ThrowsException<NotImplementedException>(() => provider.VerifyForeignKeyExistsInDB(new(), new()));
            Assert.ThrowsException<NotImplementedException>(() => provider.ParseSchemaAndDbTableName("container"));
            Assert.ThrowsException<NotImplementedException>(() => provider.GetEntityNamesAndDbObjects());
            Assert.ThrowsException<NotImplementedException>(() => provider.TryGetEntityNameFromPath("path", out _));
            Assert.ThrowsException<NotImplementedException>(() => provider.TryGetExposedFieldToBackingFieldMap("Book", out _));
            Assert.ThrowsException<NotImplementedException>(() => provider.TryGetBackingFieldToExposedFieldMap("Book", out _));
            Assert.ThrowsException<NotImplementedException>(() => provider.InitializeAsync(new(), new()));
            Assert.ThrowsException<NotSupportedException>(() => provider.GetStoredProcedureDefinition("Book"));
        }

        [TestMethod]
        public void TrivialMetadataMembers_ReturnCosmosDefaults()
        {
            CosmosSqlMetadataProvider provider = CreateProvider(databaseType: DatabaseType.CosmosDB_NoSQL, isDevelopment: true);

            Assert.AreEqual(DatabaseType.CosmosDB_NoSQL, provider.GetDatabaseType());
            Assert.AreEqual(string.Empty, provider.GetDefaultSchemaName());
            Assert.IsTrue(provider.IsDevelopmentMode());
            Assert.AreEqual(0, provider.GetSourceDefinition("Book").Columns.Count);
            Assert.AreSame(provider.GetODataParser(), provider.GetODataParser());
            Assert.IsTrue(provider.InitializeAsync().IsCompletedSuccessfully);
        }

        [TestMethod]
        public void FieldMappingMembers_ReturnInputFieldWithoutMapping()
        {
            CosmosSqlMetadataProvider provider = CreateProvider();

            Assert.IsTrue(provider.TryGetExposedColumnName("Book", "id", out string? exposed));
            Assert.AreEqual("id", exposed);
            Assert.IsTrue(provider.TryGetBackingColumn("Book", "id", out string? backing));
            Assert.AreEqual("id", backing);
            Assert.IsFalse(provider.TryGetArrayElementSyntaxKind("Book", "id", out SyntaxKind kind));
            Assert.AreEqual(default, kind);
        }

        [TestMethod]
        public void PartitionKeyPath_CanBeAddedUpdatedAndRead()
        {
            CosmosSqlMetadataProvider provider = CreateProvider();

            Assert.IsNull(provider.GetPartitionKeyPath("db", "container"));
            provider.SetPartitionKeyPath("db", "container", "/tenantId");
            Assert.AreEqual("/tenantId", provider.GetPartitionKeyPath("db", "container"));
            provider.SetPartitionKeyPath("db", "container", "/accountId");
            Assert.AreEqual("/accountId", provider.GetPartitionKeyPath("db", "container"));
        }

        [DataTestMethod]
        [DataRow(null, "container", "/id")]
        [DataRow("db", null, "/id")]
        [DataRow("db", "container", null)]
        public void SetPartitionKeyPath_NullArgumentsThrow(string? database, string? container, string? path)
        {
            CosmosSqlMetadataProvider provider = CreateProvider();
            Assert.ThrowsException<ArgumentNullException>(() => provider.SetPartitionKeyPath(database!, container!, path!));
        }

        [DataTestMethod]
        [DataRow(null, "container")]
        [DataRow("db", null)]
        public void GetPartitionKeyPath_NullArgumentsThrow(string? database, string? container)
        {
            CosmosSqlMetadataProvider provider = CreateProvider();
            Assert.ThrowsException<ArgumentNullException>(() => provider.GetPartitionKeyPath(database!, container!));
        }

        [DataTestMethod]
        [DataRow("db.books", "configuredDb", "configuredContainer", "books")]
        [DataRow("books", "configuredDb", "configuredContainer", "books")]
        [DataRow("", "configuredDb", "configuredContainer", "configuredContainer")]
        public void GetDatabaseObjectName_ResolvesSourceOrConfiguredContainer(
            string source,
            string configuredDatabase,
            string configuredContainer,
            string expected)
        {
            CosmosSqlMetadataProvider provider = CreateProvider(
                entities: new() { ["Book"] = CreateEntity(source) },
                options: new(configuredDatabase, configuredContainer, null, null));

            Assert.AreEqual(expected, provider.GetDatabaseObjectName("Book"));
        }

        [DataTestMethod]
        [DataRow("db.books", "configuredDb", "db")]
        [DataRow("books", "configuredDb", "configuredDb")]
        [DataRow("", "configuredDb", "configuredDb")]
        public void GetSchemaName_ResolvesSourceOrConfiguredDatabase(string source, string configuredDatabase, string expected)
        {
            CosmosSqlMetadataProvider provider = CreateProvider(
                entities: new() { ["Book"] = CreateEntity(source) },
                options: new(configuredDatabase, "container", null, null));

            Assert.AreEqual(expected, provider.GetSchemaName("Book"));
        }

        [TestMethod]
        public void GetSchemaName_MissingDatabaseThrows()
        {
            CosmosSqlMetadataProvider provider = CreateProvider(
                entities: new() { ["Book"] = CreateEntity(string.Empty) },
                options: new(null, "container", null, null));

            Assert.ThrowsException<DataApiBuilderException>(() => provider.GetSchemaName("Book"));
        }

        [TestMethod]
        public void GetDatabaseObjectName_NullSourceAndMissingContainerThrows()
        {
            CosmosSqlMetadataProvider provider = CreateProvider(
                entities: new() { ["Book"] = CreateEntity(null!) },
                options: new("db", null, null, null));

            Assert.ThrowsException<DataApiBuilderException>(() => provider.GetDatabaseObjectName("Book"));
        }

        [TestMethod]
        public void GetDatabaseObjectName_EmptySourceAndMissingContainerReturnsEmptyName()
        {
            CosmosSqlMetadataProvider provider = CreateProvider(
                entities: new() { ["Book"] = CreateEntity(string.Empty) },
                options: new("db", string.Empty, null, null));

            Assert.AreEqual(string.Empty, provider.GetDatabaseObjectName("Book"));
        }

        [TestMethod]
        public void GetSchemaName_OnePartSourceAndMissingConfiguredDatabaseThrows()
        {
            CosmosSqlMetadataProvider provider = CreateProvider(
                entities: new() { ["Book"] = CreateEntity("books") },
                options: new(null, "container", null, null));

            Assert.ThrowsException<DataApiBuilderException>(() => provider.GetSchemaName("Book"));
        }

        [TestMethod]
        public void Constructor_MissingCosmosOptionsThrowsInitializationError()
        {
            RuntimeConfig runtimeConfig = new(
                Schema: string.Empty,
                DataSource: new DataSource(DatabaseType.CosmosDB_NoSQL, string.Empty, Options: null),
                Entities: new RuntimeEntities(new Dictionary<string, Entity>()));
            RuntimeConfigProvider configProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(runtimeConfig);
            MockFileSystem fileSystem = new();
            RuntimeConfigValidator validator = new(
                configProvider,
                fileSystem,
                NullLogger<RuntimeConfigValidator>.Instance);

            DataApiBuilderException exception = Assert.ThrowsException<DataApiBuilderException>(() =>
                new CosmosSqlMetadataProvider(configProvider, validator, fileSystem));

            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ErrorInInitialization, exception.SubStatusCode);
        }

        [TestMethod]
        public void GetEntityName_ResolvesDirectModelDirectiveAndSingularNames()
        {
            Dictionary<string, Entity> entities = new()
            {
                ["Book"] = CreateEntity("db.books", singular: "Volume")
            };
            DocumentNode schema = Utf8GraphQLParser.Parse("type BookAlias @model(name: \"Book\") { id: ID }");
            CosmosSqlMetadataProvider provider = CreateProvider(entities: entities, schema: schema);

            Assert.AreEqual("Book", provider.GetEntityName("Book"));
            Assert.AreEqual("Book", provider.GetEntityName("BookAlias"));
            Assert.AreEqual("Volume", provider.GetEntityName("Volume"));
            Assert.ThrowsException<DataApiBuilderException>(() => provider.GetEntityName("Missing"));
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("not valid graphql")]
        public void ParseSchemaGraphQLDocument_InvalidSchemaThrows(string schema)
        {
            CosmosSqlMetadataProvider provider = CreateProvider(options: new("db", "container", null, schema));

            Assert.ThrowsException<DataApiBuilderException>(() => provider.ParseSchemaGraphQLDocument());
        }

        [TestMethod]
        public void ParseSchemaGraphQLDocument_LoadsSchemaFromConfiguredFile()
        {
            Mock<IFileSystem> fileSystem = new();
            fileSystem.Setup(x => x.File.ReadAllText("schema.graphql"))
                .Returns("type Book @model(name: \"Book\") { id: ID!, title: String }");
            CosmosSqlMetadataProvider provider = CreateProvider(options: new("db", "container", "schema.graphql", null));
            SetField(provider, "_fileSystem", fileSystem.Object);

            provider.ParseSchemaGraphQLDocument();
            InvokePrivate(provider, "ParseSchemaGraphQLFieldsForGraphQLType");

            CollectionAssert.AreEquivalent(new[] { "id", "title" }, provider.GetSchemaGraphQLFieldNamesForEntityName("Book"));
            Assert.AreEqual("ID!", provider.GetSchemaGraphQLFieldTypeFromFieldName("Book", "id"));
            Assert.AreEqual("title", provider.GetSchemaGraphQLFieldFromFieldName("Book", "title")!.Name.Value);
            Assert.AreEqual(0, provider.GetSchemaGraphQLFieldNamesForEntityName("Missing").Count);
            Assert.IsNull(provider.GetSchemaGraphQLFieldTypeFromFieldName("Missing", "id"));
            Assert.IsNull(provider.GetSchemaGraphQLFieldFromFieldName("Missing", "id"));
        }

        [TestMethod]
        public void ParseSchemaGraphQLFieldsForJoins_AddsRepeatedModelPaths()
        {
            DocumentNode schema = Utf8GraphQLParser.Parse(@"
                type FirstBook @model(name: ""Book"") { id: ID }
                type SecondBook @model(name: ""Book"") { id: ID }");
            CosmosSqlMetadataProvider provider = CreateProvider(
                entities: new() { ["Book"] = CreateEntity("db.books") },
                schema: schema);

            InvokePrivate(provider, "ParseSchemaGraphQLFieldsForJoins");

            Assert.AreEqual(2, provider.EntityWithJoins["Book"].Count);
        }

        [TestMethod]
        public void AssertIfEntityIsAvailableInConfig_MissingEntityThrows()
        {
            CosmosSqlMetadataProvider provider = CreateProvider();

            TargetInvocationException exception = Assert.ThrowsException<TargetInvocationException>(
                () => InvokePrivate(provider, "AssertIfEntityIsAvailableInConfig", "Missing"));

            Assert.IsInstanceOfType<DataApiBuilderException>(exception.InnerException);
        }

        private static CosmosSqlMetadataProvider CreateProvider(
            Dictionary<string, Entity>? entities = null,
            CosmosDbNoSQLDataSourceOptions? options = null,
            DatabaseType databaseType = DatabaseType.CosmosDB_NoSQL,
            bool isDevelopment = false,
            DocumentNode? schema = null)
        {
            CosmosSqlMetadataProvider provider = (CosmosSqlMetadataProvider)RuntimeHelpers.GetUninitializedObject(typeof(CosmosSqlMetadataProvider));
            SetField(provider, "_runtimeConfigEntities", new RuntimeEntities(entities ?? new Dictionary<string, Entity>()));
            SetField(provider, "_cosmosDb", options ?? new CosmosDbNoSQLDataSourceOptions("db", "container", null, null));
            SetField(provider, "_databaseType", databaseType);
            SetField(provider, "_isDevelopmentMode", isDevelopment);
            SetField(provider, "_partitionKeyPaths", new ConcurrentDictionary<string, string>());
            SetField(provider, "_oDataParser", new ODataParser());
            SetField(provider, "_graphQLTypeToFieldsMap", new Dictionary<string, List<FieldDefinitionNode>>());
            provider.EntityWithJoins = new Dictionary<string, List<EntityDbPolicyCosmosModel>>();
            provider.GraphQLSchemaRoot = schema ?? new DocumentNode(Array.Empty<IDefinitionNode>());
            return provider;
        }

        private static Entity CreateEntity(string source, string singular = "Book") =>
            new(
                Source: new EntitySource(source, EntitySourceType.Table, null, null),
                GraphQL: new EntityGraphQLOptions(singular, "Books"),
                Fields: null,
                Rest: new EntityRestOptions(Enabled: true),
                Permissions: Array.Empty<EntityPermission>(),
                Mappings: null,
                Relationships: null);

        private static void SetField(CosmosSqlMetadataProvider provider, string name, object value)
        {
            typeof(CosmosSqlMetadataProvider).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(provider, value);
        }

        private static object? InvokePrivate(CosmosSqlMetadataProvider provider, string name, params object[] arguments)
        {
            return typeof(CosmosSqlMetadataProvider).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(provider, arguments);
        }
    }
}
