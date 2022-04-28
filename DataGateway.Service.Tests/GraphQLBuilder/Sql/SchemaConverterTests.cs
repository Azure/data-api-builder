using System;
using System.Collections.Generic;
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
        private static Entity GenerateEmptyEntity()
        {
            return new Entity("dbo.entity", Rest: null, GraphQL: null, Array.Empty<PermissionSetting>(), Relationships: new(), Mappings: new());
        }

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
        public void EntityNameBecomesObjectName(string entityName, string expected)
        {
            TableDefinition table = new();

            ObjectTypeDefinitionNode od = SchemaConverter.FromTableDefinition(entityName, table, GenerateEmptyEntity(), new());

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

            ObjectTypeDefinitionNode od = SchemaConverter.FromTableDefinition("table", table, GenerateEmptyEntity(), new());

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

            ObjectTypeDefinitionNode od = SchemaConverter.FromTableDefinition("table", table, GenerateEmptyEntity(), new());

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

            ObjectTypeDefinitionNode od = SchemaConverter.FromTableDefinition("table", table, GenerateEmptyEntity(), new());

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

            ObjectTypeDefinitionNode od = SchemaConverter.FromTableDefinition("table", table, GenerateEmptyEntity(), new());

            Assert.AreEqual(table.Columns.Count, od.Fields.Count);
        }

        [DataTestMethod]
        [DataRow(typeof(string), "String")]
        [DataRow(typeof(long), "Int")]
        // TODO: Uncomment these once we have more GraphQL type support - https://github.com/Azure/hawaii-gql/issues/247
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

            ObjectTypeDefinitionNode od = SchemaConverter.FromTableDefinition("table", table, GenerateEmptyEntity(), new());

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

            ObjectTypeDefinitionNode od = SchemaConverter.FromTableDefinition("table", table, GenerateEmptyEntity(), new());

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

            ObjectTypeDefinitionNode od = SchemaConverter.FromTableDefinition("table", table, GenerateEmptyEntity(), new());

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
            const string refColName = "ref_col";
            const string foreignKeyTable = "fkTable";
            table.ForeignKeys.Add("forign_key", new ForeignKeyDefinition { ReferencedTable = foreignKeyTable, ReferencingColumns = new List<string> { refColName } });
            table.Columns.Add(refColName, new ColumnDefinition
            {
                SystemType = typeof(long)
            });

            Dictionary<string, Relationship> relationships =
                new()
                {
                    {
                        foreignKeyTable,
                        new Relationship(
                          Cardinality.One,
                          foreignKeyTable,
                          SourceFields: null,
                          TargetFields: null,
                          LinkingObject: null,
                          LinkingSourceFields: null,
                          LinkingTargetFields: null)
                    }
                };
            Entity configEntity = GenerateEmptyEntity() with { Relationships = relationships };
            Entity relationshipEntity = GenerateEmptyEntity();

            ObjectTypeDefinitionNode od = SchemaConverter.FromTableDefinition("table", table, configEntity, new() { { foreignKeyTable, relationshipEntity } });

            Assert.AreEqual(3, od.Fields.Count);
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
            const string refColName = "ref_col";
            table.ForeignKeys.Add("foreign_key", new ForeignKeyDefinition { ReferencedTable = foreignKeyTable, ReferencingColumns = new List<string> { refColName } });
            table.Columns.Add(refColName, new ColumnDefinition
            {
                SystemType = typeof(long)
            });

            Dictionary<string, Relationship> relationships =
                new()
                {
                    {
                        foreignKeyTable,
                        new Relationship(
                          Cardinality.One,
                          foreignKeyTable,
                          SourceFields: null,
                          TargetFields: null,
                          LinkingObject: null,
                          LinkingSourceFields: null,
                          LinkingTargetFields: null)
                    }
                };
            Entity configEntity = GenerateEmptyEntity() with { Relationships = relationships };
            Entity relationshipEntity = GenerateEmptyEntity();

            ObjectTypeDefinitionNode od = SchemaConverter.FromTableDefinition("table", table, configEntity, new() { { foreignKeyTable, relationshipEntity } });

            FieldDefinitionNode field = od.Fields.First(f => f.Name.Value != refColName && f.Name.Value != columnName);

            Assert.AreEqual("fkTables", field.Name.Value);
            Assert.AreEqual(foreignKeyTable, field.Type.NamedType().Name.Value);
        }

        [TestMethod]
        public void ForeignKeyFieldWillHaveRelationshipDirective()
        {
            TableDefinition table = new();

            string columnName = "columnName";
            table.Columns.Add(columnName, new ColumnDefinition
            {
                SystemType = typeof(string),
                IsNullable = false,
            });
            const string foreignKeyTable = "FkTable";
            const string refColName = "ref_col";
            table.ForeignKeys.Add("foreign_key", new ForeignKeyDefinition { ReferencedTable = foreignKeyTable, ReferencingColumns = new List<string> { refColName } });
            table.Columns.Add(refColName, new ColumnDefinition
            {
                SystemType = typeof(long)
            });

            Dictionary<string, Relationship> relationships =
                new()
                {
                    {
                        foreignKeyTable,
                        new Relationship(
                          Cardinality.One,
                          foreignKeyTable,
                          SourceFields: null,
                          TargetFields: null,
                          LinkingObject: null,
                          LinkingSourceFields: null,
                          LinkingTargetFields: null)
                    }
                };
            Entity configEntity = GenerateEmptyEntity() with { Relationships = relationships };
            Entity relationshipEntity = GenerateEmptyEntity();

            ObjectTypeDefinitionNode od = SchemaConverter.FromTableDefinition("table", table, configEntity, new() { { foreignKeyTable, relationshipEntity } });

            FieldDefinitionNode field = od.Fields.First(f => f.Name.Value == "fkTables");

            Assert.AreEqual(1, field.Directives.Count);
            Assert.AreEqual(RelationshipDirective.DirectiveName, field.Directives[0].Name.Value);
        }

        [TestMethod]
        public void MultipleForeignKeyColumnsStillSingleObjectFieldReference()
        {
            TableDefinition table = new();

            string columnName = "columnName";
            table.Columns.Add(columnName, new ColumnDefinition
            {
                SystemType = typeof(string),
                IsNullable = false,
            });
            const string foreignKeyTable = "FkTable";

            table.ForeignKeys.Add("foreign_key", new ForeignKeyDefinition { ReferencedTable = foreignKeyTable, ReferencingColumns = new List<string>() });

            const int refColCount = 5;
            for (int i = 0; i < refColCount; i++)
            {
                string refColName = $"ref_col{i}";
                table.Columns.Add(refColName, new ColumnDefinition
                {
                    SystemType = typeof(long)
                });
                table.ForeignKeys["foreign_key"].ReferencingColumns.Add(refColName);
            }

            Dictionary<string, Relationship> relationships =
                new()
                {
                    {
                        foreignKeyTable,
                        new Relationship(
                          Cardinality.One,
                          foreignKeyTable,
                          SourceFields: null,
                          TargetFields: null,
                          LinkingObject: null,
                          LinkingSourceFields: null,
                          LinkingTargetFields: null)
                    }
                };
            Entity configEntity = GenerateEmptyEntity() with { Relationships = relationships };
            Entity relationshipEntity = GenerateEmptyEntity();

            ObjectTypeDefinitionNode od = SchemaConverter.FromTableDefinition("table", table, configEntity, new() { { foreignKeyTable, relationshipEntity } });

            Assert.AreEqual(1, od.Fields.Count(f => f.Type.NamedType().Name.Value == foreignKeyTable));
        }

        [TestMethod]
        public void CardinalityOfOneWillBeSingleObjectRelationship()
        {
            TableDefinition table = new();

            string columnName = "columnName";
            table.Columns.Add(columnName, new ColumnDefinition
            {
                SystemType = typeof(string),
                IsNullable = false,
            });
            const string foreignKeyTable = "FkTable";
            const string refColName = "ref_col";
            table.ForeignKeys.Add("foreign_key", new ForeignKeyDefinition { ReferencedTable = foreignKeyTable, ReferencingColumns = new List<string> { refColName } });
            table.Columns.Add(refColName, new ColumnDefinition
            {
                SystemType = typeof(long)
            });

            Dictionary<string, Relationship> relationships =
                new()
                {
                    {
                        foreignKeyTable,
                        new Relationship(
                          Cardinality.One,
                          foreignKeyTable,
                          SourceFields: null,
                          TargetFields: null,
                          LinkingObject: null,
                          LinkingSourceFields: null,
                          LinkingTargetFields: null)
                    }
                };
            Entity configEntity = GenerateEmptyEntity() with { Relationships = relationships };
            Entity relationshipEntity = GenerateEmptyEntity();

            ObjectTypeDefinitionNode od = SchemaConverter.FromTableDefinition("table", table, configEntity, new() { { foreignKeyTable, relationshipEntity } });

            FieldDefinitionNode field = od.Fields.First(f => f.Type.NamedType().Name.Value == foreignKeyTable);
            Assert.IsFalse(field.Type.IsListType());
        }

        [TestMethod]
        public void CardinalityOfManyWillBeListRelationship()
        {
            TableDefinition table = new();

            string columnName = "columnName";
            table.Columns.Add(columnName, new ColumnDefinition
            {
                SystemType = typeof(string),
                IsNullable = false,
            });
            const string foreignKeyTable = "FkTable";
            const string refColName = "ref_col";
            table.ForeignKeys.Add("foreign_key", new ForeignKeyDefinition { ReferencedTable = foreignKeyTable, ReferencingColumns = new List<string> { refColName } });
            table.Columns.Add(refColName, new ColumnDefinition
            {
                SystemType = typeof(long)
            });

            Dictionary<string, Relationship> relationships =
                new()
                {
                    {
                        foreignKeyTable,
                        new Relationship(
                          Cardinality.Many,
                          foreignKeyTable,
                          SourceFields: null,
                          TargetFields: null,
                          LinkingObject: null,
                          LinkingSourceFields: null,
                          LinkingTargetFields: null)
                    }
                };
            Entity configEntity = GenerateEmptyEntity() with { Relationships = relationships };
            Entity relationshipEntity = GenerateEmptyEntity();

            ObjectTypeDefinitionNode od = SchemaConverter.FromTableDefinition("table", table, configEntity, new() { { foreignKeyTable, relationshipEntity } });

            FieldDefinitionNode field = od.Fields.First(f => f.Type.NamedType().Name.Value == foreignKeyTable);
            Assert.IsTrue(field.Type.InnerType().IsListType());
        }

        [TestMethod]
        public void WhenForeignKeyDefinedButNoRelationship_GraphQLWontModelIt()
        {
            TableDefinition table = new();

            string columnName = "columnName";
            table.Columns.Add(columnName, new ColumnDefinition
            {
                SystemType = typeof(string),
                IsNullable = false,
            });
            const string foreignKeyTable = "FkTable";
            const string refColName = "ref_col";
            table.ForeignKeys.Add("foreign_key", new ForeignKeyDefinition { ReferencedTable = foreignKeyTable, ReferencingColumns = new List<string> { refColName } });
            table.Columns.Add(refColName, new ColumnDefinition
            {
                SystemType = typeof(long)
            });

            Entity configEntity = GenerateEmptyEntity() with { Relationships = new() };
            Entity relationshipEntity = GenerateEmptyEntity();

            ObjectTypeDefinitionNode od = SchemaConverter.FromTableDefinition("table", table, configEntity, new() { { foreignKeyTable, relationshipEntity } });

            Assert.AreEqual(2, od.Fields.Count);
        }

        [DataTestMethod]
        [DataRow("entityName", "overrideName", "OverrideName")]
        [DataRow("entityName", null, "EntityName")]
        [DataRow("entityName", "", "EntityName")]
        public void SingularNamingRulesDeterminedByRuntimeConfig(string entityName, string singular, string expected)
        {
            TableDefinition table = new();

            Entity configEntity = GenerateEmptyEntity() with { GraphQL = new SingularPlural(singular, null) };
            ObjectTypeDefinitionNode od = SchemaConverter.FromTableDefinition(entityName, table, configEntity, new());

            Assert.AreEqual(expected, od.Name.Value);
        }

        [DataTestMethod]
        [DataRow("singularName", "pluralNameOverride", "FkTable", "pluralNameOverride")]
        [DataRow("singularName", "", "FkTable", "singularNames")]
        [DataRow("singularName", null, "FkTable", "singularNames")]
        [DataRow(null, null, "FkTable", "fkTables")]
        public void NamingRulesAppliedOnRelationshipField(string singular, string plural, string foreignKeyTable, string expected)
        {
            TableDefinition table = new();

            string columnName = "columnName";
            table.Columns.Add(columnName, new ColumnDefinition
            {
                SystemType = typeof(string),
                IsNullable = false,
            });
            const string refColName = "ref_col";
            table.ForeignKeys.Add("foreign_key", new ForeignKeyDefinition { ReferencedTable = foreignKeyTable, ReferencingColumns = new List<string> { refColName } });
            table.Columns.Add(refColName, new ColumnDefinition
            {
                SystemType = typeof(long)
            });

            Dictionary<string, Relationship> relationships =
                new()
                {
                    {
                        foreignKeyTable,
                        new Relationship(
                          Cardinality.Many,
                          foreignKeyTable,
                          SourceFields: null,
                          TargetFields: null,
                          LinkingObject: null,
                          LinkingSourceFields: null,
                          LinkingTargetFields: null)
                    }
                };
            Entity configEntity = GenerateEmptyEntity() with { Relationships = relationships };
            Entity relationshipEntity = GenerateEmptyEntity() with { GraphQL = new SingularPlural(singular, plural) };

            ObjectTypeDefinitionNode od = SchemaConverter.FromTableDefinition("table", table, configEntity, new() { { foreignKeyTable, relationshipEntity } });

            FieldDefinitionNode field = od.Fields.First(f => f.Name.Value != columnName && f.Name.Value != refColName);
            Assert.AreEqual(expected, field.Name.Value);
        }
    }
}
