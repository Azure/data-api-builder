using System;
using System.Linq;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.GraphQLBuilder.Directives;
using Azure.DataGateway.Service.GraphQLBuilder.Sql;
using HotChocolate.Language;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.GraphQLBuilder.Sql
{
    [TestClass]
    [TestCategory("GraphQL Schema Builder")]
    public class SchemaConverterTests
    {
        [DataTestMethod]
        [DataRow("test", "Test")]
        [DataRow("Test", "Test")]
        [DataRow("With Space", "WithSpace")]
        [DataRow("with space", "WithSpace")]
        [DataRow("@test", "Test")]
        [DataRow("_test", "Test")]
        [DataRow("#test", "Test")]
        [DataRow("T.est", "Test")]
        [DataRow("T_est", "T_est")]
        [DataRow("Test1", "Test1")]
        public void TableNameBecomesObjectName(string tableName, string expected)
        {
            TableDefinition table = new();

            ObjectTypeDefinitionNode od = SchemaConverter.FromTableDefinition(tableName, table);

            Assert.AreEqual(expected, od.Name.Value);
        }

        [DataTestMethod]
        [DataRow("test", "test")]
        [DataRow("Test", "test")]
        [DataRow("With Space", "withSpace")]
        [DataRow("with space", "withSpace")]
        [DataRow("@test", "test")]
        [DataRow("_test", "test")]
        [DataRow("#test", "test")]
        [DataRow("T.est", "test")]
        [DataRow("T_est", "t_est")]
        [DataRow("Test1", "test1")]
        public void ColumnNameBecomesFieldName(string columnName, string expected)
        {
            TableDefinition table = new();

            table.Columns.Add(columnName, new ColumnDefinition
            {
                SystemType = typeof(string)
            });

            ObjectTypeDefinitionNode od = SchemaConverter.FromTableDefinition("table", table);

            Assert.AreEqual(expected, od.Fields[0].Name.Value);
        }

        [TestMethod]
        public void PrimaryKeyColumnHasAppropriateDirective()
        {
            TableDefinition table = new();

            string columnName = "columnName";
            table.Columns.Add(columnName, new ColumnDefinition
            {
                SystemType = typeof(string)
            });
            table.PrimaryKey.Add(columnName);

            ObjectTypeDefinitionNode od = SchemaConverter.FromTableDefinition("table", table);

            FieldDefinitionNode field = od.Fields.First(f => f.Name.Value == columnName);
            Assert.AreEqual(1, field.Directives.Count);
            Assert.AreEqual(PrimaryKeyDirective.DirectiveName, field.Directives[0].Name.Value);
        }

        [TestMethod]
        public void MultiplePrimaryKeysAllMappedWithDirectives()
        {
            TableDefinition table = new();

            for (int i = 0; i < 5; i++)
            {
                string columnName = $"col{i}";
                table.Columns.Add(columnName, new ColumnDefinition { SystemType = typeof(string) });
                table.PrimaryKey.Add(columnName);
            }

            ObjectTypeDefinitionNode od = SchemaConverter.FromTableDefinition("table", table);

            foreach (FieldDefinitionNode field in od.Fields)
            {
                Assert.AreEqual(1, field.Directives.Count);
                Assert.AreEqual(PrimaryKeyDirective.DirectiveName, field.Directives[0].Name.Value);
            }
        }

        [TestMethod]
        public void MultipleColumnsAllMapped()
        {
            TableDefinition table = new();

            for (int i = 0; i < 5; i++)
            {
                table.Columns.Add($"col{i}", new ColumnDefinition { SystemType = typeof(string) });
            }

            ObjectTypeDefinitionNode od = SchemaConverter.FromTableDefinition("table", table);

            Assert.AreEqual(table.Columns.Count, od.Fields.Count);
        }

        [DataTestMethod]
        [DataRow(typeof(string), "String")]
        [DataRow(typeof(long), "Int")]
        //[DataRow(typeof(int), "Int")]
        //[DataRow(typeof(short), "Int")]
        //[DataRow(typeof(float), "Float")]
        //[DataRow(typeof(decimal), "Float")]
        //[DataRow(typeof(double), "Float")]
        //[DataRow(typeof(bool), "Boolean")]
        public void SystemTypeMapsToCorrectGraphQLType(Type systemType, string graphQLType)
        {
            TableDefinition table = new();

            string columnName = "columnName";
            table.Columns.Add(columnName, new ColumnDefinition
            {
                SystemType = systemType
            });

            ObjectTypeDefinitionNode od = SchemaConverter.FromTableDefinition("table", table);

            FieldDefinitionNode field = od.Fields.First(f => f.Name.Value == columnName);
            Assert.AreEqual(graphQLType, field.Type.NamedType().Name.Value);
        }

        [TestMethod]
        public void NullColumnBecomesNullField()
        {
            TableDefinition table = new();

            string columnName = "columnName";
            table.Columns.Add(columnName, new ColumnDefinition
            {
                SystemType = typeof(string),
                IsNullable = true,
            });

            ObjectTypeDefinitionNode od = SchemaConverter.FromTableDefinition("table", table);

            FieldDefinitionNode field = od.Fields.First(f => f.Name.Value == columnName);
            Assert.IsFalse(field.Type.IsNonNullType());
        }

        [TestMethod]
        public void NonNullColumnBecomesNonNullField()
        {
            TableDefinition table = new();

            string columnName = "columnName";
            table.Columns.Add(columnName, new ColumnDefinition
            {
                SystemType = typeof(string),
                IsNullable = false,
            });

            ObjectTypeDefinitionNode od = SchemaConverter.FromTableDefinition("table", table);

            FieldDefinitionNode field = od.Fields.First(f => f.Name.Value == columnName);
            Assert.IsTrue(field.Type.IsNonNullType());
        }

        [TestMethod]
        public void ForeignKeyGeneratesObjectAndColumnField()
        {
            TableDefinition table = new();

            string columnName = "columnName";
            table.Columns.Add(columnName, new ColumnDefinition
            {
                SystemType = typeof(string),
                IsNullable = false,
            });
            table.ForeignKeys.Add(columnName, new ForeignKeyDefinition { ReferencedTable = "fkTable" });

            ObjectTypeDefinitionNode od = SchemaConverter.FromTableDefinition("table", table);

            Assert.AreEqual(2, od.Fields.Count);
        }

        [TestMethod]
        public void ForeignKeyObjectFieldNameAndTypeMatchesReferenceTable()
        {
            TableDefinition table = new();

            string columnName = "columnName";
            table.Columns.Add(columnName, new ColumnDefinition
            {
                SystemType = typeof(string),
                IsNullable = false,
            });
            const string foreignKeyTable = "FkTable";
            table.ForeignKeys.Add(columnName, new ForeignKeyDefinition { ReferencedTable = foreignKeyTable });

            ObjectTypeDefinitionNode od = SchemaConverter.FromTableDefinition("table", table);

            FieldDefinitionNode field = od.Fields.First(f => f.Name.Value != columnName);

            Assert.AreEqual("fkTables", field.Name.Value);
            Assert.AreEqual(foreignKeyTable, field.Type.NamedType().Name.Value);
        }
    }
}
