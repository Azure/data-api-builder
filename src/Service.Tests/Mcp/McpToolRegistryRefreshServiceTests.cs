// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
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
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;
using Moq;
using static Azure.DataApiBuilder.Config.DabConfigEvents;
using static Azure.DataApiBuilder.Mcp.Model.McpEnums;

namespace Azure.DataApiBuilder.Service.Tests.Mcp
{
    [TestClass]
    public class McpToolRegistryRefreshServiceTests
    {
        [TestMethod]
        public void EnsureInitialized_IsIdempotentForSameConfig()
        {
            RuntimeConfig currentConfig = CreateRuntimeConfig();
            TestContext context = CreateContext(
                () => currentConfig,
                new TestMcpTool("read_records", ToolType.BuiltIn));

            context.Service.EnsureInitialized();
            IMcpTool? initialTool = GetRequiredTool(context.Registry, "read_records");
            context.Service.EnsureInitialized();

            Assert.AreSame(initialTool, GetRequiredTool(context.Registry, "read_records"));
            Assert.AreEqual(1, context.Registry.GetAdvertisedTools().Count);
            context.Notifier.Verify(notifier => notifier.NotifyToolsListChanged(), Times.Never);
        }

        [TestMethod]
        public async Task HostedStart_DefersInitializationToStartupOrchestrator()
        {
            RuntimeConfig currentConfig = CreateRuntimeConfig();
            TestContext context = CreateContext(
                () => currentConfig,
                new TestMcpTool("read_records", ToolType.BuiltIn));

            await context.Service.StartAsync(CancellationToken.None);

            Assert.AreEqual(0, context.Registry.GetAdvertisedTools().Count,
                "Hosted service startup occurs before metadata initialization and must not publish.");

            context.Service.EnsureInitialized();

            Assert.AreEqual(1, context.Registry.GetAdvertisedTools().Count,
                "The startup orchestrator should publish after metadata initialization.");
        }

        [TestMethod]
        public void HotReload_AddsFreshCustomToolAndNotifiesClient()
        {
            RuntimeConfig currentConfig = CreateRuntimeConfig();
            TestContext context = CreateContext(
                () => currentConfig,
                new TestMcpTool("read_records", ToolType.BuiltIn));
            context.Service.EnsureInitialized();

            currentConfig = CreateRuntimeConfig(("GetBook", "Gets one book"));
            RaiseRegistryChanged(context.HotReloadEventHandler);

            IMcpTool customTool = GetRequiredTool(context.Registry, "get_book");
            Assert.IsInstanceOfType<DynamicCustomTool>(customTool);
            Assert.AreEqual(
                "Gets one book",
                context.Registry.GetAdvertisedTools().Single(tool => tool.Name == "get_book").Description);
            context.Notifier.Verify(notifier => notifier.NotifyToolsListChanged(), Times.Once);
        }

        [TestMethod]
        public void HotReload_PreservesExplicitlyDiRegisteredCustomTool()
        {
            RuntimeConfig currentConfig = CreateRuntimeConfig();
            Mock<RuntimeConfigLoader> configLoader = new(null, null);
            configLoader.Object.RuntimeConfig = currentConfig;
            Mock<RuntimeConfigProvider> configProvider = new(configLoader.Object);
            configProvider.Setup(provider => provider.GetConfig()).Returns(() => currentConfig);

            Mock<ISqlMetadataProvider> sqlMetadataProvider = new();
            sqlMetadataProvider
                .SetupGet(provider => provider.EntityToDatabaseObject)
                .Returns(new Dictionary<string, DatabaseObject>());
            Mock<IMetadataProviderFactory> metadataProviderFactory = new();
            metadataProviderFactory
                .Setup(factory => factory.GetMetadataProvider(It.IsAny<string>()))
                .Returns(sqlMetadataProvider.Object);

            TestMcpTool registeredCustomTool = new("extension_tool", ToolType.Custom);
            HotReloadEventHandler<HotReloadEventArgs> hotReloadEventHandler = new();
            ServiceCollection services = new();
            services.AddLogging();
            services.AddSingleton(configProvider.Object);
            services.AddSingleton(metadataProviderFactory.Object);
            services.AddSingleton(hotReloadEventHandler);
            services.AddSingleton<IMcpTool>(registeredCustomTool);
            services.AddDabMcpServer(configProvider.Object);
            using ServiceProvider serviceProvider = services.BuildServiceProvider();
            McpToolRegistryRefreshService refreshService = serviceProvider
                .GetRequiredService<McpToolRegistryRefreshService>();
            McpToolRegistry registry = serviceProvider.GetRequiredService<McpToolRegistry>();

            refreshService.EnsureInitialized();
            Assert.IsTrue(registry.TryGetTool("extension_tool", out IMcpTool? initialTool));
            Assert.AreSame(registeredCustomTool, initialTool);
            Assert.IsTrue(registry.GetAdvertisedTools().Any(tool => tool.Name == "extension_tool"));

            currentConfig = CreateRuntimeConfig();
            RaiseRegistryChanged(hotReloadEventHandler);

            Assert.IsTrue(registry.TryGetTool("extension_tool", out IMcpTool? refreshedTool));
            Assert.AreSame(
                registeredCustomTool,
                refreshedTool,
                "Independent DI-owned tools must remain published across configuration generations.");
            Assert.IsTrue(registry.GetAdvertisedTools().Any(tool => tool.Name == "extension_tool"));
        }

        [TestMethod]
        public void HotReload_ReplacesCustomToolInstanceAndMetadata()
        {
            RuntimeConfig currentConfig = CreateRuntimeConfig(("GetBook", "Old description"));
            TestContext context = CreateContext(() => currentConfig);
            context.Service.EnsureInitialized();
            IMcpTool oldTool = GetRequiredTool(context.Registry, "get_book");

            currentConfig = CreateRuntimeConfig(("GetBook", "New description"));
            RaiseRegistryChanged(context.HotReloadEventHandler);

            IMcpTool newTool = GetRequiredTool(context.Registry, "get_book");
            Assert.AreNotSame(oldTool, newTool);
            Assert.AreEqual(
                "New description",
                context.Registry.GetAdvertisedTools().Single().Description);
            context.Notifier.Verify(notifier => notifier.NotifyToolsListChanged(), Times.Once);
        }

        [TestMethod]
        public void HotReload_WithDuplicateToolName_PreservesPreviousRegistry()
        {
            RuntimeConfig currentConfig = CreateRuntimeConfig();
            TestMcpTool builtIn = new("read_records", ToolType.BuiltIn);
            TestContext context = CreateContext(() => currentConfig, builtIn);
            context.Service.EnsureInitialized();

            currentConfig = CreateRuntimeConfig(("ReadRecords", "Conflicting custom tool"));
            RaiseRegistryChanged(context.HotReloadEventHandler);

            Assert.AreSame(builtIn, GetRequiredTool(context.Registry, "read_records"));
            Assert.AreEqual(1, context.Registry.GetAdvertisedTools().Count);
            context.Notifier.Verify(notifier => notifier.NotifyToolsListChanged(), Times.Never);
        }

        [TestMethod]
        public void EnsureInitialized_WithDuplicateToolName_Throws()
        {
            RuntimeConfig currentConfig = CreateRuntimeConfig(("ReadRecords", "Conflicting custom tool"));
            TestContext context = CreateContext(
                () => currentConfig,
                new TestMcpTool("read_records", ToolType.BuiltIn));

            Assert.ThrowsException<DataApiBuilderException>(context.Service.EnsureInitialized);
            Assert.AreEqual(0, context.Registry.GetAdvertisedTools().Count);
        }

        [TestMethod]
        public void HotReload_WithEquivalentDiscoveryMetadata_DoesNotNotify()
        {
            RuntimeConfig currentConfig = CreateRuntimeConfig();
            TestContext context = CreateContext(
                () => currentConfig,
                new TestMcpTool("read_records", ToolType.BuiltIn));
            context.Service.EnsureInitialized();

            currentConfig = CreateRuntimeConfig();
            RaiseRegistryChanged(context.HotReloadEventHandler);

            context.Notifier.Verify(notifier => notifier.NotifyToolsListChanged(), Times.Never);
        }

        [TestMethod]
        public void EnsureInitialized_WhenDatabaseMetadataUnavailable_PublishesConfigFallbackSchema()
        {
            RuntimeConfig currentConfig = CreateRuntimeConfigWithParameter(
                parameterDescription: "Configured identifier");
            TestContext context = CreateContext(() => currentConfig);

            context.Service.EnsureInitialized();

            Tool customTool = context.Registry.GetAdvertisedTools().Single();
            JsonElement properties = customTool.InputSchema.GetProperty("properties");
            JsonElement idSchema = properties.GetProperty("id");
            CollectionAssert.AreEqual(
                new[] { "string", "number", "boolean", "null" },
                idSchema.GetProperty("type").EnumerateArray().Select(value => value.GetString()).ToArray());
            Assert.AreEqual("Configured identifier", idSchema.GetProperty("description").GetString());
            CollectionAssert.AreEqual(
                new[] { "id" },
                customTool.InputSchema.GetProperty("required")
                    .EnumerateArray()
                    .Select(value => value.GetString())
                    .ToArray());
            VerifyLogContains(
                context.Logger,
                LogLevel.Warning,
                "Reason: Database metadata for entity 'GetBook' was not available from data source");
            VerifyLogContains(
                context.Logger,
                LogLevel.Information,
                "with 0 built-in tools, 0 DI-registered custom tools, " +
                "1 configuration-generated custom tools, 1 registered tools, and 1 advertised tools. " +
                "Discovery changed: True.");
        }

        [TestMethod]
        public void HotReload_WithInputSchemaOnlyChange_NotifiesClient()
        {
            RuntimeConfig currentConfig = CreateRuntimeConfigWithParameter("Old parameter description");
            TestContext context = CreateContext(() => currentConfig);
            context.Service.EnsureInitialized();

            currentConfig = CreateRuntimeConfigWithParameter("New parameter description");
            RaiseRegistryChanged(context.HotReloadEventHandler);

            Assert.AreEqual(
                "New parameter description",
                context.Registry.GetAdvertisedTools()
                    .Single()
                    .InputSchema
                    .GetProperty("properties")
                    .GetProperty("id")
                    .GetProperty("description")
                    .GetString());
            context.Notifier.Verify(notifier => notifier.NotifyToolsListChanged(), Times.Once);
        }

        [TestMethod]
        public void HotReload_WhenNotifierThrows_PreservesPublicationAndContinuesNotifying()
        {
            RuntimeConfig currentConfig = CreateRuntimeConfig();
            ThrowingNotifier throwingNotifier = new();
            Mock<IMcpToolListChangedNotifier> healthyNotifier = new();
            TestContext context = CreateContextWithNotifiers(
                () => currentConfig,
                healthyNotifier,
                new IMcpToolListChangedNotifier[] { throwingNotifier, healthyNotifier.Object });
            context.Service.EnsureInitialized();

            currentConfig = CreateRuntimeConfig(("GetBook", "New tool"));
            RaiseRegistryChanged(context.HotReloadEventHandler);

            Assert.IsTrue(context.Registry.TryGetTool("get_book", out _));
            Assert.AreEqual(1, throwingNotifier.CallCount);
            healthyNotifier.Verify(notifier => notifier.NotifyToolsListChanged(), Times.Once);
        }

        [TestMethod]
        public async Task HotReload_WhenNotifierBlocks_DoesNotBlockPublicationOrLaterHandlers()
        {
            RuntimeConfig currentConfig = CreateRuntimeConfig();
            BlockingStringWriter output = new();
            using McpStdoutWriter stdoutWriter = new(output);
            McpStdioToolListChangedNotifier notifier = new(stdoutWriter);
            notifier.MarkInitialized();
            ManualResetEventSlim laterHandlerCalled = new();
            TestContext context = CreateContextWithNotifiers(
                () => currentConfig,
                new Mock<IMcpToolListChangedNotifier>(),
                new IMcpToolListChangedNotifier[] { notifier });
            context.HotReloadEventHandler.Subscribe(
                GRAPHQL_SCHEMA_EVICTION_ON_CONFIG_CHANGED,
                (_, _) => laterHandlerCalled.Set());
            context.Service.EnsureInitialized();

            currentConfig = CreateRuntimeConfig(("FirstTool", "First generation"));
            TestRuntimeConfigLoader loader = new(context.HotReloadEventHandler)
            {
                RuntimeConfig = currentConfig
            };
            Task firstRefresh = Task.Run(loader.RaiseConfigChanged);

            try
            {
                Assert.IsTrue(
                    output.WriteEntered.Wait(TimeSpan.FromSeconds(5)),
                    "The stdio notification worker did not reach the blocking writer.");
                Assert.IsTrue(
                    laterHandlerCalled.Wait(TimeSpan.FromSeconds(5)),
                    "A blocked transport must not prevent later ordered hot-reload handlers.");
                Assert.IsTrue(context.Registry.TryGetTool("first_tool", out _));
            }
            finally
            {
                output.ReleaseWrite.Set();
                await firstRefresh.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.IsTrue(
                    output.LineWritten.Wait(TimeSpan.FromSeconds(5)),
                    "The queued notification did not finish after stdout resumed.");
            }
        }

        [TestMethod]
        public void HotReload_AfterRejectedCandidate_RecoversOnNextConfig()
        {
            RuntimeConfig currentConfig = CreateRuntimeConfig();
            TestMcpTool builtIn = new("read_records", ToolType.BuiltIn);
            TestContext context = CreateContext(() => currentConfig, builtIn);
            context.Service.EnsureInitialized();

            currentConfig = CreateRuntimeConfig(("ReadRecords", "Conflicting custom tool"));
            RaiseRegistryChanged(context.HotReloadEventHandler);
            Assert.AreSame(builtIn, GetRequiredTool(context.Registry, "read_records"));
            Assert.IsFalse(context.Registry.TryGetTool("get_book", out _));

            currentConfig = CreateRuntimeConfig(("GetBook", "Recovered tool"));
            RaiseRegistryChanged(context.HotReloadEventHandler);

            Assert.IsTrue(context.Registry.TryGetTool("get_book", out _));
            Assert.AreEqual(
                "Recovered tool",
                context.Registry.GetAdvertisedTools().Single(tool => tool.Name == "get_book").Description);
            context.Notifier.Verify(notifier => notifier.NotifyToolsListChanged(), Times.Once);
        }

        [TestMethod]
        public void HotReload_WithSuccessiveConfigurations_PublishesLatestGeneration()
        {
            RuntimeConfig currentConfig = CreateRuntimeConfig();
            TestContext context = CreateContext(() => currentConfig);
            context.Service.EnsureInitialized();

            currentConfig = CreateRuntimeConfig(("FirstTool", "First generation"));
            RaiseRegistryChanged(context.HotReloadEventHandler);
            currentConfig = CreateRuntimeConfig(("LatestTool", "Latest generation"));
            RaiseRegistryChanged(context.HotReloadEventHandler);

            Assert.IsFalse(context.Registry.TryGetTool("first_tool", out _));
            Assert.IsTrue(context.Registry.TryGetTool("latest_tool", out _));
            CollectionAssert.AreEqual(
                new[] { "latest_tool" },
                context.Registry.GetAdvertisedTools().Select(tool => tool.Name).ToArray());
            context.Notifier.Verify(notifier => notifier.NotifyToolsListChanged(), Times.Exactly(2));
        }

        [TestMethod]
        public void HotReload_DiscardsCandidateWhenNewerConfigBecomesActive()
        {
            RuntimeConfig initialConfig = CreateRuntimeConfig();
            RuntimeConfig candidateConfig = CreateRuntimeConfig();
            RuntimeConfig newerConfig = CreateRuntimeConfig(("GetBook", "Newer config"));
            RuntimeConfig currentConfig = initialConfig;
            int metadataReadCount = 0;
            TestMcpTool builtIn = new(
                "read_records",
                ToolType.BuiltIn,
                metadataFactory: () =>
                {
                    metadataReadCount++;
                    if (metadataReadCount == 2)
                    {
                        currentConfig = newerConfig;
                    }

                    return CreateMetadata("read_records", "Built-in tool");
                });
            TestContext context = CreateContext(() => currentConfig, builtIn);
            context.Service.EnsureInitialized();

            currentConfig = candidateConfig;
            RaiseRegistryChanged(context.HotReloadEventHandler);

            Assert.AreSame(builtIn, GetRequiredTool(context.Registry, "read_records"));
            Assert.IsFalse(context.Registry.TryGetTool("get_book", out _));
            context.Notifier.Verify(notifier => notifier.NotifyToolsListChanged(), Times.Never);
        }

        [TestMethod]
        public void RuntimeConfigLoader_RaisesMcpEventAfterDependenciesAndBeforeGraphQL()
        {
            List<string> events = new();
            HotReloadEventHandler<HotReloadEventArgs> hotReloadEventHandler = new();
            hotReloadEventHandler.Subscribe(
                METADATA_PROVIDER_FACTORY_ON_CONFIG_CHANGED,
                (_, _) => events.Add(METADATA_PROVIDER_FACTORY_ON_CONFIG_CHANGED));
            hotReloadEventHandler.Subscribe(
                AUTHZ_RESOLVER_ON_CONFIG_CHANGED,
                (_, _) => events.Add(AUTHZ_RESOLVER_ON_CONFIG_CHANGED));
            hotReloadEventHandler.Subscribe(
                MCP_TOOL_REGISTRY_ON_CONFIG_CHANGED,
                (_, _) => events.Add(MCP_TOOL_REGISTRY_ON_CONFIG_CHANGED));
            hotReloadEventHandler.Subscribe(
                GRAPHQL_SCHEMA_EVICTION_ON_CONFIG_CHANGED,
                (_, _) => events.Add(GRAPHQL_SCHEMA_EVICTION_ON_CONFIG_CHANGED));

            TestRuntimeConfigLoader loader = new(hotReloadEventHandler)
            {
                RuntimeConfig = CreateRuntimeConfig()
            };

            loader.RaiseConfigChanged();

            CollectionAssert.AreEqual(
                new[]
                {
                    METADATA_PROVIDER_FACTORY_ON_CONFIG_CHANGED,
                    AUTHZ_RESOLVER_ON_CONFIG_CHANGED,
                    MCP_TOOL_REGISTRY_ON_CONFIG_CHANGED,
                    GRAPHQL_SCHEMA_EVICTION_ON_CONFIG_CHANGED
                },
                events);
        }

        private static TestContext CreateContext(
            Func<RuntimeConfig> getConfig,
            params IMcpTool[] builtInTools)
        {
            Mock<IMcpToolListChangedNotifier> notifier = new();
            return CreateContextWithNotifiers(
                getConfig,
                notifier,
                new[] { notifier.Object },
                builtInTools);
        }

        private static TestContext CreateContextWithNotifiers(
            Func<RuntimeConfig> getConfig,
            Mock<IMcpToolListChangedNotifier> primaryNotifier,
            IEnumerable<IMcpToolListChangedNotifier> notifiers,
            params IMcpTool[] builtInTools)
        {
            Mock<RuntimeConfigLoader> configLoader = new(null, null);
            Mock<RuntimeConfigProvider> configProvider = new(configLoader.Object);
            configProvider.Setup(provider => provider.GetConfig()).Returns(getConfig);

            Mock<ISqlMetadataProvider> sqlMetadataProvider = new();
            sqlMetadataProvider
                .SetupGet(provider => provider.EntityToDatabaseObject)
                .Returns(new Dictionary<string, DatabaseObject>());
            Mock<IMetadataProviderFactory> metadataProviderFactory = new();
            metadataProviderFactory
                .Setup(factory => factory.GetMetadataProvider(It.IsAny<string>()))
                .Returns(sqlMetadataProvider.Object);

            McpToolRegistry registry = new();
            HotReloadEventHandler<HotReloadEventArgs> hotReloadEventHandler = new();
            Mock<ILogger<McpToolRegistryRefreshService>> logger = new();
            McpToolRegistryRefreshService service = new(
                configProvider.Object,
                builtInTools,
                registry,
                metadataProviderFactory.Object,
                notifiers,
                logger.Object,
                hotReloadEventHandler);

            return new TestContext(
                service,
                registry,
                primaryNotifier,
                hotReloadEventHandler,
                logger);
        }

        private static RuntimeConfig CreateRuntimeConfig(
            params (string EntityName, string Description)[] customTools)
        {
            Dictionary<string, Entity> entities = customTools.ToDictionary(
                item => item.EntityName,
                item => new Entity(
                    Source: new("test_procedure", EntitySourceType.StoredProcedure, Parameters: null, KeyFields: null),
                    GraphQL: new(item.EntityName, item.EntityName),
                    Rest: new(Enabled: true),
                    Fields: null,
                    Permissions: new[]
                    {
                        new EntityPermission(
                            Role: "anonymous",
                            Actions: new[]
                            {
                                new EntityAction(Action: EntityActionOperation.Execute, Fields: null, Policy: null)
                            })
                    },
                    Relationships: null,
                    Mappings: null,
                    Description: item.Description,
                    Mcp: new EntityMcpOptions(customToolEnabled: true, dmlToolsEnabled: null)));

            return new RuntimeConfig(
                Schema: "test-schema",
                DataSource: new DataSource(DatabaseType.MSSQL, "", Options: null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(Enabled: true),
                    Host: new(Cors: null, Authentication: null, Mode: HostMode.Development)),
                Entities: new(entities));
        }

        private static RuntimeConfig CreateRuntimeConfigWithParameter(string parameterDescription)
        {
            Entity entity = new(
                Source: new(
                    "test_procedure",
                    EntitySourceType.StoredProcedure,
                    Parameters: new List<ParameterMetadata>
                    {
                        new()
                        {
                            Name = "id",
                            Description = parameterDescription,
                            Required = true
                        }
                    },
                    KeyFields: null),
                GraphQL: new("GetBook", "GetBooks"),
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
                Description: "Stable tool description",
                Mcp: new EntityMcpOptions(customToolEnabled: true, dmlToolsEnabled: null));

            return new RuntimeConfig(
                Schema: "test-schema",
                DataSource: new DataSource(DatabaseType.MSSQL, string.Empty, Options: null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(Enabled: true),
                    Host: new(Cors: null, Authentication: null, Mode: HostMode.Development)),
                Entities: new(new Dictionary<string, Entity> { ["GetBook"] = entity }));
        }

        private static IMcpTool GetRequiredTool(McpToolRegistry registry, string name)
        {
            Assert.IsTrue(registry.TryGetTool(name, out IMcpTool? tool));
            Assert.IsNotNull(tool);
            return tool;
        }

        private static void RaiseRegistryChanged(
            HotReloadEventHandler<HotReloadEventArgs> hotReloadEventHandler)
        {
            hotReloadEventHandler.OnConfigChangedEvent(
                hotReloadEventHandler,
                new HotReloadEventArgs(MCP_TOOL_REGISTRY_ON_CONFIG_CHANGED, string.Empty));
        }

        private static Tool CreateMetadata(string name, string description)
        {
            return new Tool
            {
                Name = name,
                Description = description,
                InputSchema = JsonSerializer.Deserialize<JsonElement>("{\"type\":\"object\"}")
            };
        }

        private static void VerifyLogContains(
            Mock<ILogger<McpToolRegistryRefreshService>> logger,
            LogLevel logLevel,
            string expectedMessage)
        {
            logger.Verify(
                value => value.Log(
                    logLevel,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((state, _) =>
                        state.ToString()!.Contains(expectedMessage, StringComparison.Ordinal)),
                    It.IsAny<Exception>(),
                    (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()),
                Times.Once);
        }

        private sealed record TestContext(
            McpToolRegistryRefreshService Service,
            McpToolRegistry Registry,
            Mock<IMcpToolListChangedNotifier> Notifier,
            HotReloadEventHandler<HotReloadEventArgs> HotReloadEventHandler,
            Mock<ILogger<McpToolRegistryRefreshService>> Logger);

        private sealed class TestMcpTool : IMcpTool
        {
            private readonly string _name;
            private readonly Func<Tool>? _metadataFactory;

            public TestMcpTool(
                string name,
                ToolType toolType,
                Func<Tool>? metadataFactory = null)
            {
                _name = name;
                ToolType = toolType;
                _metadataFactory = metadataFactory;
            }

            public ToolType ToolType { get; }

            public Tool GetToolMetadata()
            {
                return _metadataFactory?.Invoke() ?? CreateMetadata(_name, "Test tool");
            }

            public bool IsEnabled(RuntimeConfig config) => true;

            public Task<CallToolResult> ExecuteAsync(
                JsonDocument? arguments,
                IServiceProvider serviceProvider,
                CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }
        }

        private sealed class ThrowingNotifier : IMcpToolListChangedNotifier
        {
            public int CallCount { get; private set; }

            public void NotifyToolsListChanged()
            {
                CallCount++;
                throw new InvalidOperationException("Expected notification failure.");
            }
        }

        private sealed class BlockingStringWriter : StringWriter
        {
            public ManualResetEventSlim WriteEntered { get; } = new();

            public ManualResetEventSlim ReleaseWrite { get; } = new();

            public ManualResetEventSlim LineWritten { get; } = new();

            public override void WriteLine(string? value)
            {
                WriteEntered.Set();
                if (!ReleaseWrite.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException("Timed out waiting to release the stdout write.");
                }

                base.WriteLine(value);
                LineWritten.Set();
            }
        }

        private sealed class TestRuntimeConfigLoader : RuntimeConfigLoader
        {
            public TestRuntimeConfigLoader(HotReloadEventHandler<HotReloadEventArgs> handler)
                : base(handler)
            {
            }

            public void RaiseConfigChanged()
            {
                SignalConfigChanged();
            }

            public override bool TryLoadKnownConfig(
                [NotNullWhen(true)] out RuntimeConfig? config,
                bool replaceEnvVar = false)
            {
                config = RuntimeConfig;
                return config is not null;
            }

            public override string GetPublishedDraftSchemaLink()
            {
                return string.Empty;
            }
        }
    }
}
