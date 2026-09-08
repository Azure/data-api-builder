// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

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

        [TestMethod]
        public async Task RunAsync_InitializeRespondsBeforeMetadataInferenceCompletes()
        {
            const string INPUT =
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{}}\n" +
                "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\",\"params\":{}}\n" +
                "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\",\"params\":{}}\n" +
                "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/list\",\"params\":{}}\n" +
                "{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"shutdown\"}\n";

            SignalingTextWriter stdoutCapture = new();
            BlockingMetadataProviderFactory metadataProviderFactory = new();
            RuntimeConfig runtimeConfig = new(
                Schema: RuntimeConfig.DEFAULT_CONFIG_SCHEMA_LINK,
                DataSource: null,
                Entities: new RuntimeEntities(new Dictionary<string, Entity>()),
                Runtime: new RuntimeOptions(
                    Rest: null,
                    GraphQL: null,
                    Mcp: new McpRuntimeOptions(),
                    Host: null));
            RuntimeConfigProvider runtimeConfigProvider = new StubRuntimeConfigProvider(runtimeConfig);

            ServiceProvider serviceProvider = new ServiceCollection()
                .AddSingleton(new McpStdoutWriter(stdoutCapture))
                .AddSingleton<McpToolRegistry>()
                .AddSingleton<IMetadataProviderFactory>(metadataProviderFactory)
                .AddSingleton(runtimeConfigProvider)
                .BuildServiceProvider();
            McpStdioServer server = new(
                serviceProvider.GetRequiredService<McpToolRegistry>(),
                serviceProvider,
                new StringReader(INPUT));

            Task runTask = server.RunAsync(CancellationToken.None);

            string initializeResponse = await stdoutCapture.FirstLineWritten.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await metadataProviderFactory.InitializeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            using (JsonDocument response = JsonDocument.Parse(initializeResponse))
            {
                Assert.AreEqual(1, response.RootElement.GetProperty("id").GetInt32(),
                    "Initialize must respond before metadata inference completes.");
            }

            Assert.AreEqual(1, stdoutCapture.LineCount,
                "tools/list must wait until metadata inference and tool registration complete.");

            metadataProviderFactory.CompleteInitialization();
            await runTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.AreEqual(4, stdoutCapture.LineCount,
                "Expected initialize, two tools/list, and shutdown responses.");
            Assert.AreEqual(1, metadataProviderFactory.InitializeAsyncCallCount,
                "Metadata inference must run exactly once.");
        }

        private static (McpStdioServer server, StringWriter stdoutCapture) CreateServerWithCapturedOutput(TextReader inputReader)
        {
            StringWriter stdoutCapture = new();
            McpStdoutWriter stdoutWriter = new(stdoutCapture);

            ServiceCollection services = new();
            services.AddSingleton(stdoutWriter);
            services.AddSingleton<McpToolRegistry>();
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

        private sealed class BlockingMetadataProviderFactory : IMetadataProviderFactory
        {
            private readonly TaskCompletionSource _completeInitialization =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource InitializeStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public int InitializeAsyncCallCount { get; private set; }

            public async Task InitializeAsync()
            {
                InitializeAsyncCallCount++;
                InitializeStarted.TrySetResult();
                await _completeInitialization.Task;
            }

            public void CompleteInitialization()
            {
                _completeInitialization.TrySetResult();
            }

            public ISqlMetadataProvider GetMetadataProvider(string dataSourceName)
                => throw new NotImplementedException();

            public IEnumerable<ISqlMetadataProvider> ListMetadataProviders()
                => Array.Empty<ISqlMetadataProvider>();

            public List<Exception> GetAllMetadataExceptions()
                => new();

            public void InitializeAsync(
                Dictionary<string, Dictionary<string, DatabaseObject>> entityToDatabaseObjectMap,
                Dictionary<string, Dictionary<string, string>> graphQLStoredProcedureExposedNameToEntityNameMap)
            {
                InitializeAsyncCallCount++;
            }
        }

        private sealed class StubRuntimeConfigProvider : RuntimeConfigProvider
        {
            private readonly RuntimeConfig _runtimeConfig;

            public StubRuntimeConfigProvider(RuntimeConfig runtimeConfig) : base(new StubRuntimeConfigLoader())
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
    }
}
