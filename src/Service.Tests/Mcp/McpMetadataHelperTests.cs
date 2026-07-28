// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Mcp.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.Mcp
{
    /// <summary>
    /// Unit tests for <see cref="McpMetadataHelper"/> metadata-resolution branches using an
    /// in-memory config and a mocked metadata provider factory (no database).
    /// </summary>
    [TestClass]
    public class McpMetadataHelperTests
    {
        private const string ENTITY_NAME = "Book";

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public void TryResolveMetadata_NullOrEmptyEntityName_ReturnsFalse(string entityName)
        {
            RuntimeConfig config = CreateConfig(includeBookEntity: true);
            IServiceProvider serviceProvider = CreateServiceProvider(registerFactory: true, includeBookMetadata: true);

            bool result = McpMetadataHelper.TryResolveMetadata(
                entityName, config, serviceProvider, out _, out _, out _, out string error);

            Assert.IsFalse(result);
            Assert.AreEqual("Entity name cannot be null or empty.", error);
        }

        [TestMethod]
        public void TryResolveMetadata_FactoryNotRegistered_ReturnsFalse()
        {
            RuntimeConfig config = CreateConfig(includeBookEntity: true);
            IServiceProvider serviceProvider = CreateServiceProvider(registerFactory: false, includeBookMetadata: false);

            bool result = McpMetadataHelper.TryResolveMetadata(
                ENTITY_NAME, config, serviceProvider, out _, out _, out _, out string error);

            Assert.IsFalse(result);
            Assert.AreEqual("Metadata provider factory is not registered.", error);
        }

        [TestMethod]
        public void TryResolveMetadata_EntityNotInConfig_ReturnsFalse()
        {
            RuntimeConfig config = CreateConfig(includeBookEntity: false);
            IServiceProvider serviceProvider = CreateServiceProvider(registerFactory: true, includeBookMetadata: false);

            bool result = McpMetadataHelper.TryResolveMetadata(
                "GhostEntity", config, serviceProvider, out _, out _, out _, out string error);

            Assert.IsFalse(result);
            StringAssert.Contains(error, "is not defined in the configuration");
        }

        [TestMethod]
        public void TryResolveMetadata_EntityNotInMetadata_ReturnsFalse()
        {
            RuntimeConfig config = CreateConfig(includeBookEntity: true);
            // Factory registered but the metadata provider has no entry for the entity.
            IServiceProvider serviceProvider = CreateServiceProvider(registerFactory: true, includeBookMetadata: false);

            bool result = McpMetadataHelper.TryResolveMetadata(
                ENTITY_NAME, config, serviceProvider, out _, out _, out _, out string error);

            Assert.IsFalse(result);
            StringAssert.Contains(error, "is not defined in the configuration");
        }

        [TestMethod]
        public void TryResolveMetadata_Success_ReturnsTrue()
        {
            RuntimeConfig config = CreateConfig(includeBookEntity: true);
            IServiceProvider serviceProvider = CreateServiceProvider(registerFactory: true, includeBookMetadata: true);

            bool result = McpMetadataHelper.TryResolveMetadata(
                ENTITY_NAME, config, serviceProvider,
                out ISqlMetadataProvider provider, out DatabaseObject dbObject, out string dataSourceName, out string error);

            Assert.IsTrue(result);
            Assert.IsNotNull(provider);
            Assert.IsNotNull(dbObject);
            Assert.IsFalse(string.IsNullOrEmpty(dataSourceName));
            Assert.AreEqual(string.Empty, error);
        }

        [TestMethod]
        public void TryResolveMetadata_CancelledToken_Throws()
        {
            RuntimeConfig config = CreateConfig(includeBookEntity: true);
            IServiceProvider serviceProvider = CreateServiceProvider(registerFactory: true, includeBookMetadata: true);
            using CancellationTokenSource cts = new();
            cts.Cancel();

            Assert.ThrowsException<OperationCanceledException>(() => McpMetadataHelper.TryResolveMetadata(
                ENTITY_NAME, config, serviceProvider, out _, out _, out _, out _, cts.Token));
        }

        [TestMethod]
        public void TryResolveDatabaseObject_Success_ReturnsDbObject()
        {
            RuntimeConfig config = CreateConfig(includeBookEntity: true);
            IServiceProvider serviceProvider = CreateServiceProvider(registerFactory: true, includeBookMetadata: true);

            DatabaseObject? dbObject = McpMetadataHelper.TryResolveDatabaseObject(
                ENTITY_NAME, config, serviceProvider, out string error);

            Assert.IsNotNull(dbObject);
            Assert.AreEqual(string.Empty, error);
        }

        [TestMethod]
        public void TryResolveDatabaseObject_Failure_ReturnsNull()
        {
            RuntimeConfig config = CreateConfig(includeBookEntity: true);
            IServiceProvider serviceProvider = CreateServiceProvider(registerFactory: true, includeBookMetadata: false);

            DatabaseObject? dbObject = McpMetadataHelper.TryResolveDatabaseObject(
                ENTITY_NAME, config, serviceProvider, out string error);

            Assert.IsNull(dbObject);
            StringAssert.Contains(error, "is not defined in the configuration");
        }

        #region Helpers

        private static RuntimeConfig CreateConfig(bool includeBookEntity)
        {
            Dictionary<string, Entity> entities = new();
            if (includeBookEntity)
            {
                entities[ENTITY_NAME] = new Entity(
                    Source: new(ENTITY_NAME, EntitySourceType.Table, null, null),
                    GraphQL: new(ENTITY_NAME, ENTITY_NAME + "s"),
                    Fields: null,
                    Rest: new(Enabled: true),
                    Permissions: new[]
                    {
                        new EntityPermission(Role: "anonymous", Actions: new[]
                        {
                            new EntityAction(Action: EntityActionOperation.Read, Fields: null, Policy: null)
                        })
                    },
                    Mappings: null,
                    Relationships: null,
                    Mcp: null);
            }

            return new RuntimeConfig(
                Schema: "test-schema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, ConnectionString: "Server=test;", Options: null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: null,
                    Host: new(Cors: null, Authentication: null, Mode: HostMode.Development)),
                Entities: new(entities));
        }

        private static IServiceProvider CreateServiceProvider(bool registerFactory, bool includeBookMetadata)
        {
            ServiceCollection services = new();

            if (registerFactory)
            {
                Dictionary<string, DatabaseObject> dbObjects = new();
                if (includeBookMetadata)
                {
                    dbObjects[ENTITY_NAME] = new DatabaseTable(schemaName: "dbo", tableName: "books")
                    {
                        SourceType = EntitySourceType.Table,
                        TableDefinition = new SourceDefinition()
                    };
                }

                Mock<ISqlMetadataProvider> mockProvider = new();
                mockProvider.Setup(x => x.EntityToDatabaseObject).Returns(dbObjects);

                Mock<IMetadataProviderFactory> mockFactory = new();
                mockFactory.Setup(x => x.GetMetadataProvider(It.IsAny<string>())).Returns(mockProvider.Object);
                services.AddSingleton(mockFactory.Object);
            }

            return services.BuildServiceProvider();
        }

        #endregion
    }
}
