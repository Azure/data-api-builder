// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Azure.Core;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Resolvers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO.Abstractions.TestingHelpers;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass, TestCategory(TestCategory.COSMOSDBNOSQL)]
    public class CosmosClientProviderHelperTests
    {
        [TestMethod]
        public async Task Constructor_ConfigNotLoadedRegistersInitializationHandler()
        {
            RuntimeConfigProvider runtimeConfigProvider = new(
                new FileSystemRuntimeConfigLoader(new MockFileSystem()));

            CosmosClientProvider provider = new(runtimeConfigProvider);

            Assert.AreEqual(0, provider.Clients.Count);
            Assert.AreEqual(1, runtimeConfigProvider.RuntimeConfigLoadedHandlers.Count);

            RuntimeConfig configuration = new(
                Schema: string.Empty,
                DataSource: new DataSource(DatabaseType.MSSQL, "Server=localhost"),
                Entities: new RuntimeEntities(new Dictionary<string, Entity>()));
            bool initialized = await runtimeConfigProvider.RuntimeConfigLoadedHandlers[0](runtimeConfigProvider, configuration);

            Assert.IsTrue(initialized);
            Assert.AreEqual(0, provider.Clients.Count);
        }

        [TestMethod]
        public void InitializeClient_NullConfigurationThrows()
        {
            CosmosClientProvider provider = CreateUninitializedProvider();

            TargetInvocationException exception = Assert.ThrowsException<TargetInvocationException>(() =>
                InvokeInitializeClient(provider, null));

            Assert.IsInstanceOfType<ArgumentNullException>(exception.InnerException);
        }

        [TestMethod]
        public void InitializeClient_NonCosmosConfigurationReturnsWithoutClients()
        {
            CosmosClientProvider provider = CreateUninitializedProvider();
            RuntimeConfig configuration = new(
                Schema: string.Empty,
                DataSource: new DataSource(DatabaseType.MSSQL, "Server=localhost"),
                Entities: new RuntimeEntities(new Dictionary<string, Entity>()));

            InvokeInitializeClient(provider, configuration);

            Assert.AreEqual(0, provider.Clients.Count);
        }

        [TestMethod]
        public void InitializeClient_CosmosWithoutAccountKeyCreatesCredentialClient()
        {
            RuntimeConfig configuration = new(
                Schema: string.Empty,
                DataSource: new DataSource(
                    DatabaseType.CosmosDB_NoSQL,
                    "AccountEndpoint=https://localhost:8081/;",
                    new Dictionary<string, object?>()),
                Entities: new RuntimeEntities(new Dictionary<string, Entity>()));
            CosmosClientProvider provider = CreateUninitializedProvider();

            InvokeInitializeClient(provider, configuration);

            Assert.IsTrue(provider.Clients.ContainsKey(configuration.DefaultDataSourceName));
            provider.Clients[configuration.DefaultDataSourceName]!.Dispose();
        }

        [TestMethod]
        public void Constructor_LoadedCosmosConfigurationInitializesImmediately()
        {
            RuntimeConfig configuration = new(
                Schema: string.Empty,
                DataSource: new DataSource(
                    DatabaseType.CosmosDB_NoSQL,
                    "AccountEndpoint=https://localhost:8081/;",
                    new Dictionary<string, object?>()),
                Entities: new RuntimeEntities(new Dictionary<string, Entity>()));
            RuntimeConfigProvider runtimeConfigProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(configuration);
            Assert.AreEqual(DatabaseType.CosmosDB_NoSQL, runtimeConfigProvider.GetConfig().DataSource.DatabaseType);

            CosmosClientProvider provider = new(runtimeConfigProvider);

            Assert.AreEqual(0, runtimeConfigProvider.RuntimeConfigLoadedHandlers.Count);
            Assert.AreEqual(1, provider.Clients.Count);
            foreach (Microsoft.Azure.Cosmos.CosmosClient? client in provider.Clients.Values)
            {
                client?.Dispose();
            }
        }

        [DataTestMethod]
        [DataRow("AccountEndpoint=https://localhost:8081/;AccountKey=secret", "https://localhost:8081/", "secret")]
        [DataRow("ApplicationName=dab", null, null)]
        public void ParseCosmosConnectionString_ReturnsAvailableComponents(
            string connectionString,
            string? expectedEndpoint,
            string? expectedKey)
        {
            MethodInfo method = typeof(CosmosClientProvider).GetMethod(
                "ParseCosmosConnectionString",
                BindingFlags.Static | BindingFlags.NonPublic)!;

            (string? endpoint, string? key) = ((string?, string?))method.Invoke(null, new object[] { connectionString })!;

            Assert.AreEqual(expectedEndpoint, endpoint);
            Assert.AreEqual(expectedKey, key);
        }

        [TestMethod]
        public void AadTokenCredential_InvalidTokenThrows()
        {
            Type credentialType = typeof(CosmosClientProvider).GetNestedType(
                "AADTokenCredential",
                BindingFlags.NonPublic)!;
            TokenCredential credential = (TokenCredential)Activator.CreateInstance(
                credentialType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args: new object[] { "not-a-jwt" },
                culture: null)!;

            Assert.ThrowsException<InvalidOperationException>(() =>
                credential.GetToken(new TokenRequestContext(Array.Empty<string>()), default));
        }

        private static CosmosClientProvider CreateUninitializedProvider()
        {
            CosmosClientProvider provider =
                (CosmosClientProvider)RuntimeHelpers.GetUninitializedObject(typeof(CosmosClientProvider));
            typeof(CosmosClientProvider).GetField("<Clients>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(provider, new Dictionary<string, Microsoft.Azure.Cosmos.CosmosClient?>());
            typeof(CosmosClientProvider).GetField("_accessToken", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(provider, new Dictionary<string, string?>());
            return provider;
        }

        private static void InvokeInitializeClient(CosmosClientProvider provider, RuntimeConfig? configuration)
        {
            typeof(CosmosClientProvider).GetMethod("InitializeClient", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(provider, new object?[] { configuration });
        }
    }
}
