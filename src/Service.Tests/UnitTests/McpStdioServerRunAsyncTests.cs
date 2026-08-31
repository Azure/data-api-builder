// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
    }
}
