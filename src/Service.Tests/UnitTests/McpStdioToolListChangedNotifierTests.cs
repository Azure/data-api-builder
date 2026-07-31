// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Azure.DataApiBuilder.Mcp.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

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
            StringWriter output = new();
            using McpStdoutWriter stdoutWriter = new(output);
            McpStdioToolListChangedNotifier notifier = new(stdoutWriter);
            notifier.MarkInitialized();

            notifier.NotifyToolsListChanged();

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
            StringWriter output = new();
            using McpStdoutWriter stdoutWriter = new(output);
            McpStdioToolListChangedNotifier notifier = new(stdoutWriter);

            notifier.MarkInitialized();
            notifier.MarkInitialized();
            notifier.NotifyToolsListChanged();

            string[] lines = output.ToString().Split(
                Environment.NewLine,
                StringSplitOptions.RemoveEmptyEntries);
            Assert.AreEqual(1, lines.Length);
        }
    }
}
