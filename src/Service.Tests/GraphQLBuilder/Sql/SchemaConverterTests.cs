// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.GraphQLBuilder;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Directives;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Queries;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Sql;
using HotChocolate.Language;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes.SupportedHotChocolateTypes;

namespace Azure.DataApiBuilder.Service.Tests.GraphQLBuilder.Sql
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
        [DataRow("test", "test")]
        [DataRow("Test", "Test")]
        [DataRow("T_est", "T_est")]
        [DataRow("Test1", "Test1")]
        public void EntityNameBecomesObjectName(string entityName, string expected)
        {
            DatabaseObject dbObject = new DatabaseTable { TableDefinition = new SourceDefinition() };

            ObjectTypeDefinitionNode od = SchemaConverter.GenerateObjectTypeDefinitionForDatabaseObject(
                entityName,
                dbObject,
                GenerateEmptyEntity(entityName),
                new(new Dictionary<string, Entity>()),
                rolesAllowedForEntity: GetRolesAllowedForEntity(),
                rolesAllowedForFields: GetFieldToRolesMap()
                );

            Assert.AreEqual(expected, od.Name.Value);
        }

        /// <summary>
        /// Validates that schema generation does not modify names.
        /// </summary>
        /// <param name="columnName"></param>
        /// <param name="expected"></param>
        [DataTestMethod]
        [DataRow("test", "test")]
        [DataRow("Test", "Test")]
        public void ColumnNameBecomesFieldName(string columnName, string expected)
        {
            SourceDefinition table = new();
            table.Columns.Add(columnName, new ColumnDefinition
            {
                SystemType = typeof(string)
            });

            DatabaseObject dbObject = new DatabaseTable() { TableDefinition = table };

            ObjectTypeDefinitionNode od = SchemaConverter.GenerateObjectTypeDefinitionForDatabaseObject(
                "table",
                dbObject,
                GenerateEmptyEntity("table"),
                new(new Dictionary<string, Entity>()),
                rolesAllowedForEntity: GetRolesAllowedForEntity(),
                rolesAllowedForFields: GetFieldToRolesMap(columnName: table.Columns.First().Key)
                );

            Assert.AreEqual(expected, od.Fields[0].Name.Value);
        }

        /// <summary>
        /// Tests that an Entity object's mapping configuration is utilized in the schema generator
        /// by checking that mapped column values are used for field names instead of backing column names.
        /// </summary>
        /// <param name="setMappings">Whether to add mapping entries to the mappings collection.</param>
        /// <param name="backingColumnName">Name of database column.</param>
        /// <param name="mappedName">Configured alternative (mapped) name of column to be used in REST/GraphQL endpoints.</param>
        /// <param name="expectMappedName">Whether GraphQL object field name should equal the mapped column name provided.</param>
        [DataTestMethod]
        [DataRow(true, "__typename", "typename", true, DisplayName = "Mapped column name fixes GraphQL introspection naming violation. ")]
        [DataRow(false, "typename", "mappedtypename", false, DisplayName = "Mapped column name  ")]
        public void FieldNameMatchesMappedValue(bool setMappings, string backingColumnName, string mappedName, bool expectMappedName)
        {
            Dictionary<string, string> mappings = new();

            if (setMappings)
            {
                mappings.Add(backingColumnName, mappedName);
            }

            SourceDefinition table = new();
            table.Columns.Add(backingColumnName, new ColumnDefinition
            {
                SystemType = typeof(string)
            });

            DatabaseObject dbObject = new DatabaseTable() { TableDefinition = table };

            Entity configEntity = GenerateEmptyEntity("table") with { Mappings = mappings };

            ObjectTypeDefinitionNode od = SchemaConverter.GenerateObjectTypeDefinitionForDatabaseObject(
                "table",
                dbObject,
                configEntity,
                entities: new(new Dictionary<string, Entity>()),
                rolesAllowedForEntity: GetRolesAllowedForEntity(),
                rolesAllowedForFields: GetFieldToRolesMap(columnName: table.Columns.First().Key));

            string errorMessage = "Object field representing database column has an unexpected name value.";
            if (expectMappedName)
            {
                Assert.AreEqual(mappedName, od.Fields[0].Name.Value, message: errorMessage);
            }
            else
            {
                Assert.AreEqual(backingColumnName, od.Fields[0].Name.Value, message: errorMessage);
            }
        }

        [TestMethod]
        public void PrimaryKeyColumnHasAppropriateDirective()
        {
            SourceDefinition table = new();

            string columnName = "columnName";
            table.Columns.Add(columnName, new ColumnDefinition
            {
                SystemType = typeof(string)
            });
            table.PrimaryKey.Add(columnName);

            DatabaseObject dbObject = new DatabaseTable() { TableDefinition = table };

            ObjectTypeDefinitionNode od = SchemaConverter.GenerateObjectTypeDefinitionForDatabaseObject(
                "table",
                dbObject,
                GenerateEmptyEntity("table"),
                new(new Dictionary<string, Entity>()),
                rolesAllowedForEntity: GetRolesAllowedForEntity(),
                rolesAllowedForFields: GetFieldToRolesMap()
                );

            FieldDefinitionNode field = od.Fields.First(f => f.Name.Value == columnName);
            // Authorization directive implicitly created so actual count should be 1 + {expected number of directives}.
            Assert.AreEqual(2, field.Directives.Count);
            Assert.AreEqual(PrimaryKeyDirectiveType.DirectiveName, field.Directives[0].Name.Value);
        }

        [TestMethod]
        public void MultiplePrimaryKeysAllMappedWithDirectives()
        {
            SourceDefinition table = new();

            for (int i = 0; i < 5; i++)
            {
                string columnName = $"col{i}";
                table.Columns.Add(columnName, new ColumnDefinition { SystemType = typeof(string) });
                table.PrimaryKey.Add(columnName);
            }

            DatabaseObject dbObject = new DatabaseTable() { TableDefinition = table };

            ObjectTypeDefinitionNode od = SchemaConverter.GenerateObjectTypeDefinitionForDatabaseObject(
                "table",
                dbObject,
                GenerateEmptyEntity("table"),
                new(new Dictionary<string, Entity>()),
                rolesAllowedForEntity: GetRolesAllowedForEntity(),
                rolesAllowedForFields: GetFieldToRolesMap()
                );

            foreach (FieldDefinitionNode field in od.Fields)
            {
                Assert.AreEqual(1, field.Directives.Count);
                Assert.AreEqual(PrimaryKeyDirectiveType.DirectiveName, field.Directives[0].Name.Value);
            }
        }

        [TestMethod]
        public void MultipleColumnsAllMapped()
        {
            int customColumnCount = 5;

            SourceDefinition table = new();

            for (int i = 0; i < customColumnCount; i++)
            {
                table.Columns.Add($"col{i}", new ColumnDefinition { SystemType = typeof(string) });
            }

            DatabaseObject dbObject = new DatabaseTable() { TableDefinition = table };

            ObjectTypeDefinitionNode od = SchemaConverter.GenerateObjectTypeDefinitionForDatabaseObject(
                "table",
                dbObject,
                GenerateEmptyEntity("table"),
                new(new Dictionary<string, Entity>()),
                rolesAllowedForEntity: GetRolesAllowedForEntity(),
                rolesAllowedForFields: GetFieldToRolesMap(additionalColumns: customColumnCount)
                );

            Assert.AreEqual(table.Columns.Count, od.Fields.Count);
        }

        [DataTestMethod]
        [DataRow(typeof(string), STRING_TYPE)]
        [DataRow(typeof(byte), BYTE_TYPE)]
        [DataRow(typeof(short), SHORT_TYPE)]
        [DataRow(typeof(int), INT_TYPE)]
        [DataRow(typeof(long), LONG_TYPE)]
        [DataRow(typeof(float), SINGLE_TYPE)]
        [DataRow(typeof(double), FLOAT_TYPE)]
        [DataRow(typeof(decimal), DECIMAL_TYPE)]
        [DataRow(typeof(bool), BOOLEAN_TYPE)]
        [DataRow(typeof(DateTime), DATETIME_TYPE)]
        [DataRow(typeof(DateTimeOffset), DATETIME_TYPE)]
        [DataRow(typeof(byte[]), BYTEARRAY_TYPE)]
        [DataRow(typeof(Guid), UUID_TYPE)]
        [DataRow(typeof(TimeOnly), LOCALTIME_TYPE)]
        public void SystemTypeMapsToCorrectGraphQLType(Type systemType, string graphQLType)
        {
            SourceDefinition table = new();

            string columnName = "columnName";
            table.Columns.Add(columnName, new ColumnDefinition
            {
                SystemType = systemType
            });

            DatabaseObject dbObject = new DatabaseTable() { TableDefinition = table };

            ObjectTypeDefinitionNode od = SchemaConverter.GenerateObjectTypeDefinitionForDatabaseObject(
                "table",
                dbObject,
                GenerateEmptyEntity("table"),
                new(new Dictionary<string, Entity>()),
                rolesAllowedForEntity: GetRolesAllowedForEntity(),
                rolesAllowedForFields: GetFieldToRolesMap()
                );

            FieldDefinitionNode field = od.Fields.First(f => f.Name.Value == columnName);
            Assert.AreEqual(graphQLType, field.Type.NamedType().Name.Value);
        }

        [TestMethod]
        public void NullColumnBecomesNullField()
        {
            SourceDefinition table = new();

            string columnName = "columnName";
            table.Columns.Add(columnName, new ColumnDefinition
            {
                SystemType = typeof(string),
                IsNullable = true,
            });

            DatabaseObject dbObject = new DatabaseTable() { TableDefinition = table };

            ObjectTypeDefinitionNode od = SchemaConverter.GenerateObjectTypeDefinitionForDatabaseObject(
                "table",
                dbObject,
                GenerateEmptyEntity("table"),
                new(new Dictionary<string, Entity>()),
                rolesAllowedForEntity: GetRolesAllowedForEntity(),
                rolesAllowedForFields: GetFieldToRolesMap()
                );

            FieldDefinitionNode field = od.Fields.First(f => f.Name.Value == columnName);
            Assert.IsFalse(field.Type.IsNonNullType());
        }

        [TestMethod]
        public void NonNullColumnBecomesNonNullField()
        {
            SourceDefinition table = new();

            string columnName = "columnName";
            table.Columns.Add(columnName, new ColumnDefinition
            {
                SystemType = typeof(string),
                IsNullable = false,
            });

            DatabaseObject dbObject = new DatabaseTable() { TableDefinition = table };

            ObjectTypeDefinitionNode od = SchemaConverter.GenerateObjectTypeDefinitionForDatabaseObject(
                "table",
                dbObject,
                GenerateEmptyEntity("table"),
                new(new Dictionary<string, Entity>()),
                rolesAllowedForEntity: GetRolesAllowedForEntity(),
                rolesAllowedForFields: GetFieldToRolesMap()
                );

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

        [DataRow(true, DisplayName = "Test relationship field is nullable.")]
        [DataRow(false, DisplayName = "Test relationship field is not nullable.")]
        [TestMethod]
        public void ForeignKeyFieldHasCorrectNullability(bool isNullable)
        {
            ObjectTypeDefinitionNode od = GenerateObjectWithRelationship(Cardinality.Many, isNullableRelationship: isNullable);
            FieldDefinitionNode field = od.Fields.First(f => f.Name.Value == FIELD_NAME_FOR_TARGET);
            Assert.AreEqual(expected: isNullable, actual: field.Type is INullableTypeNode);
        }

        [TestMethod]
        public void WhenForeignKeyDefinedButNoRelationship_GraphQLWontModelIt()
        {
            SourceDefinition table = GenerateTableWithForeignKeyDefinition();

            Entity configEntity = GenerateEmptyEntity(SOURCE_ENTITY) with { Relationships = new() };
            Entity relationshipEntity = GenerateEmptyEntity(TARGET_ENTITY);

            DatabaseObject dbObject = new DatabaseTable() { TableDefinition = table };

            ObjectTypeDefinitionNode od =
                SchemaConverter.GenerateObjectTypeDefinitionForDatabaseObject(
                    SOURCE_ENTITY,
                    dbObject,
                    configEntity,
                    new(new Dictionary<string, Entity>() { { TARGET_ENTITY, relationshipEntity } }),
                    rolesAllowedForEntity: GetRolesAllowedForEntity(),
                    rolesAllowedForFields: GetFieldToRolesMap()
                    );

            Assert.AreEqual(2, od.Fields.Count);
        }

        /// <summary>
        /// Tests Object Type definition using config defined entity name is handled properly
        /// by schema converter:
        /// - entityName is not singularized, even if plural.
        /// - uses singular name if present and not null/empty.
        /// - Name is not formatted differently from config -> name is not converted to pascal/camel case.
        /// </summary>
        /// <param name="entityName"></param>
        /// <param name="singular"></param>
        /// <param name="expected"></param>
        [DataTestMethod]
        [DataRow("entityName", "overrideName", "overrideName", DisplayName = "Singular name overrides top-level entity name")]
        [DataRow("my entity", "", "my entity", DisplayName = "Top-level entity name with space is not reformatted.")]
        [DataRow("entityName", null, "entityName", DisplayName = "Null singular name defers to top-level entity name")]
        [DataRow("entityName", "", "entityName", DisplayName = "Empty singular name defers to top-level entity name")]
        [DataRow("entities", null, "entities", DisplayName = "Plural top-level entity name and null singular name not singularized")]
        [DataRow("entities", "", "entities", DisplayName = "Plural top-level entity name and empty singular name not singularized")]
        public void SingularNamingRulesDeterminedByRuntimeConfig(string entityName, string singular, string expected)
        {
            SourceDefinition table = new();

            Entity configEntity = GenerateEmptyEntity(string.IsNullOrEmpty(singular) ? entityName : singular);

            DatabaseObject dbObject = new DatabaseTable() { TableDefinition = table };

            ObjectTypeDefinitionNode od = SchemaConverter.GenerateObjectTypeDefinitionForDatabaseObject(
                entityName,
                dbObject,
                configEntity,
                new(new Dictionary<string, Entity>()),
                rolesAllowedForEntity: GetRolesAllowedForEntity(),
                rolesAllowedForFields: GetFieldToRolesMap()
                );

            Assert.AreEqual(expected, od.Name.Value);
        }

        /// <summary>
        /// When schema ObjectTypeDefinition is created,
        /// its fields contain the @authorize directive
        /// when rolesAllowedForFields() returns a role list
        /// </summary>
        [TestMethod]
        public void AutoGeneratedFieldHasDirectiveIndicatingSuch()
        {
            SourceDefinition table = new();
            string columnName = "columnName";
            table.Columns.Add(columnName, new ColumnDefinition
            {
                SystemType = typeof(string),
                IsNullable = false,
                IsAutoGenerated = true,
                IsReadOnly = true
            });

            DatabaseObject dbObject = new DatabaseTable() { TableDefinition = table };

            Entity configEntity = GenerateEmptyEntity("entity");
            ObjectTypeDefinitionNode od = SchemaConverter.GenerateObjectTypeDefinitionForDatabaseObject(
                "entity",
                dbObject,
                configEntity,
                new(new Dictionary<string, Entity>()),
                rolesAllowedForEntity: GetRolesAllowedForEntity(),
                rolesAllowedForFields: GetFieldToRolesMap()
                );

            Assert.IsTrue(od.Fields[0].Directives.Any(d => d.Name.Value == AutoGeneratedDirectiveType.DirectiveName));
        }

        [DataTestMethod]
        [DataRow((byte)1, BYTE_TYPE, SyntaxKind.IntValue)]
        [DataRow((short)1, SHORT_TYPE, SyntaxKind.IntValue)]
        [DataRow(1, INT_TYPE, SyntaxKind.IntValue)]
        [DataRow(1L, LONG_TYPE, SyntaxKind.IntValue)]
        [DataRow("test", STRING_TYPE, SyntaxKind.StringValue)]
        [DataRow(true, BOOLEAN_TYPE, SyntaxKind.BooleanValue)]
        [DataRow(1.2f, SINGLE_TYPE, SyntaxKind.FloatValue)]
        [DataRow(1.2, FLOAT_TYPE, SyntaxKind.FloatValue)]
        [DataRow(1.2, DECIMAL_TYPE, SyntaxKind.FloatValue)]
        [DataRow("1999-01-08 10:23:54", DATETIME_TYPE, SyntaxKind.StringValue)]
        [DataRow("U3RyaW5neQ==", BYTEARRAY_TYPE, SyntaxKind.StringValue)]
        public void DefaultValueGetsSetOnDirective(object defaultValue, string fieldName, SyntaxKind kind)
        {
            if (fieldName == DECIMAL_TYPE)
            {
                defaultValue = decimal.Parse(defaultValue.ToString());
            }
            else if (fieldName == DATETIME_TYPE)
            {
                defaultValue = DateTime.Parse(defaultValue.ToString());
            }
            else if (fieldName == BYTEARRAY_TYPE)
            {
                defaultValue = Convert.FromBase64String(defaultValue.ToString());
            }

            SourceDefinition table = new();
            string columnName = "columnName";
            table.Columns.Add(columnName, new ColumnDefinition
            {
                SystemType = typeof(string),
                IsNullable = false,
                DefaultValue = defaultValue
            });

            DatabaseObject dbObject = new DatabaseTable() { TableDefinition = table };

            Entity configEntity = GenerateEmptyEntity("entity");
            ObjectTypeDefinitionNode od = SchemaConverter.GenerateObjectTypeDefinitionForDatabaseObject(
                "entity",
                dbObject,
                configEntity,
                new(new Dictionary<string, Entity>()),
                rolesAllowedForEntity: GetRolesAllowedForEntity(),
                rolesAllowedForFields: GetFieldToRolesMap()
                );

            // @authorize directive is implicitly created so the count to compare to is 2
            Assert.AreEqual(2, od.Fields[0].Directives.Count);
            DirectiveNode directive = od.Fields[0].Directives[0];
            ObjectValueNode value = (ObjectValueNode)directive.Arguments[0].Value;
            Assert.AreEqual(fieldName, value.Fields[0].Name.Value);
            Assert.AreEqual(kind, value.Fields[0].Value.Kind);
        }

        /// <summary>
        /// Tests that each field on an ObjectTypeDefinition includes
        /// the expected @authorize directive.
        /// Given a the anonymous role provided by GetFieldToRolesMap()
        ///     id - {anonymous, authenticated, role3, roleN}
        ///     title - {authenticated}
        ///     field3 - {role3, roleN}
        /// Adds directive @authorize(roles=[role1, role2, role3]).
        /// </summary>
        [DataTestMethod]
        [DataRow(new string[] { "authenticated" }, DisplayName = "One non-anonymous system role (authenticated) defined for field, @authorize directive added.")]
        [DataRow(new string[] { "authenticated", "role1" }, DisplayName = "Mixed role types (non-anonymous) roles defined for field, @authorize directive added.")]
        [DataRow(new string[] { "role1", "role2", "role3" }, DisplayName = "Multiple non-system roles defined for field, @authorize directive added.")]
        public void AutoGeneratedFieldHasAuthorizeDirective(string[] rolesForField)
        {
            SourceDefinition table = new();
            string columnName = "columnName";
            table.Columns.Add(columnName, new ColumnDefinition
            {
                SystemType = typeof(string),
                IsNullable = false,
                IsAutoGenerated = true,
            });

            DatabaseObject dbObject = new DatabaseTable() { TableDefinition = table };

            Entity configEntity = GenerateEmptyEntity("entity");
            ObjectTypeDefinitionNode od = SchemaConverter.GenerateObjectTypeDefinitionForDatabaseObject(
                "entity",
                dbObject,
                configEntity,
                new(new Dictionary<string, Entity>()),
                rolesAllowedForEntity: GetRolesAllowedForEntity(),
                rolesAllowedForFields: GetFieldToRolesMap(rolesForField: rolesForField)
                );

            // Ensures all fields added have the appropriate @authorize directive.
            Assert.IsTrue(od.Fields.All(field => field.Directives.Any(d => d.Name.Value == GraphQLUtils.AUTHORIZE_DIRECTIVE)));
        }

        /// <summary>
        /// Tests that each field on an entity's ObjectTypeDefinition does not include
        /// an @authorize directive.
        /// Given a set of roles provided by GetFieldToRolesMap()
        ///     id - {anonymous, authenticated, role3, roleN}
        ///     title - {authenticated}
        ///     field3 - {role3, roleN}
        /// Adds directive @authorize(roles=[role1, role2, role3]).
        /// </summary>
        [DataTestMethod]
        [DataRow(new string[] { "anonymous" }, DisplayName = "Anonymous is only role for field")]
        [DataRow(new string[] { "anonymous", "Role1" }, DisplayName = "Anonymous is 1 of many roles for field")]
        [DataRow(new string[] { "authenticated", "anonymous" }, DisplayName = "Anonymous and authenticated are present and randomly ordered, anonymous wins.")]
        public void FieldWithAnonymousAccessHasNoAuthorizeDirective(string[] rolesForField)
        {
            SourceDefinition table = new();
            string columnName = "columnName";
            table.Columns.Add(columnName, new ColumnDefinition
            {
                SystemType = typeof(string),
                IsNullable = false,
                IsAutoGenerated = true,
            });

            Entity configEntity = GenerateEmptyEntity("entity");
            DatabaseObject dbObject = new DatabaseTable() { TableDefinition = table };

            ObjectTypeDefinitionNode od = SchemaConverter.GenerateObjectTypeDefinitionForDatabaseObject(
                "entity",
                dbObject,
                configEntity,
                new(new Dictionary<string, Entity>()),
                rolesAllowedForEntity: GetRolesAllowedForEntity(),
                rolesAllowedForFields: GetFieldToRolesMap(rolesForField: rolesForField)
                );

            // Ensures no field has the @authorize directive.
            Assert.IsFalse(od.Fields.All(field => field.Directives.Any(d => d.Name.Value == GraphQLUtils.AUTHORIZE_DIRECTIVE)),
                message: "@authorize directive must not be present for field with anonymous access permissions.");
        }

        /// <summary>
        /// Tests that an entity's ObjectTypeDefinition only includes an
        /// @authorize directive when anonymous is not a defined role.
        /// Given a set of roles provided by GetRolesAllowedForEntity()
        ///     {anonymous, authenticated, role3, roleN} -> no @authorize directive as anonymous access required.
        ///     {authenticated} -> @authorize directive included with listed roles.
        ///     {role3, roleN} -> @authorize directive included with listed roles.
        /// </summary>
        [DataTestMethod]
        [DataRow(new string[] { "anonymous" }, false, DisplayName = "Anonymous is only role for field, no authorize directive.")]
        [DataRow(new string[] { "anonymous", "role1" }, false, DisplayName = "Anonymous is 1 of many roles for field, no authorize directive.")]
        [DataRow(new string[] { "authenticated", "anonymous" }, false, DisplayName = "Anonymous and authenticated are present and randomly ordered, anonymous wins.")]
        [DataRow(new string[] { "authenticated" }, true, DisplayName = "Authorize directive present for listed roles.")]
        [DataRow(new string[] { "role1", "role2" }, true, DisplayName = "Authorize directive present for listed roles.")]
        public void EntityObjectTypeDefinition_AuthorizeDirectivePresence(string[] roles, bool authorizeDirectiveExpected)
        {
            SourceDefinition table = new();
            string columnName = "columnName";
            table.Columns.Add(columnName, new ColumnDefinition
            {
                SystemType = typeof(string),
                IsNullable = false,
                IsAutoGenerated = true,
            });

            DatabaseObject dbObject = new DatabaseTable() { TableDefinition = table };

            Entity configEntity = GenerateEmptyEntity("entity");
            ObjectTypeDefinitionNode od = SchemaConverter.GenerateObjectTypeDefinitionForDatabaseObject(
                "entity",
                dbObject,
                configEntity,
                new(new Dictionary<string, Entity>()),
                rolesAllowedForEntity: roles,
                rolesAllowedForFields: GetFieldToRolesMap(rolesForField: roles)
                );

            // Prepares error message, only used if assertion fails.
            string errorMessage = !authorizeDirectiveExpected ? $"@authorize directive must not be present for field with anonymous access permissions."
                : $"@authorize directive must not be present for field with anonymous access permissions.";

            // Ensures no field has the @authorize directive if it is NOT authorizeDirectiveExpected
            Assert.AreEqual(expected: authorizeDirectiveExpected, actual: od.Directives.Any(d => d.Name.Value == GraphQLUtils.AUTHORIZE_DIRECTIVE),
                 message: errorMessage);
        }

        /// <summary>
        /// Tests that an entity's ObjectTypeDefinition only includes an
        /// @authorize directive when anonymous is not a defined role.
        /// Tests that an entity's ObjectTypeDefinition's fields only includes an
        /// @authorize directive when anonymous is not a defined role.
        /// </summary>
        /// <param name="rolesForEntity">Roles allowed for entity.</param>
        /// <param name="rolesForFields">Roles allowed for fields.</param>
        /// <param name="authorizeDirectiveExpectedEntity">Directive expected to be present on entity.</param>
        /// <param name="authorizeDirectiveExpectedFields">Directive expected to be present on fields.</param>
        [DataTestMethod]
        [DataRow(new string[] { "anonymous" }, new string[] { "anonymous" }, false, false, DisplayName = "No authorize directive on entity or fields.")]
        [DataRow(new string[] { "anonymous", "role1" }, new string[] { "role1" }, false, true, DisplayName = "No authorize directive on entity, but it is on fields.")]
        [DataRow(new string[] { "authenticated", "anonymous" }, new string[] { "anonymous" }, false, false, DisplayName = "No Authorize directive on entity or fields, mixed.")]
        [DataRow(new string[] { "authenticated" }, new string[] { "role1" }, true, true, DisplayName = "Authorize directive on entity and on fields.")]
        [DataRow(new string[] { "authenticated" }, new string[] { "anonymous" }, true, false, DisplayName = "Authorize Directive on entity, not on fields")]
        public void EntityObjectTypeDefinition_AuthorizeDirectivePresenceMixed(string[] rolesForEntity, string[] rolesForFields, bool authorizeDirectiveExpectedEntity, bool authorizeDirectiveExpectedFields)
        {
            SourceDefinition table = new();
            string columnName = "columnName";
            table.Columns.Add(columnName, new ColumnDefinition
            {
                SystemType = typeof(string),
                IsNullable = false,
                IsAutoGenerated = true,
            });

            Entity configEntity = GenerateEmptyEntity("entity");
            DatabaseObject dbObject = new DatabaseTable() { TableDefinition = table };

            ObjectTypeDefinitionNode od = SchemaConverter.GenerateObjectTypeDefinitionForDatabaseObject(
                "entity",
                dbObject,
                configEntity,
                new(new Dictionary<string, Entity>()),
                rolesAllowedForEntity: rolesForEntity,
                rolesAllowedForFields: GetFieldToRolesMap(rolesForField: rolesForFields)
                );

            // Prepares error message, only used if assertion fails.
            string entityErrorMessage = !authorizeDirectiveExpectedEntity ? $"@authorize directive must not be present on entity with anonymous access permissions."
                : $"@authorize directive must be present on entity with anonymous access permissions.";

            // Ensures no field has the @authorize directive if it is NOT authorizeDirectiveExpected
            Assert.AreEqual(expected: authorizeDirectiveExpectedEntity, actual: od.Directives.Any(d => d.Name.Value == GraphQLUtils.AUTHORIZE_DIRECTIVE),
                 message: entityErrorMessage);

            // Prepares error message, only used if assertion fails.
            string fieldErrorMessage = !authorizeDirectiveExpectedFields ? $"@authorize directive must not be present for field with anonymous access permissions."
                : $"@authorize directive must be present for field with anonymous access permissions.";

            // Ensures no field has the @authorize directive if it is NOT authorizeDirectiveExpected
            Assert.AreEqual(expected: authorizeDirectiveExpectedFields, actual: od.Fields.All(field => field.Directives.Any(d => d.Name.Value == GraphQLUtils.AUTHORIZE_DIRECTIVE)),
                 message: fieldErrorMessage);
        }

        /// <summary>
        /// Mocks a list of roles for the Schema Converter Tests
        /// Defaults to authenticated
        /// </summary>
        /// <returns>Collection of roles</returns>
        public static IEnumerable<string> GetRolesAllowedForEntity()
        {
            return new List<string>() { "authenticated" };
        }

        /// <summary>
        /// Mocks FieldToRoleMap for Schema Converter Tests
        /// For tests that require arbitrary number of columns,
        /// the additionalColumns argument should be used to define
        /// the desired number of columns.
        /// Default is 0 and results in the two constant fields created that are
        /// relevant to most tests in SchemaConverterTests.
        /// </summary>
        /// <param name="additionalColumns">number of columns/fields to generate</param>
        /// <param name="columnName">custom column name</param>
        /// <returns>Key Value Map of Field to Roles</returns>
        public static IDictionary<string, IEnumerable<string>> GetFieldToRolesMap(int additionalColumns = 0, string columnName = "", IEnumerable<string> rolesForField = null)
        {
            Dictionary<string, IEnumerable<string>> fieldToRolesMap = new();

            if (rolesForField is null)
            {
                rolesForField = GetRolesAllowedForEntity();
            }

            if (additionalColumns != 0)
            {
                for (int columnNumber = 0; columnNumber < additionalColumns; columnNumber++)
                {
                    fieldToRolesMap.Add("col" + columnNumber.ToString(), rolesForField);
                }
            }
            else if (!string.IsNullOrEmpty(columnName))
            {
                fieldToRolesMap.Add(columnName, rolesForField);
            }
            else
            {
                fieldToRolesMap.Add(COLUMN_NAME, rolesForField);
                fieldToRolesMap.Add(REF_COLNAME, rolesForField);
            }

            return fieldToRolesMap;
        }

        public static Entity GenerateEmptyEntity(string entityName)
        {
            return new Entity(
                Source: new($"{SCHEMA_NAME}.{TABLE_NAME}", EntitySourceType.Table, null, null),
                Rest: new(Enabled: true),
                GraphQL: new(entityName, ""),
                Permissions: Array.Empty<EntityPermission>(),
                Relationships: new(),
                Mappings: new()
            );
        }

        private static ObjectTypeDefinitionNode GenerateObjectWithRelationship(Cardinality cardinality, bool isNullableRelationship = false)
        {
            SourceDefinition table = GenerateTableWithForeignKeyDefinition(isNullableRelationship);

            Dictionary<string, EntityRelationship> relationships =
                new()
                {
                    {
                        FIELD_NAME_FOR_TARGET,
                        new EntityRelationship(
                          cardinality,
                          TARGET_ENTITY,
                          SourceFields: null,
                          TargetFields: null,
                          LinkingObject: null,
                          LinkingSourceFields: null,
                          LinkingTargetFields: null)
                    }
                };
            Entity configEntity = GenerateEmptyEntity(SOURCE_ENTITY) with { Relationships = relationships };
            Entity relationshipEntity = GenerateEmptyEntity(TARGET_ENTITY);

            DatabaseObject dbObject = new DatabaseTable()
            { SchemaName = SCHEMA_NAME, Name = TABLE_NAME, TableDefinition = table };

            return SchemaConverter.GenerateObjectTypeDefinitionForDatabaseObject(
                SOURCE_ENTITY,
                dbObject,
                configEntity, new(new Dictionary<string, Entity>() { { TARGET_ENTITY, relationshipEntity } }),
                rolesAllowedForEntity: GetRolesAllowedForEntity(),
                rolesAllowedForFields: GetFieldToRolesMap()
                );
        }

        /// <summary>
        /// Generates a table with a foreign key relationship added.
        /// </summary>
        /// <param name="isNullable">whether the foreign key column can be null</param>
        /// <returns></returns>
        private static SourceDefinition GenerateTableWithForeignKeyDefinition(bool isNullable = false)
        {
            SourceDefinition table = new();
            table.Columns.Add(COLUMN_NAME, new ColumnDefinition
            {
                SystemType = typeof(string),
                IsNullable = false,
            });

            RelationshipMetadata
                relationshipMetadata = new();

            table.SourceEntityRelationshipMap.Add(SOURCE_ENTITY, relationshipMetadata);
            List<ForeignKeyDefinition> fkDefinitions = new()
            {
                new ForeignKeyDefinition()
                {
                    Pair = new()
                    {
                        ReferencingDbTable = new DatabaseTable(SCHEMA_NAME, TABLE_NAME),
                        ReferencedDbTable = new DatabaseTable(SCHEMA_NAME, REFERENCED_TABLE)
                    },
                    ReferencingColumns = new List<string> { REF_COLNAME },
                    ReferencedColumns = new List<string> { REFD_COLNAME }
                }
            };
            relationshipMetadata.TargetEntityToFkDefinitionMap.Add(TARGET_ENTITY, fkDefinitions);

            table.Columns.Add(REF_COLNAME, new ColumnDefinition
            {
                SystemType = typeof(int),
                IsNullable = isNullable
            });

            return table;
        }

        /// <summary>
        /// Tests generation of aggregation type for an entity with numeric fields.
        /// Verifies that all numeric operations (max, min, avg, sum, count) are created
        /// with correct return types and arguments.
        /// </summary>
        [TestMethod]
        [TestCategory("Schema Converter - Aggregation Type")]
        public void GenerateAggregationTypeForEntity_WithNumericFields_CreatesAllOperations()
        {
            string gql = @"
type Book @model(name:""Book"") {
    id: ID!
    price: Float!
    rating: Int
    pages: Int!
}";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);
            ObjectTypeDefinitionNode node = root.Definitions[0] as ObjectTypeDefinitionNode;

            ObjectTypeDefinitionNode aggregationType = SchemaConverter.GenerateAggregationTypeForEntity("Book", node);

            Assert.AreEqual("BookAggregations", aggregationType.Name.Value);
            Assert.AreEqual(5, aggregationType.Fields.Count, "Should have max, min, avg, sum, and count operations");

            // Verify all operations exist with correct return types
            Dictionary<string, string> operations = aggregationType.Fields.ToDictionary(f => f.Name.Value, f => f.Type.NamedType().Name.Value);
            Assert.AreEqual("Float", operations["max"]);
            Assert.AreEqual("Float", operations["min"]);
            Assert.AreEqual("Float", operations["avg"]);
            Assert.AreEqual("Float", operations["sum"]);
            Assert.AreEqual("Int", operations["count"]);

            // Verify field arguments and their specific filter input types
            FieldDefinitionNode maxField = aggregationType.Fields.First(f => f.Name.Value == "max");
            Assert.AreEqual(3, maxField.Arguments.Count, "Each operation should have field, having, and distinct arguments");
            Assert.AreEqual("BookNumericAggregateFields", maxField.Arguments[0].Type.NamedType().Name.Value);
            Assert.AreEqual("FloatFilterInput", maxField.Arguments[1].Type.NamedType().Name.Value, "Should use FloatFilterInput for mixed Float/Int fields");
            Assert.AreEqual("Boolean", maxField.Arguments[2].Type.NamedType().Name.Value);
        }

        /// <summary>
        /// Tests generation of aggregation type for an entity with only integer fields.
        /// Verifies that the filter input type is specifically IntFilterInput when all
        /// numeric fields are integers.
        /// </summary>
        [TestMethod]
        [TestCategory("Schema Converter - Aggregation Type")]
        public void GenerateAggregationTypeForEntity_WithSingleNumericType_UsesSpecificFilterInput()
        {
            string gql = @"
type Book @model(name:""Book"") {
    id: ID!
    pages: Int!
    chapter_count: Int
}";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);
            ObjectTypeDefinitionNode node = root.Definitions[0] as ObjectTypeDefinitionNode;

            ObjectTypeDefinitionNode aggregationType = SchemaConverter.GenerateAggregationTypeForEntity("Book", node);

            // Verify that operations use IntFilterInput since all numeric fields are Int
            FieldDefinitionNode maxField = aggregationType.Fields.First(f => f.Name.Value == "max");
            Assert.AreEqual("IntFilterInput", maxField.Arguments[1].Type.NamedType().Name.Value, "Should use IntFilterInput when all numeric fields are Int");
        }

        /// <summary>
        /// Tests generation of aggregation type for an entity with no numeric fields.
        /// Verifies that an empty type is created when there are no fields eligible
        /// for numeric aggregation.
        /// </summary>
        [TestMethod]
        [TestCategory("Schema Converter - Aggregation Type")]
        public void GenerateAggregationTypeForEntity_WithNoNumericFields_CreatesEmptyType()
        {
            string gql = @"
type Book @model(name:""Book"") {
    id: ID!
    title: String!
    isPublished: Boolean
}";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);
            ObjectTypeDefinitionNode node = root.Definitions[0] as ObjectTypeDefinitionNode;

            ObjectTypeDefinitionNode aggregationType = SchemaConverter.GenerateAggregationTypeForEntity("Book", node);

            Assert.AreEqual("BookAggregations", aggregationType.Name.Value);
            Assert.AreEqual(0, aggregationType.Fields.Count, "Should have no aggregation operations");
        }

        /// <summary>
        /// Tests generation of aggregation type for an entity with mixed field types.
        /// Verifies that only numeric fields are included in the aggregation operations
        /// while other types (string, boolean, etc.) are excluded.
        /// </summary>
        [TestMethod]
        [TestCategory("Schema Converter - Aggregation Type")]
        public void GenerateAggregationTypeForEntity_WithMixedFields_OnlyIncludesNumericOperations()
        {
            string gql = @"
type Book @model(name:""Book"") {
    id: ID!
    title: String
    price: Float!
    isPublished: Boolean
    rating: Int
}";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);
            ObjectTypeDefinitionNode node = root.Definitions[0] as ObjectTypeDefinitionNode;

            ObjectTypeDefinitionNode aggregationType = SchemaConverter.GenerateAggregationTypeForEntity("Book", node);

            Assert.AreEqual("BookAggregations", aggregationType.Name.Value);
            Assert.AreEqual(5, aggregationType.Fields.Count, "Should have all operations for numeric fields only");

            // Verify the field argument only includes numeric fields
            InputValueDefinitionNode fieldArg = aggregationType.Fields.First().Arguments[0];
            Assert.AreEqual("BookNumericAggregateFields", fieldArg.Type.NamedType().Name.Value);
        }
    }
}
