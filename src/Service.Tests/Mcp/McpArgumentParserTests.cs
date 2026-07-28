// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Text.Json;
using Azure.DataApiBuilder.Mcp.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Mcp
{
    /// <summary>
    /// Unit tests for <see cref="McpArgumentParser"/> covering argument parsing and
    /// validation for the built-in MCP DML tools. Pure logic; no database required.
    /// </summary>
    [TestClass]
    public class McpArgumentParserTests
    {
        private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

        [TestMethod]
        public void TryParseEntity_ValidEntity_ReturnsTrue()
        {
            bool ok = McpArgumentParser.TryParseEntity(Parse(@"{ ""entity"": ""Book"" }"), out string entity, out string error);

            Assert.IsTrue(ok);
            Assert.AreEqual("Book", entity);
            Assert.AreEqual(string.Empty, error);
        }

        [TestMethod]
        public void TryParseEntity_MissingEntity_ReturnsFalse()
        {
            bool ok = McpArgumentParser.TryParseEntity(Parse(@"{ ""other"": ""x"" }"), out _, out string error);

            Assert.IsFalse(ok);
            StringAssert.Contains(error, "entity");
        }

        [DataTestMethod]
        [DataRow(@"{ ""entity"": """" }", DisplayName = "Empty entity")]
        [DataRow(@"{ ""entity"": ""   "" }", DisplayName = "Whitespace entity")]
        public void TryParseEntity_EmptyEntity_ReturnsFalse(string json)
        {
            bool ok = McpArgumentParser.TryParseEntity(Parse(json), out _, out string error);

            Assert.IsFalse(ok);
            StringAssert.Contains(error, "Entity is required");
        }

        [TestMethod]
        public void TryParseEntityAndData_Valid_ReturnsTrue()
        {
            bool ok = McpArgumentParser.TryParseEntityAndData(
                Parse(@"{ ""entity"": ""Book"", ""data"": { ""title"": ""t"" } }"),
                out string entity, out JsonElement data, out string error);

            Assert.IsTrue(ok);
            Assert.AreEqual("Book", entity);
            Assert.AreEqual(JsonValueKind.Object, data.ValueKind);
            Assert.AreEqual(string.Empty, error);
        }

        [TestMethod]
        public void TryParseEntityAndData_MissingData_ReturnsFalse()
        {
            bool ok = McpArgumentParser.TryParseEntityAndData(
                Parse(@"{ ""entity"": ""Book"" }"), out _, out _, out string error);

            Assert.IsFalse(ok);
            StringAssert.Contains(error, "data");
        }

        [TestMethod]
        public void TryParseEntityAndData_DataNotObject_ReturnsFalse()
        {
            bool ok = McpArgumentParser.TryParseEntityAndData(
                Parse(@"{ ""entity"": ""Book"", ""data"": 5 }"), out _, out _, out string error);

            Assert.IsFalse(ok);
            StringAssert.Contains(error, "JSON object");
        }

        [TestMethod]
        public void TryParseEntityAndKeys_Valid_ReturnsTrue()
        {
            bool ok = McpArgumentParser.TryParseEntityAndKeys(
                Parse(@"{ ""entity"": ""Book"", ""keys"": { ""id"": 1 } }"),
                out string entity, out Dictionary<string, object?> keys, out string error);

            Assert.IsTrue(ok);
            Assert.AreEqual("Book", entity);
            Assert.AreEqual(1, keys.Count);
            Assert.AreEqual(string.Empty, error);
        }

        [TestMethod]
        public void TryParseEntityAndKeys_MissingKeys_ReturnsFalse()
        {
            bool ok = McpArgumentParser.TryParseEntityAndKeys(
                Parse(@"{ ""entity"": ""Book"" }"), out _, out _, out string error);

            Assert.IsFalse(ok);
            StringAssert.Contains(error, "keys");
        }

        [TestMethod]
        public void TryParseEntityAndKeys_KeysNotObject_ReturnsFalse()
        {
            bool ok = McpArgumentParser.TryParseEntityAndKeys(
                Parse(@"{ ""entity"": ""Book"", ""keys"": [1, 2] }"), out _, out _, out string error);

            Assert.IsFalse(ok);
            StringAssert.Contains(error, "JSON object");
        }

        [TestMethod]
        public void TryParseEntityAndKeys_EmptyKeys_ReturnsFalse()
        {
            bool ok = McpArgumentParser.TryParseEntityAndKeys(
                Parse(@"{ ""entity"": ""Book"", ""keys"": { } }"), out _, out _, out string error);

            Assert.IsFalse(ok);
            StringAssert.Contains(error, "Keys are required");
        }

        [TestMethod]
        public void TryParseEntityAndKeys_NullKeyValue_ReturnsFalse()
        {
            bool ok = McpArgumentParser.TryParseEntityAndKeys(
                Parse(@"{ ""entity"": ""Book"", ""keys"": { ""id"": null } }"), out _, out _, out string error);

            Assert.IsFalse(ok);
            StringAssert.Contains(error, "cannot be null or empty");
        }

        [TestMethod]
        public void TryParseEntityKeysAndFields_Valid_ReturnsTrue()
        {
            bool ok = McpArgumentParser.TryParseEntityKeysAndFields(
                Parse(@"{ ""entity"": ""Book"", ""keys"": { ""id"": 1 }, ""fields"": { ""title"": ""t"" } }"),
                out string entity, out Dictionary<string, object?> keys, out Dictionary<string, object?> fields, out string error);

            Assert.IsTrue(ok);
            Assert.AreEqual("Book", entity);
            Assert.AreEqual(1, keys.Count);
            Assert.AreEqual(1, fields.Count);
            Assert.AreEqual(string.Empty, error);
        }

        [TestMethod]
        public void TryParseEntityKeysAndFields_MissingFields_ReturnsFalse()
        {
            bool ok = McpArgumentParser.TryParseEntityKeysAndFields(
                Parse(@"{ ""entity"": ""Book"", ""keys"": { ""id"": 1 } }"),
                out _, out _, out _, out string error);

            Assert.IsFalse(ok);
            StringAssert.Contains(error, "fields");
        }

        [TestMethod]
        public void TryParseEntityKeysAndFields_EmptyFields_ReturnsFalse()
        {
            bool ok = McpArgumentParser.TryParseEntityKeysAndFields(
                Parse(@"{ ""entity"": ""Book"", ""keys"": { ""id"": 1 }, ""fields"": { } }"),
                out _, out _, out _, out string error);

            Assert.IsFalse(ok);
            StringAssert.Contains(error, "field must be provided");
        }

        [TestMethod]
        public void TryParseExecuteArguments_NonObjectRoot_ReturnsFalse()
        {
            bool ok = McpArgumentParser.TryParseExecuteArguments(
                Parse("[1, 2, 3]"), out _, out _, out string error);

            Assert.IsFalse(ok);
            StringAssert.Contains(error, "Arguments must be an object");
        }

        [TestMethod]
        public void TryParseExecuteArguments_WithTypedParameters_AreConverted()
        {
            string json = @"{
                ""entity"": ""GetBooks"",
                ""parameters"": {
                    ""name"": ""abc"",
                    ""count"": 7,
                    ""ratio"": 1.5,
                    ""active"": true,
                    ""nothing"": null
                }
            }";

            bool ok = McpArgumentParser.TryParseExecuteArguments(
                Parse(json), out string entity, out Dictionary<string, object?> parameters, out string error);

            Assert.IsTrue(ok);
            Assert.AreEqual("GetBooks", entity);
            Assert.AreEqual(string.Empty, error);
            Assert.AreEqual("abc", parameters["name"]);
            Assert.AreEqual(7L, parameters["count"]);
            Assert.AreEqual(1.5m, parameters["ratio"]);
            Assert.AreEqual(true, parameters["active"]);
            Assert.IsNull(parameters["nothing"]);
        }

        [TestMethod]
        public void TryParseExecuteArguments_NoParameters_ReturnsEmptyDictionary()
        {
            bool ok = McpArgumentParser.TryParseExecuteArguments(
                Parse(@"{ ""entity"": ""GetBooks"" }"),
                out string entity, out Dictionary<string, object?> parameters, out _);

            Assert.IsTrue(ok);
            Assert.AreEqual("GetBooks", entity);
            Assert.AreEqual(0, parameters.Count);
        }
    }
}
