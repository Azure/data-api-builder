// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        /// <summary>
        /// Verifies that when a tool returns a real <see cref="CallToolResult"/> with IsError=true,
        /// the stdio wire output contains "isError": true in the JSON-RPC result object.
        /// Regression test for the bug where CoerceToMcpContentBlocks discarded IsError.
        /// </summary>
        [TestMethod]
        public void HandleCallTool_ErrorResult_EmitsIsErrorTrueOnWire()
        {
            (McpStdioServer server, MemoryStream memoryStream, McpStdoutWriter stdoutWriter) = CreateServerWithCapturedOutput();

            // Use a real CallToolResult (the actual type returned by every tool's error path)
            // to match exactly what HandleCallToolAsync receives from McpTelemetryHelper.
            CallToolResult callToolResult = new()
            {
                IsError = true,
                Content = new List<ContentBlock> { new TextContentBlock { Text = "{\"status\":\"error\"}" } }
            };

            JsonElement id = JsonDocument.Parse("1").RootElement;
            MethodInfo? handleCallToolAsync = typeof(McpStdioServer).GetMethod(
                "HandleCallToolAsync",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(JsonElement), typeof(CallToolResult) },
                modifiers: null);

            Assert.IsNotNull(handleCallToolAsync, "Expected to find McpStdioServer.HandleCallToolAsync(JsonElement, CallToolResult).");

            object? handleCallTask = handleCallToolAsync.Invoke(server, new object[] { id, callToolResult });
            Assert.IsNotNull(handleCallTask, "HandleCallToolAsync should return a Task.");
            ((System.Threading.Tasks.Task)handleCallTask).GetAwaiter().GetResult();

            string wireOutput = ReadCapturedOutput(stdoutWriter, memoryStream);

            using JsonDocument doc = JsonDocument.Parse(wireOutput);
            JsonElement result = doc.RootElement.GetProperty("result");

            Assert.IsTrue(result.TryGetProperty("isError", out JsonElement isErrorEl),
                "isError must be present on the wire for error tool results.");
            Assert.AreEqual(JsonValueKind.True, isErrorEl.ValueKind,
                "isError must be true for error tool results.");
        }

        /// <summary>
        /// Verifies that when a tool returns a success result (IsError=null), the stdio wire
        /// output does NOT contain an "isError" field (omitted, not present as null or false).
        /// </summary>
        [TestMethod]
        public void HandleCallTool_SuccessResult_OmitsIsErrorFromWire()
        {
            (McpStdioServer server, MemoryStream memoryStream, McpStdoutWriter stdoutWriter) = CreateServerWithCapturedOutput();

            object[] contentBlocks = InvokeCoerceToMcpContentBlocks(new
            {
                Content = new ContentBlock[] { new TextContentBlock { Text = "{\"status\":\"success\"}" } }
            });

            JsonElement id = JsonDocument.Parse("2").RootElement;

            // Simulate what HandleCallTool does when IsError is null (success)
            InvokeWriteResult(server, id, new { content = contentBlocks });

            string wireOutput = ReadCapturedOutput(stdoutWriter, memoryStream);

            using JsonDocument doc = JsonDocument.Parse(wireOutput);
            JsonElement result = doc.RootElement.GetProperty("result");

            Assert.IsFalse(result.TryGetProperty("isError", out _),
                "isError must be absent from the wire for successful tool results.");
        }

        [TestMethod]
        public void CoerceToMcpContentBlocks_Null_ReturnsEmptyArray()
        {
            Assert.AreEqual(0, InvokeCoerceToMcpContentBlocks(null).Length);
        }

        [TestMethod]
        public void CoerceToMcpContentBlocks_MixedEnumerable_NormalizesStringsAndJson()
        {
            using JsonDocument json = JsonDocument.Parse("{\"answer\":42}");
            TextContentBlock existingBlock = new() { Text = "already normalized" };
            object input = new
            {
                Content = new object[] { "plain text", json.RootElement.Clone(), existingBlock }
            };

            object[] result = InvokeCoerceToMcpContentBlocks(input);

            Assert.AreEqual(3, result.Length);
            JsonElement textBlock = SerializeToElement(result[0]);
            Assert.AreEqual("text", textBlock.GetProperty("type").GetString());
            Assert.AreEqual("plain text", textBlock.GetProperty("text").GetString());

            JsonElement jsonBlock = SerializeToElement(result[1]);
            Assert.AreEqual("application/json", jsonBlock.GetProperty("type").GetString());
            Assert.AreEqual(42, jsonBlock.GetProperty("data").GetProperty("answer").GetInt32());
            Assert.AreSame(existingBlock, result[2]);
        }

        [TestMethod]
        public void CoerceToMcpContentBlocks_StringContent_ReturnsTextBlock()
        {
            object[] result = InvokeCoerceToMcpContentBlocks(new { Content = "hello" });

            JsonElement block = SerializeToElement(result.Single());
            Assert.AreEqual("text", block.GetProperty("type").GetString());
            Assert.AreEqual("hello", block.GetProperty("text").GetString());
        }

        [TestMethod]
        public void CoerceToMcpContentBlocks_JsonContent_ReturnsApplicationJsonBlock()
        {
            using JsonDocument json = JsonDocument.Parse("[1,2,3]");

            object[] result = InvokeCoerceToMcpContentBlocks(new { Content = json.RootElement.Clone() });

            JsonElement block = SerializeToElement(result.Single());
            Assert.AreEqual("application/json", block.GetProperty("type").GetString());
            Assert.AreEqual(3, block.GetProperty("data").GetArrayLength());
        }

        [TestMethod]
        public void CoerceToMcpContentBlocks_RawJsonElement_ReturnsApplicationJsonBlock()
        {
            using JsonDocument json = JsonDocument.Parse("true");

            object[] result = InvokeCoerceToMcpContentBlocks(json.RootElement.Clone());

            JsonElement block = SerializeToElement(result.Single());
            Assert.AreEqual("application/json", block.GetProperty("type").GetString());
            Assert.IsTrue(block.GetProperty("data").GetBoolean());
        }

        [TestMethod]
        public void CoerceToMcpContentBlocks_ObjectWithoutContent_SerializesAsText()
        {
            object[] result = InvokeCoerceToMcpContentBlocks(new { Status = "ok", Count = 2 });

            JsonElement block = SerializeToElement(result.Single());
            Assert.AreEqual("text", block.GetProperty("type").GetString());
            StringAssert.Contains(block.GetProperty("text").GetString(), "\"Status\":\"ok\"");
        }

        [TestMethod]
        public void SafeToString_LargeJson_TruncatesPreview()
        {
            string result = InvokeSafeToString(new { Value = new string('x', (32 * 1024) + 500) });

            Assert.IsTrue(result.StartsWith("{\"Value\":\"", StringComparison.Ordinal));
            StringAssert.Contains(result, "... [truncated, total length=");
            Assert.IsTrue(result.Length < (33 * 1024));
        }

        [TestMethod]
        public void SafeToString_SerializationFailure_UsesToStringFallback()
        {
            Assert.AreEqual("fallback", InvokeSafeToString(new SelfReferencingObject("fallback")));
        }

        [TestMethod]
        public void SafeToString_NullToStringFallback_ReturnsEmptyString()
        {
            Assert.AreEqual(string.Empty, InvokeSafeToString(new SelfReferencingObject(null)));
        }

        [DataTestMethod]
        [DataRow("\"abc\"", "abc", DisplayName = "String id")]
        [DataRow("true", null, DisplayName = "Boolean id")]
        [DataRow("[]", null, DisplayName = "Array id")]
        [DataRow("{}", null, DisplayName = "Object id")]
        public void GetIdValue_ConvertsSupportedPrimitiveTypes(string json, object? expected)
        {
            using JsonDocument document = JsonDocument.Parse(json);

            object? actual = InvokeGetIdValue(document.RootElement);

            Assert.AreEqual(expected, actual);
        }

        [DataTestMethod]
        [DataRow("9223372036854775807")]
        [DataRow("3.25")]
        [DataRow("1e400")]
        public void GetIdValue_NumericIdPreservesRawJson(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);

            object? actual = InvokeGetIdValue(document.RootElement);

            Assert.IsInstanceOfType<JsonElement>(actual);
            Assert.AreEqual(JsonValueKind.Number, ((JsonElement)actual).ValueKind);
            Assert.AreEqual(json, ((JsonElement)actual).GetRawText());
        }

        private static (McpStdioServer server, MemoryStream memoryStream, McpStdoutWriter stdoutWriter) CreateServerWithCapturedOutput()
        {
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

            ServiceCollection services = new();
            services.AddSingleton(stdoutWriter);
            services.AddSingleton<McpToolRegistry>();
            IServiceProvider serviceProvider = services.BuildServiceProvider();

            McpStdioServer server = new(
                serviceProvider.GetRequiredService<McpToolRegistry>(),
                serviceProvider);

            return (server, memoryStream, stdoutWriter);
        }

        private static string ReadCapturedOutput(McpStdoutWriter stdoutWriter, MemoryStream memoryStream)
        {
            stdoutWriter.Dispose();
            memoryStream.Position = 0;
            using StreamReader reader = new(memoryStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return reader.ReadToEnd().TrimEnd();
        }

        private static object[] InvokeCoerceToMcpContentBlocks(object? callResult)
        {
            MethodInfo? coerceMethod = typeof(McpStdioServer).GetMethod(
                "CoerceToMcpContentBlocks",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNotNull(coerceMethod, "Failed to resolve CoerceToMcpContentBlocks via reflection.");

            object? result = coerceMethod!.Invoke(obj: null, parameters: new object?[] { callResult });
            return (object[])result!;
        }

        private static string InvokeSafeToString(object value)
        {
            MethodInfo? safeToStringMethod = typeof(McpStdioServer).GetMethod(
                "SafeToString",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNotNull(safeToStringMethod, "Failed to resolve SafeToString via reflection.");
            return (string)safeToStringMethod.Invoke(obj: null, parameters: new[] { value })!;
        }

        private static object? InvokeGetIdValue(JsonElement id)
        {
            MethodInfo? getIdValueMethod = typeof(McpStdioServer).GetMethod(
                "GetIdValue",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNotNull(getIdValueMethod, "Failed to resolve GetIdValue via reflection.");
            return getIdValueMethod.Invoke(obj: null, parameters: new object[] { id });
        }

        private static JsonElement SerializeToElement(object value)
        {
            using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(value));
            return document.RootElement.Clone();
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

        private sealed class SelfReferencingObject
        {
            private readonly string? _text;

            public SelfReferencingObject(string? text)
            {
                _text = text;
            }

            public SelfReferencingObject Self => this;

            public override string? ToString() => _text;
        }
    }
}
