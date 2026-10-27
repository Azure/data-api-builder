// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Mcp.Core;
using Azure.DataApiBuilder.Mcp.Model;
using Azure.DataApiBuilder.Service.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;
using Moq;
using static Azure.DataApiBuilder.Config.DabConfigEvents;

namespace Azure.DataApiBuilder.Service.Tests.Mcp
{
    [TestClass]
    public class McpInitialHotReloadSerializationTests
    {
        [TestMethod]
        public async Task InitialConstructionAndReload_PublishLatestDatabaseMetadataGeneration()
        {
            string testDirectory = Path.Combine(
                Path.GetTempPath(),
                $"dab-mcp-initial-reload-serialization-{Guid.NewGuid():N}");
            Directory.CreateDirectory(testDirectory);
            string configPath = Path.Combine(testDirectory, "dab-config.json");
            File.WriteAllText(configPath, CreateRuntimeConfig("Generation A").ToJson());

            try
            {
                HotReloadEventHandler<HotReloadEventArgs> hotReloadEventHandler = new();
                FileSystem fileSystem = new();

                // The OS watcher is disabled so synchronization barriers, rather than filesystem
                // notification timing, deterministically control this startup-to-reload race.
                using FileSystemRuntimeConfigLoader configLoader = new(
                    fileSystem,
                    hotReloadEventHandler,
                    configPath,
                    connectionString: null,
                    isCliLoader: true);
                Assert.IsTrue(configLoader.TryLoadKnownConfig(out RuntimeConfig initialConfig));
                Assert.AreEqual(
                    "Generation A",
                    initialConfig.Entities["GetBook"].Description);

                // This focused test supplies database metadata directly. Use a provider backed by
                // the real loader state without attaching live-database validation to its change
                // token before the ordered handlers run.
                Mock<RuntimeConfigLoader> providerLoader = new(null, null);
                Mock<RuntimeConfigProvider> runtimeConfigProvider = new(providerLoader.Object);
                runtimeConfigProvider
                    .Setup(provider => provider.GetConfig())
                    .Returns(() => configLoader.RuntimeConfig!);

                Dictionary<string, DatabaseObject> currentMetadata =
                    CreateStoredProcedureMetadata("a_database_parameter", typeof(string), DbType.String);
                Mock<ISqlMetadataProvider> sqlMetadataProvider = new();
                sqlMetadataProvider
                    .SetupGet(provider => provider.EntityToDatabaseObject)
                    .Returns(() => Volatile.Read(ref currentMetadata));

                using ManualResetEventSlim initialMetadataInitializationEntered = new();
                TaskCompletionSource initialMetadataMayComplete = new(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                Mock<IMetadataProviderFactory> metadataProviderFactory = new();
                metadataProviderFactory
                    .Setup(factory => factory.GetMetadataProvider(It.IsAny<string>()))
                    .Returns(sqlMetadataProvider.Object);
                metadataProviderFactory
                    .Setup(factory => factory.InitializeAsync(It.IsAny<CancellationToken>()))
                    .Callback(initialMetadataInitializationEntered.Set)
                    .Returns(initialMetadataMayComplete.Task);

                McpToolRegistry registry = new();
                McpToolRegistryRefreshService refreshService = new(
                    runtimeConfigProvider.Object,
                    Array.Empty<IMcpTool>(),
                    registry,
                    metadataProviderFactory.Object,
                    Array.Empty<IMcpToolListChangedNotifier>(),
                    NullLogger<McpToolRegistryRefreshService>.Instance,
                    hotReloadEventHandler);

                RuntimeConfigValidator runtimeConfigValidator = new(
                    runtimeConfigProvider.Object,
                    fileSystem,
                    NullLogger<RuntimeConfigValidator>.Instance);
                using ServiceProvider serviceProvider = new ServiceCollection()
                    .AddSingleton(configLoader)
                    .AddSingleton(runtimeConfigProvider.Object)
                    .AddSingleton(runtimeConfigValidator)
                    .AddSingleton<IMetadataProviderFactory>(metadataProviderFactory.Object)
                    .AddSingleton<IMcpToolRegistryRefreshService>(refreshService)
                    .BuildServiceProvider();

                using ManualResetEventSlim reloadPausedBeforeMetadata = new();
                using ManualResetEventSlim reloadReachedGate = new();
                using ManualResetEventSlim releaseReload = new();
                hotReloadEventHandler.Subscribe(
                    QUERY_MANAGER_FACTORY_ON_CONFIG_CHANGED,
                    (_, _) =>
                    {
                        reloadPausedBeforeMetadata.Set();
                        if (!releaseReload.Wait(TimeSpan.FromSeconds(10)))
                        {
                            throw new TimeoutException("Timed out waiting to resume reload B.");
                        }
                    });
                hotReloadEventHandler.Subscribe(
                    METADATA_PROVIDER_FACTORY_ON_CONFIG_CHANGED,
                    (_, _) => Volatile.Write(
                        ref currentMetadata,
                        CreateStoredProcedureMetadata(
                            "b_database_parameter",
                            typeof(int),
                            DbType.Int32)));

                Task initialConstruction = Task.Factory.StartNew(
                    () => RuntimeInitializationHelper.InitializeRuntimeDependenciesAsync(
                        serviceProvider),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default).Unwrap();
                Task reloadB = Task.CompletedTask;

                try
                {
                    Assert.IsTrue(
                        initialMetadataInitializationEntered.Wait(TimeSpan.FromSeconds(10)),
                        "Initial metadata initialization for generation A did not start.");

                    File.WriteAllText(configPath, CreateRuntimeConfig("Generation B").ToJson());
                    reloadB = Task.Run(() => configLoader.ProcessHotReloadNotification(
                        beforeEnteringGate: reloadReachedGate.Set));
                    Assert.IsTrue(
                        reloadReachedGate.Wait(TimeSpan.FromSeconds(10)),
                        "Reload B did not reach the shared serialization gate.");

                    // Without startup serialization, B reaches this handler while A's metadata
                    // task is incomplete. Completing A then publishes a B/A candidate, and B's
                    // later MCP handler skips because B was incorrectly marked as applied.
                    bool reloadEnteredDuringInitialMetadata =
                        reloadPausedBeforeMetadata.Wait(TimeSpan.FromMilliseconds(500));
                    Assert.AreEqual(
                        0,
                        registry.GetAdvertisedTools().Count,
                        "No registry generation should publish before initial metadata completes.");

                    initialMetadataMayComplete.SetResult();
                    await initialConstruction.WaitAsync(TimeSpan.FromSeconds(10));

                    if (!reloadEnteredDuringInitialMetadata)
                    {
                        Assert.IsTrue(
                            reloadPausedBeforeMetadata.Wait(TimeSpan.FromSeconds(10)),
                            "Reload B did not pause before refreshing database metadata.");
                    }

                    releaseReload.Set();
                    await reloadB.WaitAsync(TimeSpan.FromSeconds(10));
                }
                finally
                {
                    initialMetadataMayComplete.TrySetResult();
                    releaseReload.Set();
                    await Task.WhenAll(reloadB, initialConstruction).WaitAsync(TimeSpan.FromSeconds(10));
                }

                Assert.IsTrue(initialMetadataInitializationEntered.IsSet);
                metadataProviderFactory.Verify(
                    factory => factory.InitializeAsync(It.IsAny<CancellationToken>()),
                    Times.Once);

                Tool advertisedTool = registry.GetAdvertisedTools().Single();
                Assert.AreEqual("get_book", advertisedTool.Name);
                Assert.AreEqual("Generation B", advertisedTool.Description);
                JsonElement properties = advertisedTool.InputSchema.GetProperty("properties");
                Assert.IsTrue(properties.TryGetProperty("b_database_parameter", out JsonElement parameter));
                Assert.AreEqual("integer", parameter.GetProperty("type").GetString());
                Assert.IsFalse(properties.TryGetProperty("a_database_parameter", out _));
                Assert.IsFalse(properties.TryGetProperty("config_parameter", out _));
            }
            finally
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }

        private static RuntimeConfig CreateRuntimeConfig(string description)
        {
            Entity entity = new(
                Source: new(
                    Object: "test_procedure",
                    Type: EntitySourceType.StoredProcedure,
                    Parameters: new List<ParameterMetadata>
                    {
                        new()
                        {
                            Name = "config_parameter",
                            Description = "Configuration fallback parameter",
                            Required = true
                        }
                    },
                    KeyFields: null),
                GraphQL: new(
                    Singular: "GetBook",
                    Plural: "GetBooks",
                    Enabled: false,
                    Operation: GraphQLOperation.Mutation),
                Rest: new(Enabled: true),
                Fields: null,
                Permissions: new[]
                {
                    new EntityPermission(
                        Role: "anonymous",
                        Actions: new[]
                        {
                            new EntityAction(
                                Action: EntityActionOperation.Execute,
                                Fields: null,
                                Policy: null)
                        })
                },
                Relationships: null,
                Mappings: null,
                Description: description,
                Mcp: new EntityMcpOptions(customToolEnabled: true, dmlToolsEnabled: false));

            return new RuntimeConfig(
                Schema: FileSystemRuntimeConfigLoader.SCHEMA,
                DataSource: new DataSource(
                    DatabaseType.MSSQL,
                    "Server=test;Database=test;User ID=test;Password=test;TrustServerCertificate=true",
                    Options: null),
                Runtime: new(
                    Rest: new(Enabled: true),
                    GraphQL: new(Enabled: false),
                    Mcp: new(Enabled: true, DmlTools: DmlToolsConfig.FromBoolean(false)),
                    Host: new(
                        Cors: null,
                        Authentication: new(
                            Provider: AuthenticationOptions.UNAUTHENTICATED_AUTHENTICATION),
                        Mode: HostMode.Development)),
                Entities: new(new Dictionary<string, Entity> { ["GetBook"] = entity }));
        }

        private static Dictionary<string, DatabaseObject> CreateStoredProcedureMetadata(
            string parameterName,
            Type systemType,
            DbType dbType)
        {
            DatabaseStoredProcedure storedProcedure = new("dbo", "test_procedure")
            {
                SourceType = EntitySourceType.StoredProcedure,
                StoredProcedureDefinition = new StoredProcedureDefinition
                {
                    Parameters = new Dictionary<string, ParameterDefinition>
                    {
                        [parameterName] = new ParameterDefinition
                        {
                            Name = parameterName,
                            Required = true,
                            SystemType = systemType,
                            DbType = dbType
                        }
                    }
                }
            };

            return new Dictionary<string, DatabaseObject> { ["GetBook"] = storedProcedure };
        }
    }
}
