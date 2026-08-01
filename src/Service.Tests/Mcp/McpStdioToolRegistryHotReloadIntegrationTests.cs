// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Mcp.Core;
using Azure.DataApiBuilder.Mcp.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.Mcp
{
    [TestClass]
    public class McpStdioToolRegistryHotReloadIntegrationTests
    {
        [TestMethod]
        public async Task InitializedClient_FileReload_EmitsOneNotificationAndReturnsUpdatedList()
        {
            string testDirectory = Path.Combine(
                Path.GetTempPath(),
                $"dab-mcp-stdio-hot-reload-{Guid.NewGuid():N}");
            Directory.CreateDirectory(testDirectory);
            string configPath = Path.Combine(testDirectory, "dab-config.json");
            await WriteConfigAsync(configPath, CreateRuntimeConfig());

            try
            {
                HotReloadEventHandler<HotReloadEventArgs> hotReloadEventHandler = new();
                using FileSystemRuntimeConfigLoader fileLoader = new(
                    new FileSystem(),
                    hotReloadEventHandler,
                    configPath);
                Assert.IsTrue(fileLoader.TryLoadKnownConfig(out _));

                // The refresh service reads the real loader's active generation. Keep the provider
                // itself detached from the change token so this transport test does not invoke the
                // separate live-database configuration validator.
                Mock<RuntimeConfigLoader> providerLoader = new(null, null);
                Mock<RuntimeConfigProvider> configProvider = new(providerLoader.Object);
                configProvider
                    .Setup(provider => provider.GetConfig())
                    .Returns(() => fileLoader.RuntimeConfig!);

                Mock<ISqlMetadataProvider> sqlMetadataProvider = new();
                sqlMetadataProvider
                    .SetupGet(provider => provider.EntityToDatabaseObject)
                    .Returns(new Dictionary<string, DatabaseObject>());
                Mock<IMetadataProviderFactory> metadataProviderFactory = new();
                metadataProviderFactory
                    .Setup(factory => factory.GetMetadataProvider(It.IsAny<string>()))
                    .Returns(sqlMetadataProvider.Object);

                McpToolRegistry registry = new();
                ChannelTextReader stdin = new();
                ChannelTextWriter stdout = new();
                using McpStdoutWriter stdoutWriter = new(stdout);
                McpStdioToolListChangedNotifier notifier = new(stdoutWriter);
                using ServiceProvider serviceProvider = new ServiceCollection()
                    .AddSingleton(stdoutWriter)
                    .AddSingleton<IMcpStdioToolListChangedNotifier>(notifier)
                    .AddSingleton(configProvider.Object)
                    .BuildServiceProvider();

                McpToolRegistryRefreshService refreshService = new(
                    configProvider.Object,
                    Array.Empty<IMcpTool>(),
                    registry,
                    metadataProviderFactory.Object,
                    new IMcpToolListChangedNotifier[] { notifier },
                    NullLogger<McpToolRegistryRefreshService>.Instance,
                    hotReloadEventHandler);
                refreshService.EnsureInitialized();

                McpStdioServer server = new(registry, serviceProvider, stdin);
                using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
                Task serverTask = server.RunAsync(timeout.Token);

                stdin.WriteLine(
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-11-25\",\"capabilities\":{},\"clientInfo\":{\"name\":\"test\",\"version\":\"1.0\"}}}");
                stdin.WriteLine("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
                stdin.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"ping\"}");

                using JsonDocument initializeResponse = await ReadJsonLineAsync(stdout, timeout.Token);
                Assert.IsTrue(
                    initializeResponse.RootElement
                        .GetProperty("result")
                        .GetProperty("capabilities")
                        .GetProperty("tools")
                        .GetProperty("listChanged")
                        .GetBoolean());

                using JsonDocument pingResponse = await ReadJsonLineAsync(stdout, timeout.Token);
                Assert.AreEqual(2, pingResponse.RootElement.GetProperty("id").GetInt32(),
                    "The ping response is a barrier proving the initialized notification was processed.");

                await WriteConfigAsync(
                    configPath,
                    CreateRuntimeConfig(("GetBook", "Gets one book")));

                using JsonDocument notification = await ReadJsonLineAsync(stdout, timeout.Token);
                Assert.AreEqual(
                    "notifications/tools/list_changed",
                    notification.RootElement.GetProperty("method").GetString());
                Assert.IsFalse(notification.RootElement.TryGetProperty("id", out _));

                stdin.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/list\"}");
                stdin.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"shutdown\"}");

                using JsonDocument listResponse = await ReadJsonLineAsync(stdout, timeout.Token);
                Assert.AreEqual(
                    3,
                    listResponse.RootElement.GetProperty("id").GetInt32(),
                    "An extra notification would displace the list response and fail this barrier.");
                JsonElement tool = listResponse.RootElement
                    .GetProperty("result")
                    .GetProperty("tools")
                    .EnumerateArray()
                    .Single();
                Assert.AreEqual("get_book", tool.GetProperty("name").GetString());
                Assert.AreEqual("Gets one book", tool.GetProperty("description").GetString());

                using JsonDocument shutdownResponse = await ReadJsonLineAsync(stdout, timeout.Token);
                Assert.AreEqual(
                    4,
                    shutdownResponse.RootElement.GetProperty("id").GetInt32(),
                    "Exactly one notification should be emitted for one net-new file content.");
                await serverTask;
            }
            finally
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }

        private static async Task<JsonDocument> ReadJsonLineAsync(
            ChannelTextWriter output,
            CancellationToken cancellationToken)
        {
            string line = await output.ReadLineAsync(cancellationToken);
            return JsonDocument.Parse(line);
        }

        private static async Task WriteConfigAsync(string configPath, RuntimeConfig config)
        {
            const int MAX_ATTEMPTS = 20;
            for (int attempt = 1; attempt <= MAX_ATTEMPTS; attempt++)
            {
                try
                {
                    await File.WriteAllTextAsync(configPath, config.ToJson());
                    return;
                }
                catch (IOException) when (attempt < MAX_ATTEMPTS)
                {
                    await Task.Delay(25);
                }
            }
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
                DataSource: new DataSource(DatabaseType.MSSQL, string.Empty, Options: null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(Enabled: true),
                    Host: new(Cors: null, Authentication: null, Mode: HostMode.Development)),
                Entities: new(entities));
        }

        private sealed class ChannelTextReader : TextReader
        {
            private readonly Channel<string> _lines = Channel.CreateUnbounded<string>();

            public void WriteLine(string line)
            {
                Assert.IsTrue(_lines.Writer.TryWrite(line));
            }

            public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
            {
                return await _lines.Reader.ReadAsync(cancellationToken);
            }
        }

        private sealed class ChannelTextWriter : StringWriter
        {
            private readonly Channel<string> _lines = Channel.CreateUnbounded<string>();

            public override Encoding Encoding => Encoding.UTF8;

            public override void WriteLine(string? value)
            {
                base.WriteLine(value);
                Assert.IsTrue(_lines.Writer.TryWrite(value ?? string.Empty));
            }

            public async ValueTask<string> ReadLineAsync(CancellationToken cancellationToken)
            {
                return await _lines.Reader.ReadAsync(cancellationToken);
            }
        }
    }
}
