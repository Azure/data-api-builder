// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Mcp.Core;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass]
    public class McpStdioToolListChangedNotifierTests
    {
        [TestMethod]
        public void NotifyToolsListChanged_BeforeInitialized_DoesNotWrite()
        {
            StringWriter output = new();
            using McpStdoutWriter stdoutWriter = new(output);
            McpStdioToolListChangedNotifier notifier = new(stdoutWriter);

            notifier.NotifyToolsListChanged();

            Assert.AreEqual(string.Empty, output.ToString());
        }

        [TestMethod]
        public void NotifyToolsListChanged_AfterInitialized_WritesProtocolFrame()
        {
            SignalingStringWriter output = new();
            using McpStdoutWriter stdoutWriter = new(output);
            McpStdioToolListChangedNotifier notifier = new(stdoutWriter);
            notifier.MarkInitialized();

            notifier.NotifyToolsListChanged();
            Assert.IsTrue(
                output.LineWritten.Wait(TimeSpan.FromSeconds(5)),
                "The queued tool-list notification was not written.");

            string[] lines = output.ToString().Split(
                Environment.NewLine,
                StringSplitOptions.RemoveEmptyEntries);
            Assert.AreEqual(1, lines.Length);

            using JsonDocument document = JsonDocument.Parse(lines[0]);
            JsonElement root = document.RootElement;
            Assert.AreEqual("2.0", root.GetProperty("jsonrpc").GetString());
            Assert.AreEqual(
                "notifications/tools/list_changed",
                root.GetProperty("method").GetString());
            Assert.AreEqual(JsonValueKind.Object, root.GetProperty("params").ValueKind);
            Assert.AreEqual(0, root.GetProperty("params").EnumerateObject().Count());
            Assert.IsFalse(root.TryGetProperty("id", out _),
                "JSON-RPC notifications must not include a request id.");
        }

        [TestMethod]
        public void MarkInitialized_IsIdempotent()
        {
            SignalingStringWriter output = new();
            using McpStdoutWriter stdoutWriter = new(output);
            McpStdioToolListChangedNotifier notifier = new(stdoutWriter);

            notifier.MarkInitialized();
            notifier.MarkInitialized();
            notifier.NotifyToolsListChanged();
            Assert.IsTrue(
                output.LineWritten.Wait(TimeSpan.FromSeconds(5)),
                "The queued tool-list notification was not written.");

            string[] lines = output.ToString().Split(
                Environment.NewLine,
                StringSplitOptions.RemoveEmptyEntries);
            Assert.AreEqual(1, lines.Length);
        }

        [TestMethod]
        public async Task NotifyToolsListChanged_WhenStdoutBlocks_ReturnsWithoutWaitingForWrite()
        {
            BlockingStringWriter output = new();
            using McpStdoutWriter stdoutWriter = new(output);
            McpStdioToolListChangedNotifier notifier = new(stdoutWriter);
            notifier.MarkInitialized();

            Task notificationCall = Task.Run(notifier.NotifyToolsListChanged);
            try
            {
                Assert.IsTrue(
                    output.WriteEntered.Wait(TimeSpan.FromSeconds(5)),
                    "The notification worker did not begin the stdout write.");
                Assert.IsTrue(
                    await Task.WhenAny(notificationCall, Task.Delay(TimeSpan.FromSeconds(1))) == notificationCall,
                    "NotifyToolsListChanged must enqueue transport I/O instead of blocking the reload pipeline.");
            }
            finally
            {
                output.ReleaseWrite.Set();
                await notificationCall.WaitAsync(TimeSpan.FromSeconds(5));
            }

            Assert.IsTrue(
                output.LineWritten.Wait(TimeSpan.FromSeconds(5)),
                "The notification was not written after stdout resumed.");
        }

        [TestMethod]
        public void NotifyToolsListChanged_WhenQueuedWriteFails_LogsError()
        {
            ThrowingStringWriter output = new();
            using McpStdoutWriter stdoutWriter = new(output);
            Mock<ILogger<McpStdioToolListChangedNotifier>> logger = new();
            McpStdioToolListChangedNotifier notifier = new(stdoutWriter, logger.Object);
            notifier.MarkInitialized();

            notifier.NotifyToolsListChanged();

            Assert.IsTrue(
                output.WriteAttempted.Wait(TimeSpan.FromSeconds(5)),
                "The notification worker did not attempt the stdout write.");
            Assert.IsTrue(
                SpinWait.SpinUntil(() => logger.Invocations.Count > 0, TimeSpan.FromSeconds(5)),
                "The asynchronous notification failure was not logged.");
            logger.Verify(
                value => value.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((state, _) =>
                        state.ToString()!.Contains(
                            "Failed to write an MCP tool-list change notification.",
                            StringComparison.Ordinal)),
                    It.IsAny<Exception>(),
                    (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()),
                Times.Once);
        }

        private class SignalingStringWriter : StringWriter
        {
            public ManualResetEventSlim LineWritten { get; } = new();

            public override void WriteLine(string? value)
            {
                base.WriteLine(value);
                LineWritten.Set();
            }

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                if (disposing)
                {
                    LineWritten.Dispose();
                }
            }
        }

        private sealed class BlockingStringWriter : SignalingStringWriter
        {
            public ManualResetEventSlim WriteEntered { get; } = new();

            public ManualResetEventSlim ReleaseWrite { get; } = new();

            public override void WriteLine(string? value)
            {
                WriteEntered.Set();
                if (!ReleaseWrite.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException("Timed out waiting to release the test stdout writer.");
                }

                base.WriteLine(value);
            }

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                if (disposing)
                {
                    WriteEntered.Dispose();
                    ReleaseWrite.Dispose();
                }
            }
        }

        private sealed class ThrowingStringWriter : StringWriter
        {
            public ManualResetEventSlim WriteAttempted { get; } = new();

            public override void WriteLine(string? value)
            {
                WriteAttempted.Set();
                throw new IOException("Expected stdout failure.");
            }

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                if (disposing)
                {
                    WriteAttempted.Dispose();
                }
            }
        }
    }
}
