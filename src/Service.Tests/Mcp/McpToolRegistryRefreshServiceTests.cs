// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
using Microsoft.Extensions.Logging.Abstractions;
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

            Mock<IMcpToolListChangedNotifier> notifier = new();
            McpToolRegistry registry = new();
            HotReloadEventHandler<HotReloadEventArgs> hotReloadEventHandler = new();
            McpToolRegistryRefreshService service = new(
                configProvider.Object,
                builtInTools,
                registry,
                metadataProviderFactory.Object,
                new[] { notifier.Object },
                NullLogger<McpToolRegistryRefreshService>.Instance,
                hotReloadEventHandler);

            return new TestContext(
                service,
                registry,
                notifier,
                hotReloadEventHandler);
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

        private sealed record TestContext(
            McpToolRegistryRefreshService Service,
            McpToolRegistry Registry,
            Mock<IMcpToolListChangedNotifier> Notifier,
            HotReloadEventHandler<HotReloadEventArgs> HotReloadEventHandler);

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
