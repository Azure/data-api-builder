// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Azure.DataApiBuilder.Mcp.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass]
    public class McpStdioServerContentBlockTests
    {
        /// <summary>
        /// Integration test that validates the actual stdio wire output from WriteResult.
        /// This test captures the JSON-RPC response emitted by McpStdioServer.WriteResult
        /// via an injected McpStdoutWriter backed by a StringWriter, then parses the JSON
        /// and verifies that TextContentBlock's optional metadata fields (annotations, _meta)
        /// are omitted when unset, not serialized as explicit JSON nulls.
        /// 
        /// This is a true regression test: if the WhenWritingNull serialization policy
        /// is removed from WriteResult, this test will fail.
        /// </summary>
        [TestMethod]
        public void WriteResult_WithTextContentBlock_OmitsNullAnnotationsAndMetaFromWire()
        {
            // Arrange — capture stdio output via a StringWriter-backed McpStdoutWriter
            MemoryStream memoryStream = new();
            StreamWriter streamWriter = new(
                memoryStream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: -1,
                leaveOpen: true)
            {
                AutoFlush = true
            };
            McpStdoutWriter stdoutWriter = new(streamWriter);

            // Build a minimal service provider with the injected writer
            ServiceCollection services = new();
            services.AddSingleton(stdoutWriter);
            services.AddSingleton<McpToolRegistry>();
            IServiceProvider serviceProvider = services.BuildServiceProvider();

            McpStdioServer server = new(
                serviceProvider.GetRequiredService<McpToolRegistry>(),
                serviceProvider);

            // Simulate a CallToolResult with a TextContentBlock (annotations=null, _meta=null)
            object callResult = new
            {
                Content = new ContentBlock[]
                {
                    new TextContentBlock { Text = "hello from test" }
                }
            };

            // Coerce the result to content blocks (this is what HandleCallToolAsync does)
            object[] contentBlocks = InvokeCoerceToMcpContentBlocks(callResult);

            // Create a mock id and invoke WriteResult via reflection
            JsonElement id = JsonDocument.Parse("42").RootElement;
            InvokeWriteResult(server, id, new { content = contentBlocks });

            // Act — read the captured JSON-RPC response
            stdoutWriter.Dispose();
            memoryStream.Position = 0;
            using StreamReader reader = new(memoryStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            string wireOutput = reader.ReadToEnd().TrimEnd();

            // Assert — parse and verify the wire format
            using JsonDocument doc = JsonDocument.Parse(wireOutput);
            JsonElement root = doc.RootElement;

            Assert.AreEqual("2.0", root.GetProperty("jsonrpc").GetString());
            Assert.AreEqual(42, root.GetProperty("id").GetInt32());

            JsonElement result = root.GetProperty("result");
            JsonElement content = result.GetProperty("content");
            Assert.AreEqual(JsonValueKind.Array, content.ValueKind);
            Assert.AreEqual(1, content.GetArrayLength());

            JsonElement contentBlock = content[0];
            Assert.AreEqual("text", contentBlock.GetProperty("type").GetString());
            Assert.AreEqual("hello from test", contentBlock.GetProperty("text").GetString());

            // The regression assertion: annotations and _meta must be omitted, not present as null
            Assert.IsFalse(contentBlock.TryGetProperty("annotations", out _),
                "annotations should be omitted from wire output when null.");
            Assert.IsFalse(contentBlock.TryGetProperty("_meta", out _),
                "_meta should be omitted from wire output when null.");
        }

        private static object[] InvokeCoerceToMcpContentBlocks(object callResult)
        {
            MethodInfo? coerceMethod = typeof(McpStdioServer).GetMethod(
                "CoerceToMcpContentBlocks",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNotNull(coerceMethod, "Failed to resolve CoerceToMcpContentBlocks via reflection.");

            object? result = coerceMethod!.Invoke(obj: null, parameters: new object[] { callResult });
            return (object[])result!;
        }

        private static void InvokeWriteResult(McpStdioServer server, JsonElement id, object resultObject)
        {
            MethodInfo? writeResultMethod = typeof(McpStdioServer).GetMethod(
                "WriteResult",
                BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.IsNotNull(writeResultMethod, "Failed to resolve WriteResult via reflection.");

            // WriteResult signature: void WriteResult(JsonElement? id, object resultObject)
            // We pass a non-nullable JsonElement, so wrap it as JsonElement?
            writeResultMethod!.Invoke(server, new object?[] { (JsonElement?)id, resultObject });
        }
    }
}
