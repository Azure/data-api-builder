// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Mcp.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
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
    }
}
