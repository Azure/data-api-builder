// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Text.Json;
using Azure.DataApiBuilder.Mcp.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Mcp
{
    /// <summary>
    /// Unit tests for <see cref="McpJsonHelper"/> covering JSON value conversion and
    /// engine-result extraction helpers. Pure logic; no database required.
    /// </summary>
    [TestClass]
    public class McpJsonHelperTests
    {
        private static JsonElement Value(string json) => JsonDocument.Parse(json).RootElement;

        [TestMethod]
        public void GetJsonValue_String_ReturnsString()
        {
            Assert.AreEqual("hello", McpJsonHelper.GetJsonValue(Value("\"hello\"")));
        }

        [TestMethod]
        public void GetJsonValue_Number_ReturnsDecimal()
        {
            // McpJsonHelper prefers decimal for maximum precision.
            Assert.AreEqual(42m, McpJsonHelper.GetJsonValue(Value("42")));
            Assert.AreEqual(3.14m, McpJsonHelper.GetJsonValue(Value("3.14")));
        }

        [TestMethod]
        public void GetJsonValue_Booleans_ReturnBool()
        {
            Assert.AreEqual(true, McpJsonHelper.GetJsonValue(Value("true")));
            Assert.AreEqual(false, McpJsonHelper.GetJsonValue(Value("false")));
        }

        [TestMethod]
        public void GetJsonValue_Null_ReturnsNull()
        {
            Assert.IsNull(McpJsonHelper.GetJsonValue(Value("null")));
        }

        [DataTestMethod]
        [DataRow("[1, 2, 3]", DisplayName = "Array falls back to raw text")]
        [DataRow(@"{ ""a"": 1 }", DisplayName = "Object falls back to raw text")]
        public void GetJsonValue_ComplexTypes_ReturnRawText(string json)
        {
            object? result = McpJsonHelper.GetJsonValue(Value(json));

            Assert.IsInstanceOfType(result, typeof(string));
        }

        [TestMethod]
        public void ExtractValuesFromEngineResult_WithValueArray_ReturnsFirstItemProperties()
        {
            JsonElement engine = Value(@"{ ""value"": [ { ""id"": 1, ""title"": ""DAB"" }, { ""id"": 2 } ] }");

            Dictionary<string, object?> result = McpJsonHelper.ExtractValuesFromEngineResult(engine);

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(1m, result["id"]);
            Assert.AreEqual("DAB", result["title"]);
        }

        [TestMethod]
        public void ExtractValuesFromEngineResult_EmptyValueArray_ReturnsEmpty()
        {
            JsonElement engine = Value(@"{ ""value"": [] }");

            Dictionary<string, object?> result = McpJsonHelper.ExtractValuesFromEngineResult(engine);

            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void ExtractValuesFromEngineResult_NoValueProperty_ReturnsEmpty()
        {
            JsonElement engine = Value(@"{ ""other"": 1 }");

            Dictionary<string, object?> result = McpJsonHelper.ExtractValuesFromEngineResult(engine);

            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void FormatKeyDetails_JoinsKeyValuePairs()
        {
            Dictionary<string, object?> keys = new() { ["id"] = 1, ["name"] = "abc" };

            string formatted = McpJsonHelper.FormatKeyDetails(keys);

            StringAssert.Contains(formatted, "id=1");
            StringAssert.Contains(formatted, "name=abc");
            StringAssert.Contains(formatted, ", ");
        }

        [TestMethod]
        public void FormatKeyDetails_EmptyDictionary_ReturnsEmptyString()
        {
            Assert.AreEqual(string.Empty, McpJsonHelper.FormatKeyDetails(new Dictionary<string, object?>()));
        }
    }
}
