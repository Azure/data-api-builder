// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Azure.DataApiBuilder.Mcp.Core;
using Azure.DataApiBuilder.Mcp.Model;
using Azure.DataApiBuilder.Service.Tests.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass]
    public class McpStdioServerRunAsyncTests
    {
        [TestMethod]
        public async Task RunAsync_EofOnStdin_ExitsGracefullyWithoutOutput()
        {
            // Empty input immediately yields EOF (ReadLineAsync returns null).
            (McpStdioServer server, StringWriter stdoutCapture) =
                CreateServerWithCapturedOutput(new StringReader(string.Empty));

            await server.RunAsync(CancellationToken.None);

            Assert.AreEqual(string.Empty, stdoutCapture.ToString(),
                "Server should exit cleanly on EOF without emitting protocol output.");
        }

        [TestMethod]
        public async Task RunAsync_BlankLineThenShutdown_IgnoresBlankLineAndHandlesShutdown()
        {
            (McpStdioServer server, StringWriter stdoutCapture) =
                CreateServerWithCapturedOutput(new StringReader(Environment.NewLine +
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"shutdown\"}" +
                    Environment.NewLine));

            await server.RunAsync(CancellationToken.None);

            string[] lines = stdoutCapture
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

            Assert.AreEqual(1, lines.Length,
                "Expected a single response line for shutdown request.");

            using JsonDocument response = JsonDocument.Parse(lines[0]);
            JsonElement root = response.RootElement;

            Assert.AreEqual("2.0", root.GetProperty("jsonrpc").GetString(),
                "Expected jsonrpc version 2.0 in shutdown response.");
            Assert.AreEqual(1, root.GetProperty("id").GetInt32(),
                "Expected shutdown response id to match request id.");
            Assert.IsTrue(root.GetProperty("result").GetProperty("ok").GetBoolean(),
                "Expected shutdown response result.ok to be true.");
        }

        [TestMethod]
        public async Task RunAsync_CompleteInitializationHandshake_MarksToolListNotifierReady()
        {
            Mock<IMcpStdioToolListChangedNotifier> notifier = new();
            string input =
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-11-25\",\"capabilities\":{},\"clientInfo\":{\"name\":\"test\",\"version\":\"1.0\"}}}" + Environment.NewLine +
                "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}" + Environment.NewLine +
                "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"shutdown\"}" + Environment.NewLine;
            (McpStdioServer server, _) = CreateServerWithCapturedOutput(
                new StringReader(input),
                notifier.Object);

            await server.RunAsync(CancellationToken.None);

            notifier.Verify(value => value.MarkInitialized(), Times.Once);
        }

        [TestMethod]
        public async Task RunAsync_InitializedNotificationBeforeInitialize_DoesNotMarkNotifierReady()
        {
            Mock<IMcpStdioToolListChangedNotifier> notifier = new();
            string input =
                "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}" + Environment.NewLine +
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"shutdown\"}" + Environment.NewLine;
            (McpStdioServer server, _) = CreateServerWithCapturedOutput(
                new StringReader(input),
                notifier.Object);

            await server.RunAsync(CancellationToken.None);

            notifier.Verify(value => value.MarkInitialized(), Times.Never);
        }

        [TestMethod]
        public async Task RunAsync_OutOfRangeNumericId_PreservesIdAndContinuesProcessing()
        {
            string input =
                "{\"jsonrpc\":\"2.0\",\"id\":1e400,\"method\":\"ping\"}" + Environment.NewLine +
                "{\"jsonrpc\":\"2.0\",\"id\":-1e400,\"method\":\"unknown\"}" + Environment.NewLine +
                "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"shutdown\"}" + Environment.NewLine;
            (McpStdioServer server, StringWriter stdoutCapture) =
                CreateServerWithCapturedOutput(new StringReader(input));

            await server.RunAsync(CancellationToken.None);

            string[] lines = stdoutCapture
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            Assert.AreEqual(3, lines.Length, "The server should continue after writing out-of-range numeric IDs.");

            using JsonDocument firstResponse = JsonDocument.Parse(lines[0]);
            Assert.AreEqual("1e400", firstResponse.RootElement.GetProperty("id").GetRawText());
            Assert.IsTrue(firstResponse.RootElement.GetProperty("result").GetProperty("ok").GetBoolean());

            using JsonDocument errorResponse = JsonDocument.Parse(lines[1]);
            Assert.AreEqual("-1e400", errorResponse.RootElement.GetProperty("id").GetRawText());
            Assert.AreEqual(
                McpStdioJsonRpcErrorCodes.METHOD_NOT_FOUND,
                errorResponse.RootElement.GetProperty("error").GetProperty("code").GetInt32());

            using JsonDocument shutdownResponse = JsonDocument.Parse(lines[2]);
            Assert.AreEqual(2, shutdownResponse.RootElement.GetProperty("id").GetInt32());
            Assert.IsTrue(shutdownResponse.RootElement.GetProperty("result").GetProperty("ok").GetBoolean());
        }

        [DataTestMethod]
        [DataRow(false, false)]
        [DataRow(true, false)]
        [DataRow(false, true)]
        [DataRow(true, true)]
        public async Task RunAsync_InitializeRespondsBeforeMetadataInferenceCompletes(bool telemetryEnabled, bool callToolFirst)
        {
            string input =
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{}}\n" +
                "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\",\"params\":{}}\n" +
                (callToolFirst
                    ? "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"read_records\"}}\n"
                    : "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\",\"params\":{}}\n") +
                "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/list\",\"params\":{}}\n" +
                "{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"shutdown\"}\n";

            SignalingTextWriter stdoutCapture = new();
            BlockingMetadataProviderFactory metadataProviderFactory = new();
            CapturingExporter exporter = new();
            using EngineTelemetrySession telemetry = EngineTelemetrySession.Create(() => exporter,
                enableSyntheticCollection: telemetryEnabled, readEnvironmentVariable: _ => null,
                showNotice: () => { }, resolveIdentity: _ => new(Guid.NewGuid(), "ephemeral"), startTimer: false);
            RuntimeConfig runtimeConfig = new(
                Schema: RuntimeConfig.DEFAULT_CONFIG_SCHEMA_LINK,
                DataSource: new(DatabaseType.MSSQL, string.Empty),
                Entities: new RuntimeEntities(new Dictionary<string, Entity>()),
                Runtime: new RuntimeOptions(
                    Rest: null,
                    GraphQL: null,
                    Mcp: new McpRuntimeOptions(),
                    Host: null));
            using RuntimeConfigProvider runtimeConfigProvider = new StubRuntimeConfigProvider(runtimeConfig) { ProductTelemetry = telemetry };
            TaskCompletionSource registryEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource releaseRegistry = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Mock<IMcpToolRegistryRefreshService> refresh = new();
            refresh.Setup(value => value.EnsureInitialized(It.IsAny<CancellationToken>())).Callback(() =>
            {
                registryEntered.TrySetResult();
                releaseRegistry.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            });
            Mock<IMcpTool> tool = new();
            tool.SetupGet(value => value.ToolType).Returns(McpEnums.ToolType.BuiltIn);
            tool.Setup(value => value.GetToolMetadata()).Returns(new Tool { Name = "read_records" });
            tool.Setup(value => value.IsEnabled(It.IsAny<RuntimeConfig>())).Returns(true);
            tool.Setup(value => value.ExecuteAsync(It.IsAny<JsonDocument?>(), It.IsAny<IServiceProvider>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CallToolResult { Content = [] });
            McpToolRegistry registry = new();
            registry.ReplaceAll([tool.Object], runtimeConfig);

            using ServiceProvider serviceProvider = new ServiceCollection()
                .AddSingleton(new McpStdoutWriter(stdoutCapture))
                .AddSingleton(registry)
                .AddSingleton(telemetry)
                .AddSingleton(refresh.Object)
                .AddSingleton<IMetadataProviderFactory>(metadataProviderFactory)
                .AddSingleton(runtimeConfigProvider)
                .BuildServiceProvider();
            McpStdioServer server = new(
                serviceProvider.GetRequiredService<McpToolRegistry>(),
                serviceProvider,
                new StringReader(input));

            Task runTask = server.RunAsync(CancellationToken.None);
            try
            {
                string initializeResponse = await stdoutCapture.FirstLineWritten.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await metadataProviderFactory.InitializationStarted.WaitAsync(TimeSpan.FromSeconds(5));

                using (JsonDocument response = JsonDocument.Parse(initializeResponse))
                {
                    Assert.AreEqual(1, response.RootElement.GetProperty("id").GetInt32(),
                        "Initialize must respond before metadata inference completes.");
                }

                Assert.AreEqual(1, stdoutCapture.LineCount,
                    "Tool requests must wait until metadata inference and tool registration complete.");
                Assert.IsFalse(telemetry.IsReady, "Protocol initialization alone must not report data-serving readiness.");
                metadataProviderFactory.CompleteInitialization();
                await registryEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.IsFalse(telemetry.IsReady, "Metadata alone is insufficient while tool registration is still pending.");
                Assert.AreEqual(1, stdoutCapture.LineCount);
            }
            finally
            {
                metadataProviderFactory.CompleteInitialization();
                releaseRegistry.TrySetResult();
                await runTask.WaitAsync(TimeSpan.FromSeconds(10));
            }

            Assert.AreEqual(telemetryEnabled, telemetry.IsReady);
            Assert.AreEqual(4, stdoutCapture.LineCount,
                "Expected initialize, two tool requests, and shutdown responses.");
            Assert.AreEqual(1, metadataProviderFactory.InitializeAsyncCallCount,
                "Metadata inference must run exactly once.");
            refresh.Verify(value => value.EnsureInitialized(It.IsAny<CancellationToken>()), Times.Once);
            tool.Verify(value => value.ExecuteAsync(It.IsAny<JsonDocument?>(), It.IsAny<IServiceProvider>(), It.IsAny<CancellationToken>()),
                callToolFirst ? Times.Once() : Times.Never());
            await telemetry.StopAsync();
            Assert.AreEqual(telemetryEnabled ? 1 : 0, exporter.Records.Count(record => record.Name == "dab.engine.ready"));
            Assert.AreEqual(telemetryEnabled && callToolFirst ? 1 : 0,
                exporter.Records.Count(record => record.Name == "dab.engine.first_successful_request"));
        }

        [TestMethod]
        public async Task RunAsync_CancelingDeferredMetadataReportsFailureWithoutReadiness()
        {
            CapturingExporter exporter = new();
            using EngineTelemetrySession telemetry = EngineTelemetrySession.Create(() => exporter, enableSyntheticCollection: true,
                readEnvironmentVariable: _ => null, showNotice: () => { }, startTimer: false);
            RuntimeConfig config = new(null, new(DatabaseType.MSSQL, string.Empty), new(new Dictionary<string, Entity>()));
            using RuntimeConfigProvider provider = new StubRuntimeConfigProvider(config) { ProductTelemetry = telemetry };
            BlockingMetadataProviderFactory metadata = new();
            Mock<IMcpToolRegistryRefreshService> refresh = new();
            using StringWriter output = new();
            using ServiceProvider services = new ServiceCollection()
                .AddSingleton(new McpStdoutWriter(output))
                .AddSingleton(provider)
                .AddSingleton(telemetry)
                .AddSingleton<IMetadataProviderFactory>(metadata)
                .AddSingleton(refresh.Object)
                .BuildServiceProvider();
            using StringReader input = new("""
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}
                {"jsonrpc":"2.0","id":2,"method":"tools/list"}
                """);
            McpStdioServer server = new(new McpToolRegistry(), services, input);
            using CancellationTokenSource stopping = new();
            Task running = server.RunAsync(stopping.Token);
            try
            {
                await metadata.InitializationStarted.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.IsFalse(telemetry.IsReady);
                stopping.Cancel();
                await running.WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                stopping.Cancel();
                metadata.CompleteInitialization();
                await running.WaitAsync(TimeSpan.FromSeconds(5));
            }

            await telemetry.StopAsync();
            Assert.AreEqual(1, metadata.InitializeAsyncCallCount);
            refresh.Verify(value => value.EnsureInitialized(It.IsAny<CancellationToken>()), Times.Never);
            Assert.AreEqual("metadata", exporter.Records.Single(record => record.Name == "dab.engine.startup_failed").Properties["failure_stage"]);
            Assert.IsFalse(exporter.Records.Any(record => record.Name == "dab.engine.ready"));
            string[] responses = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            Assert.AreEqual(2, responses.Length);
            StringAssert.Contains(responses[0], "protocolVersion");
            StringAssert.Contains(responses[1], "Internal error");
        }

        private static (McpStdioServer server, StringWriter stdoutCapture) CreateServerWithCapturedOutput(
            TextReader inputReader,
            IMcpStdioToolListChangedNotifier? notifier = null)
        {
            StringWriter stdoutCapture = new();
            McpStdoutWriter stdoutWriter = new(stdoutCapture);

            ServiceCollection services = new();
            services.AddSingleton(stdoutWriter);
            services.AddSingleton<McpToolRegistry>();
            if (notifier is not null)
            {
                services.AddSingleton(notifier);
            }

            IServiceProvider serviceProvider = services.BuildServiceProvider();

            McpStdioServer server = new(
                serviceProvider.GetRequiredService<McpToolRegistry>(),
                serviceProvider,
                inputReader);

            return (server, stdoutCapture);
        }

        private sealed class SignalingTextWriter : StringWriter
        {
            public TaskCompletionSource<string> FirstLineWritten { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public int LineCount { get; private set; }

            public override void WriteLine(string? value)
            {
                base.WriteLine(value);
                LineCount++;
                FirstLineWritten.TrySetResult(value ?? string.Empty);
            }
        }

        private sealed class StubRuntimeConfigProvider : RuntimeConfigProvider
        {
            private readonly RuntimeConfig _runtimeConfig;

            public StubRuntimeConfigProvider(RuntimeConfig runtimeConfig) : base(new StubRuntimeConfigLoader { RuntimeConfig = runtimeConfig })
            {
                _runtimeConfig = runtimeConfig;
            }

            public override RuntimeConfig GetConfig()
            {
                return _runtimeConfig;
            }
        }

        private sealed class StubRuntimeConfigLoader : RuntimeConfigLoader
        {
            public override bool TryLoadKnownConfig([NotNullWhen(true)] out RuntimeConfig? config, bool replaceEnvVar = false)
            {
                config = null;
                return false;
            }

            public override string GetPublishedDraftSchemaLink()
            {
                return RuntimeConfig.DEFAULT_CONFIG_SCHEMA_LINK;
            }
        }

        private sealed class CapturingExporter : IEngineTelemetryExporter
        {
            internal ConcurrentQueue<EngineTelemetryEvent> Records { get; } = new();

            public ValueTask<bool> ExportAsync(EngineTelemetryEvent record, CancellationToken cancellationToken)
            {
                Records.Enqueue(record);
                return ValueTask.FromResult(true);
            }

            public void Dispose() { }
        }
    }
}
