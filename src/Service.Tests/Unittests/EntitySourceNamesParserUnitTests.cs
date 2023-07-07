// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Core.Parsers;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Test class for the schema parser. Verifies
    /// that EntitySourceNamesParser.ParseSchemaAndTable()
    /// can handle a wide range of valid formats correctly,
    /// and throws exceptions for invalid formats as expected.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class EntitySourceNamesParserUnitTests
    {

        #region Positive Cases

        /// <summary>
        /// Verifies correct parsing for valid
        /// schema and table formats.
        /// </summary>
        /// <param name="schemaTable">Schema and table name to be parsed.</param>
        /// <param name="schema">Expected parsed schema.</param>
        /// <param name="table">Expected parsed table.</param>
        [DataTestMethod]
        [DataRow("table", "", "table")]
        [DataRow("schema.table", "schema", "table")]
        [DataRow("[schema].table", "schema", "table")]
        [DataRow("schema.[tabl.]", "schema", "tabl.")]
        [DataRow("schema.[tabl.]]]", "schema", "tabl.]]")]
        [DataRow("[[sche]].abc].table", "[sche]].abc", "table")]
        [DataRow("[[sche.ma].table", "[sche.ma", "table")]
        [DataRow("[[sche.ma]]].[table]]]", "[sche.ma]]", "table]]")]
        [DataRow("[[schema]].[ta[ble]]]", "", "[schema]].[ta[ble]]")]
        [DataRow("[sche]]].abc", "sche]]", "abc")]
        [DataRow("[schem]].]].a].[tabl.]]]", "schem]].]].a", "tabl.]]")]
        public void ParseValidSchemaAndTableNames(string schemaTable, string schema, string table)
        {
            (string, string) expected = (schema, table);
            (string, string) actual = EntitySourceNamesParser.ParseSchemaAndTable(schemaTable);
            Assert.AreEqual(expected, actual);
        }

        #endregion

        #region Negative Cases

        /// <summary>
        /// Verifies correct exception for
        /// invalid schema and table formats.
        /// </summary>
        /// <param name="schemaTable">An invalid schema and table name.</param>
        [DataTestMethod]
        [DataRow("abc.abc[abc]")]
        [DataRow("abc.abc]")]
        [DataRow("[abc.abc")]
        [DataRow("a.[]")]
        [DataRow("")]
        [DataRow("[].[]")]
        [DataRow("[a].[]")]
        [DataRow("a.")]
        [DataRow("[")]
        [DataRow("]")]
        [DataRow(".")]
        [DataRow("]]")]
        [DataRow("].")]
        [DataRow(".")]
        [DataRow(".].].].].]")]
        [DataRow("[[]]]]]]]][")]
        [DataRow("a].[abc]")]
        [DataRow("a.a[b]c")]
        [DataRow(null)]
        [DataRow("[abc].")]
        [DataRow("[abc.]abc.table")]
        [DataRow("[abc.].[abc")]
        [DataRow("abc.[tabl.].[extratoken]")]
        [DataRow("[abc]...")]
        [DataRow("[sche.ma].[tab]]")]
        [DataRow("...f")]
        [DataRow("[abc].fda[")]
        [DataRow("[abc.fda")]
        [DataRow("ab[c].fda")]
        public void ParseInvalidSchemaAndTableNames(string schemaTable)
        {
            Assert.ThrowsException<DataApiBuilderException>(() => EntitySourceNamesParser.ParseSchemaAndTable(schemaTable));
        }

        #endregion
    }
}
