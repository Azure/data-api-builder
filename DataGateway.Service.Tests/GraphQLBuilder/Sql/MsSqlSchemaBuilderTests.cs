using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Azure.DataGateway.Service.GraphQLBuilder.Sql;
using HotChocolate.Language;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataGateway.Service.Tests.GraphQLBuilder.Sql
{
    [TestClass]
    public class MsSqlSchemaBuilderTests
    {
        [TestMethod]
        public async Task ShouldCreateOneTypeForEachTable()
        {
            DataTable mockSchema = GenerateMockSchema();

            Mock<DbConnection> conn = new();

            conn.Setup(c => c.GetSchemaAsync(It.IsAny<string>(), It.IsAny<string[]>(), default).Result).Returns(mockSchema);

            DocumentNode types = await SchemaConverter.FromSchema(conn.Object);

            Assert.AreEqual(3, types.Definitions.Count);
        }

        [TestMethod]
        public async Task TypeNamesMatchTableNames()
        {
            DataTable mockSchema = GenerateMockSchema();

            Mock<DbConnection> conn = new();

            conn.Setup(c => c.GetSchemaAsync(It.IsAny<string>(), It.IsAny<string[]>(), default).Result).Returns(mockSchema);

            DocumentNode types = await SchemaConverter.FromSchema(conn.Object);

            List<string> tableNames = mockSchema.Rows.Cast<DataRow>().Select(r => (string)r["TABLE_NAME"]).Distinct().ToList();
            List<string> typeNames = types.Definitions.Cast<NamedSyntaxNode>().Select(def => def.Name.Value).ToList();

            CollectionAssert.AreEquivalent(tableNames, typeNames);
        }

        private static DataTable GenerateMockSchema()
        {
            DataTable schema = new();
            schema.Columns.Add("TABLE_CATALOG");
            schema.Columns.Add("TABLE_SCHEMA");
            schema.Columns.Add("TABLE_NAME");
            schema.Columns.Add("COLUMN_NAME");
            schema.Columns.Add("ORDINAL_POSITION");
            schema.Columns.Add("COLUMN_DEFAULT");
            schema.Columns.Add("IS_NULLABLE");
            schema.Columns.Add("DATA_TYPE");
            schema.Columns.Add("CHARACTER_MAXIMUM_LENGTH");
            schema.Columns.Add("CHARACTER_OCTET_LENGTH");
            schema.Columns.Add("NUMERIC_PRECISION");
            schema.Columns.Add("NUMERIC_PRECISION_RADIX");
            schema.Columns.Add("NUMERIC_SCALE");
            schema.Columns.Add("DATETIME_PRECISION");
            schema.Columns.Add("CHARACTER_SET_CATALOG");
            schema.Columns.Add("CHARACTER_SET_SCHEMA");
            schema.Columns.Add("CHARACTER_SET_NAME");
            schema.Columns.Add("COLLATION_CATALOG");
            schema.Columns.Add("IS_SPARSE");
            schema.Columns.Add("IS_COLUMN_SET");
            schema.Columns.Add("IS_FILESTREAM");

            DataRow row = schema.NewRow();
            row["TABLE_CATALOG"] = "datagatewaytest";
            row["TABLE_SCHEMA"] = "dbo";
            row["TABLE_NAME"] = "authors";
            row["COLUMN_NAME"] = "id";
            row["ORDINAL_POSITION"] = "1";
            row["COLUMN_DEFAULT"] = "";
            row["IS_NULLABLE"] = "NO";
            row["DATA_TYPE"] = "bigint";
            row["CHARACTER_MAXIMUM_LENGTH"] = "";
            row["CHARACTER_OCTET_LENGTH"] = "";
            row["NUMERIC_PRECISION"] = "19";
            row["NUMERIC_PRECISION_RADIX"] = "10";
            row["NUMERIC_SCALE"] = "0";
            row["DATETIME_PRECISION"] = "";
            row["CHARACTER_SET_CATALOG"] = "";
            row["CHARACTER_SET_SCHEMA"] = "";
            row["CHARACTER_SET_NAME"] = "";
            row["COLLATION_CATALOG"] = "";
            row["IS_SPARSE"] = "False";
            row["IS_COLUMN_SET"] = "False";
            row["IS_FILESTREAM"] = "False";
            schema.Rows.Add(row);

            row = schema.NewRow();
            row["TABLE_CATALOG"] = "datagatewaytest";
            row["TABLE_SCHEMA"] = "dbo";
            row["TABLE_NAME"] = "authors";
            row["COLUMN_NAME"] = "name";
            row["ORDINAL_POSITION"] = "2";
            row["COLUMN_DEFAULT"] = "";
            row["IS_NULLABLE"] = "NO";
            row["DATA_TYPE"] = "varchar";
            row["CHARACTER_MAXIMUM_LENGTH"] = "-1";
            row["CHARACTER_OCTET_LENGTH"] = "-1";
            row["NUMERIC_PRECISION"] = "";
            row["NUMERIC_PRECISION_RADIX"] = "";
            row["NUMERIC_SCALE"] = "";
            row["DATETIME_PRECISION"] = "";
            row["CHARACTER_SET_CATALOG"] = "";
            row["CHARACTER_SET_SCHEMA"] = "";
            row["CHARACTER_SET_NAME"] = "iso_1";
            row["COLLATION_CATALOG"] = "";
            row["IS_SPARSE"] = "False";
            row["IS_COLUMN_SET"] = "False";
            row["IS_FILESTREAM"] = "False";
            schema.Rows.Add(row);

            row = schema.NewRow();
            row["TABLE_CATALOG"] = "datagatewaytest";
            row["TABLE_SCHEMA"] = "dbo";
            row["TABLE_NAME"] = "authors";
            row["COLUMN_NAME"] = "birthdate";
            row["ORDINAL_POSITION"] = "3";
            row["COLUMN_DEFAULT"] = "";
            row["IS_NULLABLE"] = "NO";
            row["DATA_TYPE"] = "varchar";
            row["CHARACTER_MAXIMUM_LENGTH"] = "-1";
            row["CHARACTER_OCTET_LENGTH"] = "-1";
            row["NUMERIC_PRECISION"] = "";
            row["NUMERIC_PRECISION_RADIX"] = "";
            row["NUMERIC_SCALE"] = "";
            row["DATETIME_PRECISION"] = "";
            row["CHARACTER_SET_CATALOG"] = "";
            row["CHARACTER_SET_SCHEMA"] = "";
            row["CHARACTER_SET_NAME"] = "iso_1";
            row["COLLATION_CATALOG"] = "";
            row["IS_SPARSE"] = "False";
            row["IS_COLUMN_SET"] = "False";
            row["IS_FILESTREAM"] = "False";
            schema.Rows.Add(row);

            row = schema.NewRow();
            row["TABLE_CATALOG"] = "datagatewaytest";
            row["TABLE_SCHEMA"] = "dbo";
            row["TABLE_NAME"] = "book_author_link";
            row["COLUMN_NAME"] = "book_id";
            row["ORDINAL_POSITION"] = "1";
            row["COLUMN_DEFAULT"] = "";
            row["IS_NULLABLE"] = "NO";
            row["DATA_TYPE"] = "bigint";
            row["CHARACTER_MAXIMUM_LENGTH"] = "";
            row["CHARACTER_OCTET_LENGTH"] = "";
            row["NUMERIC_PRECISION"] = "19";
            row["NUMERIC_PRECISION_RADIX"] = "10";
            row["NUMERIC_SCALE"] = "0";
            row["DATETIME_PRECISION"] = "";
            row["CHARACTER_SET_CATALOG"] = "";
            row["CHARACTER_SET_SCHEMA"] = "";
            row["CHARACTER_SET_NAME"] = "";
            row["COLLATION_CATALOG"] = "";
            row["IS_SPARSE"] = "False";
            row["IS_COLUMN_SET"] = "False";
            row["IS_FILESTREAM"] = "False";
            schema.Rows.Add(row);

            row = schema.NewRow();
            row["TABLE_CATALOG"] = "datagatewaytest";
            row["TABLE_SCHEMA"] = "dbo";
            row["TABLE_NAME"] = "book_author_link";
            row["COLUMN_NAME"] = "author_id";
            row["ORDINAL_POSITION"] = "2";
            row["COLUMN_DEFAULT"] = "";
            row["IS_NULLABLE"] = "NO";
            row["DATA_TYPE"] = "bigint";
            row["CHARACTER_MAXIMUM_LENGTH"] = "";
            row["CHARACTER_OCTET_LENGTH"] = "";
            row["NUMERIC_PRECISION"] = "19";
            row["NUMERIC_PRECISION_RADIX"] = "10";
            row["NUMERIC_SCALE"] = "0";
            row["DATETIME_PRECISION"] = "";
            row["CHARACTER_SET_CATALOG"] = "";
            row["CHARACTER_SET_SCHEMA"] = "";
            row["CHARACTER_SET_NAME"] = "";
            row["COLLATION_CATALOG"] = "";
            row["IS_SPARSE"] = "False";
            row["IS_COLUMN_SET"] = "False";
            row["IS_FILESTREAM"] = "False";
            schema.Rows.Add(row);

            row = schema.NewRow();
            row["TABLE_CATALOG"] = "datagatewaytest";
            row["TABLE_SCHEMA"] = "dbo";
            row["TABLE_NAME"] = "books";
            row["COLUMN_NAME"] = "id";
            row["ORDINAL_POSITION"] = "1";
            row["COLUMN_DEFAULT"] = "";
            row["IS_NULLABLE"] = "NO";
            row["DATA_TYPE"] = "bigint";
            row["CHARACTER_MAXIMUM_LENGTH"] = "";
            row["CHARACTER_OCTET_LENGTH"] = "";
            row["NUMERIC_PRECISION"] = "19";
            row["NUMERIC_PRECISION_RADIX"] = "10";
            row["NUMERIC_SCALE"] = "0";
            row["DATETIME_PRECISION"] = "";
            row["CHARACTER_SET_CATALOG"] = "";
            row["CHARACTER_SET_SCHEMA"] = "";
            row["CHARACTER_SET_NAME"] = "";
            row["COLLATION_CATALOG"] = "";
            row["IS_SPARSE"] = "False";
            row["IS_COLUMN_SET"] = "False";
            row["IS_FILESTREAM"] = "False";
            schema.Rows.Add(row);

            row = schema.NewRow();
            row["TABLE_CATALOG"] = "datagatewaytest";
            row["TABLE_SCHEMA"] = "dbo";
            row["TABLE_NAME"] = "books";
            row["COLUMN_NAME"] = "title";
            row["ORDINAL_POSITION"] = "2";
            row["COLUMN_DEFAULT"] = "";
            row["IS_NULLABLE"] = "NO";
            row["DATA_TYPE"] = "varchar";
            row["CHARACTER_MAXIMUM_LENGTH"] = "-1";
            row["CHARACTER_OCTET_LENGTH"] = "-1";
            row["NUMERIC_PRECISION"] = "";
            row["NUMERIC_PRECISION_RADIX"] = "";
            row["NUMERIC_SCALE"] = "";
            row["DATETIME_PRECISION"] = "";
            row["CHARACTER_SET_CATALOG"] = "";
            row["CHARACTER_SET_SCHEMA"] = "";
            row["CHARACTER_SET_NAME"] = "iso_1";
            row["COLLATION_CATALOG"] = "";
            row["IS_SPARSE"] = "False";
            row["IS_COLUMN_SET"] = "False";
            row["IS_FILESTREAM"] = "False";
            schema.Rows.Add(row);

            row = schema.NewRow();
            row["TABLE_CATALOG"] = "datagatewaytest";
            row["TABLE_SCHEMA"] = "dbo";
            row["TABLE_NAME"] = "books";
            row["COLUMN_NAME"] = "publisher_id";
            row["ORDINAL_POSITION"] = "3";
            row["COLUMN_DEFAULT"] = "";
            row["IS_NULLABLE"] = "NO";
            row["DATA_TYPE"] = "bigint";
            row["CHARACTER_MAXIMUM_LENGTH"] = "";
            row["CHARACTER_OCTET_LENGTH"] = "";
            row["NUMERIC_PRECISION"] = "19";
            row["NUMERIC_PRECISION_RADIX"] = "10";
            row["NUMERIC_SCALE"] = "0";
            row["DATETIME_PRECISION"] = "";
            row["CHARACTER_SET_CATALOG"] = "";
            row["CHARACTER_SET_SCHEMA"] = "";
            row["CHARACTER_SET_NAME"] = "";
            row["COLLATION_CATALOG"] = "";
            row["IS_SPARSE"] = "False";
            row["IS_COLUMN_SET"] = "False";
            row["IS_FILESTREAM"] = "False";
            schema.Rows.Add(row);

            return schema;
        }
    }
}
