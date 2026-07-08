// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text.Json;
using Azure.DataApiBuilder.Mcp.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;

namespace Azure.DataApiBuilder.Service.Tests.Mcp
{
    /// <summary>
    /// Unit tests for <see cref="McpResponseBuilder"/> covering success/error result
    /// construction and IActionResult JSON extraction. Pure logic; no database required.
    /// </summary>
    [TestClass]
    public class McpResponseBuilderTests
    {
        private static JsonElement ParseContent(CallToolResult result)
        {
            TextContentBlock block = (TextContentBlock)result.Content[0];
            return JsonDocument.Parse(block.Text).RootElement;
        }

        [TestMethod]
        public void BuildSuccessResult_AddsStatusAndData()
        {
            Dictionary<string, object?> data = new() { ["id"] = 1, ["name"] = "DAB" };

            CallToolResult result = McpResponseBuilder.BuildSuccessResult(data);

            Assert.IsTrue(result.IsError != true);
            JsonElement content = ParseContent(result);
            Assert.AreEqual("success", content.GetProperty("status").GetString());
            Assert.AreEqual(1, content.GetProperty("id").GetInt32());
            Assert.AreEqual("DAB", content.GetProperty("name").GetString());
        }

        [TestMethod]
        public void BuildErrorResult_SetsIsErrorAndStructure()
        {
            CallToolResult result = McpResponseBuilder.BuildErrorResult(
                "read_records", "InvalidArguments", "Something went wrong");

            Assert.IsTrue(result.IsError == true);
            JsonElement content = ParseContent(result);
            Assert.AreEqual("read_records", content.GetProperty("toolName").GetString());
            Assert.AreEqual("error", content.GetProperty("status").GetString());
            JsonElement error = content.GetProperty("error");
            Assert.AreEqual("InvalidArguments", error.GetProperty("type").GetString());
            Assert.AreEqual("Something went wrong", error.GetProperty("message").GetString());
        }

        [TestMethod]
        public void ExtractResultJson_ObjectResultWithJsonElement_ReturnsRawText()
        {
            JsonElement je = JsonDocument.Parse(@"{ ""a"": 1 }").RootElement;
            ObjectResult objResult = new(je);

            string json = McpResponseBuilder.ExtractResultJson(objResult);

            Assert.AreEqual(@"{ ""a"": 1 }".Replace(" ", ""), json.Replace(" ", ""));
        }

        [TestMethod]
        public void ExtractResultJson_ObjectResultWithJsonDocument_ReturnsRawText()
        {
            JsonDocument jd = JsonDocument.Parse(@"{ ""b"": 2 }");
            ObjectResult objResult = new(jd);

            string json = McpResponseBuilder.ExtractResultJson(objResult);

            Assert.AreEqual(@"{ ""b"": 2 }".Replace(" ", ""), json.Replace(" ", ""));
        }

        [TestMethod]
        public void ExtractResultJson_ObjectResultWithPlainObject_SerializesValue()
        {
            ObjectResult objResult = new(new Dictionary<string, object?> { ["c"] = 3 });

            string json = McpResponseBuilder.ExtractResultJson(objResult);

            StringAssert.Contains(json, "\"c\"");
            StringAssert.Contains(json, "3");
        }

        [TestMethod]
        public void ExtractResultJson_ContentResultWithContent_ReturnsContent()
        {
            ContentResult content = new() { Content = "{\"x\":1}" };

            Assert.AreEqual("{\"x\":1}", McpResponseBuilder.ExtractResultJson(content));
        }

        [TestMethod]
        public void ExtractResultJson_ContentResultEmpty_ReturnsEmptyObject()
        {
            ContentResult content = new() { Content = "   " };

            Assert.AreEqual("{}", McpResponseBuilder.ExtractResultJson(content));
        }

        [TestMethod]
        public void ExtractResultJson_Null_ReturnsEmptyObject()
        {
            Assert.AreEqual("{}", McpResponseBuilder.ExtractResultJson(null));
        }

        [TestMethod]
        public void ExtractResultJson_UnsupportedResult_ReturnsEmptyObject()
        {
            Assert.AreEqual("{}", McpResponseBuilder.ExtractResultJson(new NoContentResult()));
        }

        [TestMethod]
        public void GetJsonValue_Number_PreservesNumericValue()
        {
            JsonElement number = JsonDocument.Parse("42").RootElement;

            Assert.AreEqual(42m, Convert.ToDecimal(McpResponseBuilder.GetJsonValue(number)));
        }
    }
}
