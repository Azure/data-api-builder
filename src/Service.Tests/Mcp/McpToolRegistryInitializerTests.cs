// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Mcp.Core;
using Azure.DataApiBuilder.Mcp.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;
using Moq;
using static Azure.DataApiBuilder.Mcp.Model.McpEnums;

namespace Azure.DataApiBuilder.Service.Tests.Mcp
{
    [TestClass]
    public class McpToolRegistryInitializerTests
    {
        [TestMethod]
        public async Task LegacyFallback_UsesConfigAwareBulkReplacement()
        {
            RuntimeConfig config = CreateRuntimeConfig();
            Mock<RuntimeConfigLoader> configLoader = new(null, null);
            Mock<RuntimeConfigProvider> configProvider = new(configLoader.Object);
            configProvider.Setup(provider => provider.GetConfig()).Returns(config);

            ServiceCollection services = new();
            services.AddSingleton(configProvider.Object);
            services.AddSingleton<IMcpTool>(
                new TestMcpTool("enabled_tool", isEnabled: true));
            services.AddSingleton<IMcpTool>(
                new TestMcpTool("disabled_tool", isEnabled: false));
            using ServiceProvider serviceProvider = services.BuildServiceProvider();
            McpToolRegistry registry = new();
#pragma warning disable CS0618 // Explicitly exercises the documented compatibility fallback.
            McpToolRegistryInitializer initializer = new(serviceProvider, registry);
#pragma warning restore CS0618

            await initializer.StartAsync(CancellationToken.None);

            CollectionAssert.AreEqual(
                new[] { "enabled_tool" },
                registry.GetAdvertisedTools().Select(tool => tool.Name).ToArray());
            Assert.IsTrue(registry.TryGetTool("enabled_tool", out _));
            Assert.IsTrue(
                registry.TryGetTool("disabled_tool", out _),
                "Disabled built-ins remain registered so execution can return a structured disabled response.");
        }

        [TestMethod]
        public async Task LegacyFallback_WithoutRuntimeConfigProvider_Throws()
        {
            ServiceCollection services = new();
            services.AddSingleton<IMcpTool>(new TestMcpTool("test_tool", isEnabled: true));
            using ServiceProvider serviceProvider = services.BuildServiceProvider();
#pragma warning disable CS0618 // Explicitly exercises the documented compatibility fallback.
            McpToolRegistryInitializer initializer = new(serviceProvider, new McpToolRegistry());
#pragma warning restore CS0618

            InvalidOperationException exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => initializer.StartAsync(CancellationToken.None));

            StringAssert.Contains(exception.Message, nameof(RuntimeConfigProvider));
        }

        private static RuntimeConfig CreateRuntimeConfig()
        {
            return new RuntimeConfig(
                Schema: "test-schema",
                DataSource: new DataSource(DatabaseType.MSSQL, string.Empty, Options: null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(Enabled: true),
                    Host: new(Cors: null, Authentication: null, Mode: HostMode.Development)),
                Entities: new(new Dictionary<string, Entity>()));
        }

        private sealed class TestMcpTool : IMcpTool
        {
            private readonly string _name;
            private readonly bool _isEnabled;

            public TestMcpTool(string name, bool isEnabled)
            {
                _name = name;
                _isEnabled = isEnabled;
            }

            public ToolType ToolType => ToolType.BuiltIn;

            public bool IsEnabled(RuntimeConfig config) => _isEnabled;

            public Tool GetToolMetadata()
            {
                return new Tool
                {
                    Name = _name,
                    Description = "Test tool",
                    InputSchema = JsonSerializer.Deserialize<JsonElement>("{\"type\":\"object\"}")
                };
            }

            public Task<CallToolResult> ExecuteAsync(
                JsonDocument? arguments,
                IServiceProvider serviceProvider,
                CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }
        }
    }
}
