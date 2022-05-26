using System;
using System.Collections.Generic;
using System.Linq;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.GraphQLBuilder.Directives;
using Azure.DataGateway.Service.GraphQLBuilder.Queries;
using Azure.DataGateway.Service.GraphQLBuilder.Sql;
using HotChocolate.Language;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.GraphQLBuilder.Sql
{
    [TestClass]
    [TestCategory("GraphQL Schema Builder")]
    public class SchemaConverterTests
    {
        const string SCHEMA_NAME = "dbo";
        const string TABLE_NAME = "tableName";
        const string COLUMN_NAME = "columnName";
        const string REF_COLNAME = "ref_col_in_source";
        const string SOURCE_ENTITY = "sourceEntity";
        const string FIELD_NAME_FOR_TARGET = "target";

        const string TARGET_ENTITY = "TargetEntity";
        const string REFERENCED_TABLE = "fkTable";
        const string REFD_COLNAME = "fk_col";

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
            Assert.AreEqual(PrimaryKeyDirectiveType.DirectiveName, field.Directives[0].Name.Value);
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
                Assert.AreEqual(PrimaryKeyDirectiveType.DirectiveName, field.Directives[0].Name.Value);
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
        [DataRow(typeof(int), "Int")]
        [DataRow(typeof(double), "Float")]
        [DataRow(typeof(bool), "Boolean")]
        // TODO: Uncomment these once we have more GraphQL type support - https://github.com/Azure/hawaii-gql/issues/247
        //[DataRow(typeof(int), "Int")]
        //[DataRow(typeof(short), "Int")]
        //[DataRow(typeof(float), "Float")]
        //[DataRow(typeof(decimal), "Float")]
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
            ObjectTypeDefinitionNode od = GenerateObjectWithRelationship(Cardinality.Many);
            Assert.AreEqual(3, od.Fields.Count);
        }

        [TestMethod]
        public void ForeignKeyObjectFieldNameAndTypeMatchesReferenceTable()
        {

            ObjectTypeDefinitionNode od = GenerateObjectWithRelationship(Cardinality.One);
            FieldDefinitionNode field
                = od.Fields.First(f => f.Name.Value != REF_COLNAME && f.Name.Value != COLUMN_NAME);

            Assert.AreEqual(FIELD_NAME_FOR_TARGET, field.Name.Value);
            Assert.AreEqual(TARGET_ENTITY, field.Type.NamedType().Name.Value);
        }

        [TestMethod]
        public void ForeignKeyFieldWillHaveRelationshipDirective()
        {
            ObjectTypeDefinitionNode od = GenerateObjectWithRelationship(Cardinality.One);
            FieldDefinitionNode field = od.Fields.First(f => f.Name.Value == FIELD_NAME_FOR_TARGET);

            Assert.AreEqual(1, field.Directives.Count);
            Assert.AreEqual(RelationshipDirectiveType.DirectiveName, field.Directives[0].Name.Value);
        }

        [TestMethod]
        public void CardinalityOfManyWillBeConnectionRelationship()
        {
            ObjectTypeDefinitionNode od = GenerateObjectWithRelationship(Cardinality.Many);
            FieldDefinitionNode field = od.Fields.First(f => f.Name.Value == FIELD_NAME_FOR_TARGET);
            Assert.IsTrue(QueryBuilder.IsPaginationType(field.Type.NamedType()));
        }

        [TestMethod]
        public void WhenForeignKeyDefinedButNoRelationship_GraphQLWontModelIt()
        {
            TableDefinition table = GenerateTableWithForeignKeyDefinition();

            Entity configEntity = GenerateEmptyEntity() with { Relationships = new() };
            Entity relationshipEntity = GenerateEmptyEntity();

            ObjectTypeDefinitionNode od =
                SchemaConverter.FromTableDefinition(
                    SOURCE_ENTITY, table, configEntity, new() { { TARGET_ENTITY, relationshipEntity } });

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

        [TestMethod]
        public void AutoGeneratedFieldHasDirectiveIndicatingSuch()
        {
            TableDefinition table = new();
            string columnName = "columnName";
            table.Columns.Add(columnName, new ColumnDefinition
            {
                SystemType = typeof(string),
                IsNullable = false,
                IsAutoGenerated = true,
            });

            Entity configEntity = GenerateEmptyEntity();
            ObjectTypeDefinitionNode od = SchemaConverter.FromTableDefinition("entity", table, configEntity, new());

            Assert.IsTrue(od.Fields[0].Directives.Any(d => d.Name.Value == AutoGeneratedDirectiveType.DirectiveName));
        }

        [DataTestMethod]
        [DataRow(1, "int", SyntaxKind.IntValue)]
        [DataRow("test", "string", SyntaxKind.StringValue)]
        [DataRow(true, "boolean", SyntaxKind.BooleanValue)]
        [DataRow(1.2f, "float", SyntaxKind.FloatValue)]
        public void DefaultValueGetsSetOnDirective(object defaultValue, string fieldName, SyntaxKind kind)
        {
            TableDefinition table = new();
            string columnName = "columnName";
            table.Columns.Add(columnName, new ColumnDefinition
            {
                SystemType = typeof(string),
                IsNullable = false,
                DefaultValue = defaultValue
            });

            Entity configEntity = GenerateEmptyEntity();
            ObjectTypeDefinitionNode od = SchemaConverter.FromTableDefinition("entity", table, configEntity, new());

            Assert.AreEqual(1, od.Fields[0].Directives.Count);
            DirectiveNode directive = od.Fields[0].Directives[0];
            ObjectValueNode value = (ObjectValueNode)directive.Arguments[0].Value;
            Assert.AreEqual(fieldName, value.Fields[0].Name.Value);
            Assert.AreEqual(kind, value.Fields[0].Value.Kind);
        }

        private static Entity GenerateEmptyEntity()
        {
            return new Entity("dbo.entity", Rest: null, GraphQL: null, Array.Empty<PermissionSetting>(), Relationships: new(), Mappings: new());
        }

        private static ObjectTypeDefinitionNode GenerateObjectWithRelationship(Cardinality cardinality)
        {
            TableDefinition table = GenerateTableWithForeignKeyDefinition();

            Dictionary<string, Relationship> relationships =
                new()
                {
                    {
                        FIELD_NAME_FOR_TARGET,
                        new Relationship(
                          cardinality,
                          TARGET_ENTITY,
                          SourceFields: null,
                          TargetFields: null,
                          LinkingObject: null,
                          LinkingSourceFields: null,
                          LinkingTargetFields: null)
                    }
                };
            Entity configEntity = GenerateEmptyEntity() with { Relationships = relationships };
            Entity relationshipEntity = GenerateEmptyEntity();

            return SchemaConverter.FromTableDefinition
                    (SOURCE_ENTITY,
                     table,
                     configEntity, new() { { TARGET_ENTITY, relationshipEntity } });
        }

        private static TableDefinition GenerateTableWithForeignKeyDefinition()
        {
            TableDefinition table = new();
            table.Columns.Add(COLUMN_NAME, new ColumnDefinition
            {
                SystemType = typeof(string),
                IsNullable = false,
            });

            RelationshipMetadata
                relationshipMetadata = new();

            table.SourceEntityRelationshipMap.Add(SOURCE_ENTITY, relationshipMetadata);
            List<ForeignKeyDefinition> fkDefinitions = new();
            fkDefinitions.Add(new ForeignKeyDefinition()
            {
                Pair = new()
                {
                    ReferencingDbObject = new(SCHEMA_NAME, TABLE_NAME),
                    ReferencedDbObject = new(SCHEMA_NAME, REFERENCED_TABLE)
                },
                ReferencingColumns = new List<string> { REF_COLNAME },
                ReferencedColumns = new List<string> { REFD_COLNAME }
            });
            relationshipMetadata.TargetEntityToFkDefinitionMap.Add(TARGET_ENTITY, fkDefinitions);

            table.Columns.Add(REF_COLNAME, new ColumnDefinition
            {
                SystemType = typeof(int)
            });

            return table;
        }
    }
}
