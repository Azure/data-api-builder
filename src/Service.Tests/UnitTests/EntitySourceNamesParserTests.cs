// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Core.Parsers;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for <see cref="EntitySourceNamesParser.ParseSchemaAndTable"/>.
    /// Pure logic; no database required.
    /// </summary>
    [TestClass]
    public class EntitySourceNamesParserTests
    {
        [TestMethod]
        public void ParseSchemaAndTable_TableOnly_UsesDefaultSchema()
        {
            (string? schema, string table) = EntitySourceNamesParser.ParseSchemaAndTable("books");

            Assert.AreEqual(string.Empty, schema);
            Assert.AreEqual("books", table);
        }

        [TestMethod]
        public void ParseSchemaAndTable_SchemaAndTable_AreSplit()
        {
            (string? schema, string table) = EntitySourceNamesParser.ParseSchemaAndTable("dbo.books");

            Assert.AreEqual("dbo", schema);
            Assert.AreEqual("books", table);
        }

        [TestMethod]
        public void ParseSchemaAndTable_BracketedSchemaWithDot_IsParsed()
        {
            (string? schema, string table) = EntitySourceNamesParser.ParseSchemaAndTable("[sch.ema].books");

            Assert.AreEqual("sch.ema", schema);
            Assert.AreEqual("books", table);
        }

        [TestMethod]
        public void ParseSchemaAndTable_BracketedTableWithDot_IsParsed()
        {
            (string? schema, string table) = EntitySourceNamesParser.ParseSchemaAndTable("dbo.[ta.ble]");

            Assert.AreEqual("dbo", schema);
            Assert.AreEqual("ta.ble", table);
        }

        [TestMethod]
        public void ParseSchemaAndTable_EscapedBrackets_ReturnsRawInnerContent()
        {
            (string? schema, string table) = EntitySourceNamesParser.ParseSchemaAndTable("[sche]]ma].[ta.ble]");

            Assert.AreEqual("sche]]ma", schema);
            Assert.AreEqual("ta.ble", table);
        }

        [DataTestMethod]
        [DataRow("", DisplayName = "Empty input")]
        [DataRow("books.", DisplayName = "Ends with dot")]
        [DataRow("a.b.c", DisplayName = "More than two tokens")]
        [DataRow("abc]xyz", DisplayName = "Unbracketed token with bracket char")]
        [DataRow("[abc]xyz", DisplayName = "Invalid character after closing bracket")]
        [DataRow("[abcdef", DisplayName = "No closing bracket")]
        public void ParseSchemaAndTable_InvalidInput_Throws(string input)
        {
            Assert.ThrowsException<DataApiBuilderException>(
                () => EntitySourceNamesParser.ParseSchemaAndTable(input));
        }
    }
}
