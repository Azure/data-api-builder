// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Mcp.Core;
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
            (McpStdioServer server, StringWriter stdoutCapture) = CreateServerWithCapturedOutput();
            TextReader originalIn = Console.In;

            try
            {
                // Empty input immediately yields EOF (ReadLineAsync returns null).
                Console.SetIn(new StringReader(string.Empty));

                await server.RunAsync(CancellationToken.None);

                Assert.AreEqual(string.Empty, stdoutCapture.ToString(),
                    "Server should exit cleanly on EOF without emitting protocol output.");
            }
            finally
            {
                Console.SetIn(originalIn);
            }
        }

        [TestMethod]
        public async Task RunAsync_BlankLineThenShutdown_IgnoresBlankLineAndHandlesShutdown()
        {
            (McpStdioServer server, StringWriter stdoutCapture) = CreateServerWithCapturedOutput();
            TextReader originalIn = Console.In;

            try
            {
                Console.SetIn(new StringReader(Environment.NewLine +
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"shutdown\"}" +
                    Environment.NewLine));

                await server.RunAsync(CancellationToken.None);

                string[] lines = stdoutCapture
                    .ToString()
                    .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

                Assert.AreEqual(1, lines.Length,
                    "Expected a single response line for shutdown request.");
                StringAssert.Contains(lines[0], "\"id\":1");
                StringAssert.Contains(lines[0], "\"ok\":true");
            }
            finally
            {
                Console.SetIn(originalIn);
            }
        }

        private static (McpStdioServer server, StringWriter stdoutCapture) CreateServerWithCapturedOutput()
        {
            StringWriter stdoutCapture = new();
            McpStdoutWriter stdoutWriter = new(stdoutCapture);

            ServiceCollection services = new();
            services.AddSingleton(stdoutWriter);
            services.AddSingleton<McpToolRegistry>();
            IServiceProvider serviceProvider = services.BuildServiceProvider();

            McpStdioServer server = new(
                serviceProvider.GetRequiredService<McpToolRegistry>(),
                serviceProvider);

            return (server, stdoutCapture);
        }
    }
}
