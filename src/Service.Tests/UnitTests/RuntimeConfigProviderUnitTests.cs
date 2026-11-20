// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for <see cref="RuntimeConfigProvider"/> accessor and access-token behavior
    /// using an in-memory loaded configuration.
    /// </summary>
    [TestClass]
    public class RuntimeConfigProviderUnitTests
    {
        [TestMethod]
        public void TryGetConfig_LoadedProvider_ReturnsTrue()
        {
            RuntimeConfigProvider provider = CreateProvider();

            Assert.IsTrue(provider.TryGetConfig(out RuntimeConfig? config));
            Assert.IsNotNull(config);
        }

        [TestMethod]
        public void TryGetLoadedConfig_BeforeLoad_ReturnsFalse_AfterLoad_ReturnsTrue()
        {
            RuntimeConfigProvider provider = CreateProvider();

            // Before any load is triggered, no config has been loaded yet.
            Assert.IsFalse(provider.TryGetLoadedConfig(out RuntimeConfig? notLoaded));
            Assert.IsNull(notLoaded);

            // GetConfig triggers the lazy load and persists it on the loader.
            provider.GetConfig();

            Assert.IsTrue(provider.TryGetLoadedConfig(out RuntimeConfig? loaded));
            Assert.IsNotNull(loaded);
        }

        [TestMethod]
        public void GetConfig_LoadedProvider_ReturnsConfig()
        {
            RuntimeConfigProvider provider = CreateProvider();

            RuntimeConfig config = provider.GetConfig();
            Assert.IsNotNull(config);
        }

        [TestMethod]
        public void TrySetAccesstoken_ExistingDataSource_StoresTokenAndReturnsTrue()
        {
            RuntimeConfigProvider provider = CreateProvider();
            string dataSourceName = provider.GetConfig().DefaultDataSourceName;

            bool result = provider.TrySetAccesstoken("test-token", dataSourceName);

            Assert.IsTrue(result);
            Assert.AreEqual("test-token", provider.ManagedIdentityAccessToken[dataSourceName]);
        }

        [TestMethod]
        public void TrySetAccesstoken_UnknownDataSource_ReturnsFalse()
        {
            RuntimeConfigProvider provider = CreateProvider();
            // Force the config to load so the data-source existence check runs.
            provider.GetConfig();

            bool result = provider.TrySetAccesstoken("test-token", "nonexistent-data-source");

            Assert.IsFalse(result);
        }

        private static RuntimeConfigProvider CreateProvider()
        {
            RuntimeConfig config = new(
                Schema: "test-schema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, ConnectionString: "Server=test;", Options: null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: null,
                    Host: new(Cors: null, Authentication: null, Mode: HostMode.Development)),
                Entities: new(new Dictionary<string, Entity>()));

            return TestHelper.GenerateInMemoryRuntimeConfigProvider(config);
        }
    }
}
