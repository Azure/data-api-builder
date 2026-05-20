// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Mcp.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass]
    public class McpStdioServerContentBlockTests
    {
        // Mirror the options used by McpStdioServer.WriteResult so the assertion
        // reflects the actual wire format the server emits.
        private static readonly JsonSerializerOptions _writeOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        [TestMethod]
        public void CoerceToMcpContentBlocks_OmitsNullAnnotationsAndMeta()
        {
            object callResult = new
            {
                Content = new ContentBlock[]
                {
                    new TextContentBlock { Text = "hello" }
                }
            };

            object[] contentBlocks = InvokeCoerceToMcpContentBlocks(callResult);

            Assert.AreEqual(1, contentBlocks.Length);

            // Serialize with WhenWritingNull options to match how WriteResult emits the response.
            JsonElement contentBlock = JsonSerializer.SerializeToElement(contentBlocks[0], _writeOptions);
            Assert.AreEqual("text", contentBlock.GetProperty("type").GetString());
            Assert.AreEqual("hello", contentBlock.GetProperty("text").GetString());
            Assert.IsFalse(contentBlock.TryGetProperty("annotations", out _), "annotations should be omitted when null.");
            Assert.IsFalse(contentBlock.TryGetProperty("_meta", out _), "_meta should be omitted when null.");
        }

        private static object[] InvokeCoerceToMcpContentBlocks(object callResult)
        {
            MethodInfo coerceMethod = typeof(McpStdioServer).GetMethod(
                "CoerceToMcpContentBlocks",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNotNull(coerceMethod, "Failed to resolve CoerceToMcpContentBlocks via reflection.");

            return (object[])coerceMethod.Invoke(obj: null, parameters: new object[] { callResult });
        }
    }
}
