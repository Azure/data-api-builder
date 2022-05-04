using System;
using Azure.DataGateway.Service.Parsers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests
{
    /// <summary>
    /// Test class for the schema parser. Verifies
    /// that the schema can parse a wide range of
    /// formats correctly, and throws exceptions
    /// when expected.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class EntitySourceNamesParserUnitTests
    {

        [TestMethod]
        public void Table()
        {
            string schemaTable = "table";
            (string, string) expected = ("", "table");
            (string, string) actual = EntitySourceNamesParser.ParseSchemaAndTable(schemaTable);
            Assert.IsTrue(expected.Equals(actual));
        }

        [TestMethod]
        public void SchemaTable()
        {
            string schemaTable = "schema.table";
            (string, string) expected = ("schema", "table");
            (string, string) actual = EntitySourceNamesParser.ParseSchemaAndTable(schemaTable);
            Assert.IsTrue(expected.Equals(actual));
        }

        [TestMethod]
        public void SchemaAndTable()
        {
            string schemaTable = "[schema].table";
            (string, string) expected = ("schema", "table");
            (string, string) actual = EntitySourceNamesParser.ParseSchemaAndTable(schemaTable);
            Assert.IsTrue(expected.Equals(actual));
        }

        [TestMethod]
        public void SchemaAndTable2()
        {
            string schemaTable = "schema.[tabl.]";
            (string, string) expected = ("schema", "tabl.");
            (string, string) actual = EntitySourceNamesParser.ParseSchemaAndTable(schemaTable);
            Assert.IsTrue(expected.Equals(actual));
        }

        [TestMethod]
        public void SchemaAndTable3()
        {
            string schemaTable = "schema.[tabl.]]]";
            (string, string) expected = ("schema", "tabl.]]");
            (string, string) actual = EntitySourceNamesParser.ParseSchemaAndTable(schemaTable);
            Assert.IsTrue(expected.Equals(actual));
        }

        [TestMethod]
        public void SchemaAndTable4()
        {
            string schemaTable = "[[sche]].abc].table";
            (string, string) expected = ("[sche]].abc", "table");
            (string, string) actual = EntitySourceNamesParser.ParseSchemaAndTable(schemaTable);
            Assert.IsTrue(expected.Equals(actual));
        }

        [TestMethod]
        public void SchemaAndTable5()
        {
            string schemaTable = "[[sche.ma].table";
            (string, string) expected = ("[sche.ma", "table");
            (string, string) actual = EntitySourceNamesParser.ParseSchemaAndTable(schemaTable);
            Assert.IsTrue(expected.Equals(actual));
        }

        [TestMethod]
        public void SchemaAndTable6()
        {
            string schemaTable = "[[sche.ma]]].[table]]]";
            (string, string) expected = ("[sche.ma]]", "table]]");
            (string, string) actual = EntitySourceNamesParser.ParseSchemaAndTable(schemaTable);
            Assert.IsTrue(expected.Equals(actual));
        }

        [TestMethod]
        public void Table2()
        {
            string schemaTable = "[[schema]].[ta[ble]]]";
            (string, string) expected = ("", "[schema]].[ta[ble]]");
            (string, string) actual = EntitySourceNamesParser.ParseSchemaAndTable(schemaTable);
            Assert.IsTrue(expected.Equals(actual));
        }

        [TestMethod]
        public void Table3()
        {
            string schemaTable = "[].[a]";
            (string, string) expected = ("", "a");
            (string, string) actual = EntitySourceNamesParser.ParseSchemaAndTable(schemaTable);
            Assert.IsTrue(expected.Equals(actual));
        }

        [TestMethod]
        public void SchemaAndTable7()
        {
            string schemaTable = "[sche]]].abc";
            (string, string) expected = ("sche]]", "abc");
            (string, string) actual = EntitySourceNamesParser.ParseSchemaAndTable(schemaTable);
            Assert.IsTrue(expected.Equals(actual));
        }

        [TestMethod]
        public void SchemaAndTable8()
        {
            string schemaTable = "[schem]].]].a].[tabl.]]]";
            (string, string) expected = ("schem]].]].a", "tabl.]]");
            (string, string) actual = EntitySourceNamesParser.ParseSchemaAndTable(schemaTable);
            Assert.IsTrue(expected.Equals(actual));
        }

        [TestMethod]
        public void ParseException1()
        {
            string schemaTable = "abc.abc[abc]";
            Assert.ThrowsException<ArgumentException>(() => EntitySourceNamesParser.ParseSchemaAndTable(schemaTable));

        }

        [TestMethod]
        public void ParseException2()
        {
            string schemaTable = "abc.abc]";
            Assert.ThrowsException<ArgumentException>(() => EntitySourceNamesParser.ParseSchemaAndTable(schemaTable));

        }

        [TestMethod]
        public void ParseException3()
        {
            string schemaTable = "[abc.abc";
            Assert.ThrowsException<ArgumentException>(() => EntitySourceNamesParser.ParseSchemaAndTable(schemaTable));

        }

        [TestMethod]
        public void ParseException4()
        {
            string schemaTable = "a.[]";
            Assert.ThrowsException<ArgumentException>(() => EntitySourceNamesParser.ParseSchemaAndTable(schemaTable));

        }

        [TestMethod]
        public void ParseException5()
        {
            string schemaTable = "[].[]";
            Assert.ThrowsException<ArgumentException>(() => EntitySourceNamesParser.ParseSchemaAndTable(schemaTable));

        }

        [TestMethod]
        public void ParseException6()
        {
            string schemaTable = "[a].[]";
            Assert.ThrowsException<ArgumentException>(() => EntitySourceNamesParser.ParseSchemaAndTable(schemaTable));

        }

        [TestMethod]
        public void ParseException8()
        {
            string schemaTable = "a.";
            Assert.ThrowsException<ArgumentException>(() => EntitySourceNamesParser.ParseSchemaAndTable(schemaTable));

        }

        [TestMethod]
        public void ParseException9()
        {
            string schemaTable = "[";
            Assert.ThrowsException<ArgumentException>(() => EntitySourceNamesParser.ParseSchemaAndTable(schemaTable));

        }

        [TestMethod]
        public void ParseException10()
        {
            string schemaTable = "]";
            Assert.ThrowsException<ArgumentException>(() => EntitySourceNamesParser.ParseSchemaAndTable(schemaTable));

        }

        [TestMethod]
        public void ParseException11()
        {
            string schemaTable = ".";
            Assert.ThrowsException<ArgumentException>(() => EntitySourceNamesParser.ParseSchemaAndTable(schemaTable));

        }

        [TestMethod]
        public void ParseException12()
        {
            string schemaTable = "]]";
            Assert.ThrowsException<ArgumentException>(() => EntitySourceNamesParser.ParseSchemaAndTable(schemaTable));

        }

        [TestMethod]
        public void ParseException13()
        {
            string schemaTable = "].";
            Assert.ThrowsException<ArgumentException>(() => EntitySourceNamesParser.ParseSchemaAndTable(schemaTable));

        }

        [TestMethod]
        public void ParseException14()
        {
            string schemaTable = ".";
            Assert.ThrowsException<ArgumentException>(() => EntitySourceNamesParser.ParseSchemaAndTable(schemaTable));

        }

        [TestMethod]
        public void ParseException15()
        {
            string schemaTable = ".].].].].]";
            Assert.ThrowsException<ArgumentException>(() => EntitySourceNamesParser.ParseSchemaAndTable(schemaTable));

        }

        [TestMethod]
        public void ParseException16()
        {
            string schemaTable = "[[]]]]]]]][";
            Assert.ThrowsException<ArgumentException>(() => EntitySourceNamesParser.ParseSchemaAndTable(schemaTable));

        }

        [TestMethod]
        public void ParseException17()
        {
            string schemaTable = "a].[abc]";
            Assert.ThrowsException<ArgumentException>(() => EntitySourceNamesParser.ParseSchemaAndTable(schemaTable));

        }

        [TestMethod]
        public void ParseException18()
        {
            string schemaTable = "a.a[b]c";
            Assert.ThrowsException<ArgumentException>(() => EntitySourceNamesParser.ParseSchemaAndTable(schemaTable));

        }

        [TestMethod]
        public void ParseException20()
        {
            string schemaTable = null;
            Assert.ThrowsException<ArgumentException>(() => EntitySourceNamesParser.ParseSchemaAndTable(schemaTable));

        }

        [TestMethod]
        public void ParseException21()
        {
            string schemaTable = "[abc].";
            Assert.ThrowsException<ArgumentException>(() => EntitySourceNamesParser.ParseSchemaAndTable(schemaTable));

        }

        [TestMethod]
        public void ParseException22()
        {
            string schemaTable = "[abc.]abc.table";
            Assert.ThrowsException<ArgumentException>(() => EntitySourceNamesParser.ParseSchemaAndTable(schemaTable));

        }

        [TestMethod]
        public void ParseException23()
        {
            string schemaTable = "[abc.].[abc";
            Assert.ThrowsException<ArgumentException>(() => EntitySourceNamesParser.ParseSchemaAndTable(schemaTable));

        }

        [TestMethod]
        public void ParseException24()
        {
            string schemaTable = "abc.[tabl.].[extratoken]";
            Assert.ThrowsException<ArgumentException>(() => EntitySourceNamesParser.ParseSchemaAndTable(schemaTable));

        }

        [TestMethod]
        public void ParseException25()
        {
            string schemaTable = "[abc]...";
            Assert.ThrowsException<ArgumentException>(() => EntitySourceNamesParser.ParseSchemaAndTable(schemaTable));
        }

        [TestMethod]
        public void ParseException26()
        {
            string schemaTable = "[sche.ma].[tab]]";
            Assert.ThrowsException<ArgumentException>(() => EntitySourceNamesParser.ParseSchemaAndTable(schemaTable));
        }

        [TestMethod]
        public void ParseException27()
        {
            string schemaTable = "[";
            Assert.ThrowsException<ArgumentException>(() => EntitySourceNamesParser.ParseSchemaAndTable(schemaTable));
        }

        [TestMethod]
        public void ParseException28()
        {
            string schemaTable = "...f";
            Assert.ThrowsException<ArgumentException>(() => EntitySourceNamesParser.ParseSchemaAndTable(schemaTable));
        }

        [TestMethod]
        public void ParseException29()
        {
            string schemaTable = "[abc].fda[";
            Assert.ThrowsException<ArgumentException>(() => EntitySourceNamesParser.ParseSchemaAndTable(schemaTable));
        }

        [TestMethod]
        public void ParseException30()
        {
            string schemaTable = "[abc.fda";
            Assert.ThrowsException<ArgumentException>(() => EntitySourceNamesParser.ParseSchemaAndTable(schemaTable));
        }

        [TestMethod]
        public void ParseException31()
        {
            string schemaTable = "ab[c].fda";
            Assert.ThrowsException<ArgumentException>(() => EntitySourceNamesParser.ParseSchemaAndTable(schemaTable));
        }
    }
}
