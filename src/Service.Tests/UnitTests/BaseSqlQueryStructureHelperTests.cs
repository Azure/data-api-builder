// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Reflection;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Service.Exceptions;
using HotChocolate.Language;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass]
    public class BaseSqlQueryStructureHelperTests
    {
        [DataTestMethod]
        [DataRow("text", typeof(string), "text")]
        [DataRow("255", typeof(byte), (byte)255)]
        [DataRow("-12", typeof(short), (short)-12)]
        [DataRow("123", typeof(int), 123)]
        [DataRow("123456789", typeof(long), 123456789L)]
        [DataRow("true", typeof(bool), true)]
        [DataRow("7d4ee078-a85c-4a95-82b6-4bf6c3f3cfe8", typeof(Guid), "7d4ee078-a85c-4a95-82b6-4bf6c3f3cfe8")]
        public void ParseParamAsSystemType_ParsesSupportedScalarTypes(string value, Type targetType, object expected)
        {
            object result = InvokeParse(value, targetType);

            if (targetType == typeof(Guid))
            {
                Assert.AreEqual(Guid.Parse((string)expected), result);
            }
            else
            {
                Assert.AreEqual(expected, result);
            }
        }

        [TestMethod]
        public void ParseParamAsSystemType_ParsesBinaryDateAndFloatingPointTypes()
        {
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, (byte[])InvokeParse("AQID", typeof(byte[])));
            Assert.AreEqual(1.25f, InvokeParse("1.25", typeof(float)));
            Assert.AreEqual(2.5d, InvokeParse("2.5", typeof(double)));
            Assert.AreEqual(3.75m, InvokeParse("3.75", typeof(decimal)));
            Assert.AreEqual(new TimeOnly(12, 34, 56), InvokeParse("12:34:56", typeof(TimeOnly)));
        }

        [TestMethod]
        public void ParseParamAsSystemType_ParsesDatesAndArrays()
        {
            DateTime dateTime = (DateTime)InvokeParse("2025-01-02T12:00:00+03:00", typeof(DateTime));
            Assert.AreEqual(DateTimeKind.Utc, dateTime.Kind);
            Assert.AreEqual(9, dateTime.Hour);

            DateTimeOffset offset = (DateTimeOffset)InvokeParse("2025-01-02T12:00:00+03:00", typeof(DateTimeOffset));
            Assert.AreEqual(TimeSpan.FromHours(3), offset.Offset);

            Assert.AreEqual(new TimeSpan(12, 34, 56), InvokeParse("12:34:56", typeof(TimeSpan)));

            object[] values = (object[])InvokeParse("[1.5,2.25]", typeof(float[]));
            CollectionAssert.AreEqual(new object[] { 1.5f, 2.25f }, values);
        }

        [TestMethod]
        public void ParseParamAsSystemType_UnsupportedOrMalformedArray_Throws()
        {
            TargetInvocationException unsupported = Assert.ThrowsException<TargetInvocationException>(
                () => InvokeParse("value", typeof(Uri)));
            Assert.IsInstanceOfType<NotSupportedException>(unsupported.InnerException);

            TargetInvocationException malformed = Assert.ThrowsException<TargetInvocationException>(
                () => InvokeParse("not-json", typeof(float[])));
            Assert.IsInstanceOfType<FormatException>(malformed.InnerException);
        }

        [TestMethod]
        public void GetSubArgumentNamesFromGQLMutArguments_ReturnsObjectFieldNames()
        {
            Dictionary<string, object?> parameters = new()
            {
                ["item"] = new List<ObjectFieldNode>
                {
                    new("id", new IntValueNode(1)),
                    new("title", new StringValueNode("book"))
                }
            };

            List<string> names = BaseSqlQueryStructure.GetSubArgumentNamesFromGQLMutArguments("item", parameters);

            CollectionAssert.AreEqual(new[] { "id", "title" }, names);
        }

        /// <summary>
        /// Verifies malformed and missing GraphQL mutation arguments produce their distinct contextual errors.
        /// </summary>
        [DataTestMethod]
        [DataRow(true, DisplayName = "Argument exists with an unexpected shape")]
        [DataRow(false, DisplayName = "Expected argument is missing")]
        public void GetSubArgumentNamesFromGQLMutArguments_InvalidArguments_Throw(bool includeWrongFormat)
        {
            Dictionary<string, object?> parameters = new();
            if (includeWrongFormat)
            {
                parameters["item"] = "unexpected";
            }

            DataApiBuilderException exception = Assert.ThrowsException<DataApiBuilderException>(() =>
                BaseSqlQueryStructure.GetSubArgumentNamesFromGQLMutArguments("item", parameters));
            StringAssert.Contains(exception.Message, includeWrongFormat ? "Unexpected" : "Expected");
        }

        [TestMethod]
        public void GetColumnSystemType_ReturnsKnownTypeAndRejectsUnknownColumn()
        {
            (TestSqlQueryStructure structure, _) = CreateStructure(EntitySourceType.Table, isDevelopment: false);

            Assert.AreEqual(typeof(int), structure.GetColumnSystemType("id"));
            Assert.ThrowsException<DataApiBuilderException>(() => structure.GetColumnSystemType("missing"));
        }

        [TestMethod]
        public void AddJoinPredicatesForRelationship_MissingSelfRelationshipThrows()
        {
            (TestSqlQueryStructure structure, Mock<ISqlMetadataProvider> metadata) = CreateStructure(EntitySourceType.Table, false);
            metadata.SetupGet(x => x.RelationshipToFkDefinition).Returns(new Dictionary<EntityRelationshipKey, ForeignKeyDefinition>());

            Assert.ThrowsException<DataApiBuilderException>(() => structure.AddJoinPredicatesForRelationship(
                new EntityRelationshipKey("Book", "related"), "Book", "table1", structure));
        }

        [TestMethod]
        public void AddJoinPredicatesForRelatedEntity_MissingRelationshipThrows()
        {
            (TestSqlQueryStructure structure, Mock<ISqlMetadataProvider> metadata) = CreateStructure(EntitySourceType.Table, false);
            DatabaseTable relatedTable = new("dbo", "authors") { TableDefinition = new SourceDefinition() };
            metadata.SetupGet(x => x.EntityToDatabaseObject).Returns(new Dictionary<string, DatabaseObject>
            {
                ["Book"] = structure.DatabaseObject,
                ["Author"] = relatedTable
            });
            metadata.Setup(x => x.GetSourceDefinition("Author")).Returns(relatedTable.TableDefinition);

            Assert.ThrowsException<DataApiBuilderException>(() =>
                structure.AddJoinPredicatesForRelatedEntity("Author", "author", "table1", structure));
        }

        /// <summary>
        /// Regression test for #3849: when an entity has two relationships to the same target entity,
        /// only the foreign key of the requested relationship is used for the join.
        /// </summary>
        [DataTestMethod]
        [DataRow("author", "author_id", DisplayName = "First relationship uses only its own source field")]
        [DataRow("editor", "editor_id", DisplayName = "Second relationship uses only its own source field")]
        public void AddJoinPredicatesForRelationship_MultipleRelationshipsToSameTarget_UsesOnlyRequestedRelationship(
            string relationshipName,
            string expectedSourceColumn)
        {
            (TestSqlQueryStructure structure, Mock<ISqlMetadataProvider> metadata) = CreateStructure(EntitySourceType.Table, false);
            DatabaseTable relatedTable = new("dbo", "authors") { TableDefinition = new SourceDefinition() };
            metadata.SetupGet(x => x.EntityToDatabaseObject).Returns(new Dictionary<string, DatabaseObject>
            {
                ["Book"] = structure.DatabaseObject,
                ["Author"] = relatedTable
            });
            metadata.Setup(x => x.GetSourceDefinition("Author")).Returns(relatedTable.TableDefinition);

            ForeignKeyDefinition CreateFk(string name, string sourceColumn) => new()
            {
                SourceEntityName = "Book",
                RelationshipName = name,
                ReferencingEntityRole = RelationshipRole.Source,
                ReferencedEntityRole = RelationshipRole.Target,
                Pair = new(name, (DatabaseTable)structure.DatabaseObject, relatedTable),
                ReferencingColumns = new() { sourceColumn },
                ReferencedColumns = new() { "id" }
            };

            RelationshipMetadata relationshipMetadata = new();
            relationshipMetadata.TargetEntityToFkDefinitionMap["Author"] = new()
            {
                CreateFk("author", "author_id"),
                CreateFk("editor", "editor_id")
            };
            structure.DatabaseObject.SourceDefinition.SourceEntityRelationshipMap["Book"] = relationshipMetadata;

            structure.AddJoinPredicatesForRelationship(
                new EntityRelationshipKey("Book", relationshipName), "Author", "table1", structure);

            Assert.AreEqual(1, structure.Predicates.Count);
            Assert.AreEqual(expectedSourceColumn, structure.Predicates[0].Left!.AsColumn()!.ColumnName);
            Assert.AreEqual("id", structure.Predicates[0].Right.AsColumn()!.ColumnName);
        }

        /// <summary>
        /// Regression test for #2223: when an entity has two relationships to the same target entity
        /// through different linking objects, only the requested relationship's linking object is joined.
        /// </summary>
        [DataTestMethod]
        [DataRow("author", "book_authors", "author_id", DisplayName = "First relationship joins only its own linking object")]
        [DataRow("editor", "book_editors", "editor_id", DisplayName = "Second relationship joins only its own linking object")]
        public void AddJoinPredicatesForRelationship_MultipleLinkingObjectsToSameTarget_UsesOnlyRequestedRelationship(
            string relationshipName,
            string expectedLinkingTable,
            string expectedLinkingTargetColumn)
        {
            (TestSqlQueryStructure structure, Mock<ISqlMetadataProvider> metadata) = CreateStructure(EntitySourceType.Table, false);
            DatabaseTable relatedTable = new("dbo", "authors") { TableDefinition = new SourceDefinition() };
            metadata.SetupGet(x => x.EntityToDatabaseObject).Returns(new Dictionary<string, DatabaseObject>
            {
                ["Book"] = structure.DatabaseObject,
                ["Author"] = relatedTable
            });
            metadata.Setup(x => x.GetSourceDefinition("Author")).Returns(relatedTable.TableDefinition);

            ForeignKeyDefinition CreateFk(string name, DatabaseTable linkingTable, DatabaseTable referencedTable, string linkingColumn) => new()
            {
                SourceEntityName = "Book",
                RelationshipName = name,
                ReferencingEntityRole = RelationshipRole.Linking,
                ReferencedEntityRole = referencedTable == relatedTable ? RelationshipRole.Target : RelationshipRole.Source,
                Pair = new(name, linkingTable, referencedTable),
                ReferencingColumns = new() { linkingColumn },
                ReferencedColumns = new() { "id" }
            };

            DatabaseTable bookAuthors = new("dbo", "book_authors");
            DatabaseTable bookEditors = new("dbo", "book_editors");
            RelationshipMetadata relationshipMetadata = new();
            relationshipMetadata.TargetEntityToFkDefinitionMap["Author"] = new()
            {
                CreateFk("author", bookAuthors, (DatabaseTable)structure.DatabaseObject, "book_id"),
                CreateFk("author", bookAuthors, relatedTable, "author_id"),
                CreateFk("editor", bookEditors, (DatabaseTable)structure.DatabaseObject, "book_id"),
                CreateFk("editor", bookEditors, relatedTable, "editor_id")
            };
            structure.DatabaseObject.SourceDefinition.SourceEntityRelationshipMap["Book"] = relationshipMetadata;

            structure.AddJoinPredicatesForRelationship(
                new EntityRelationshipKey("Book", relationshipName), "Author", "table1", structure);

            Assert.AreEqual(1, structure.Joins.Count);
            SqlJoinStructure join = structure.Joins[0];
            Assert.AreEqual(expectedLinkingTable, join.DbObject.Name);
            Assert.AreEqual(1, join.Predicates.Count);
            Assert.AreEqual(expectedLinkingTargetColumn, join.Predicates[0].Left!.AsColumn()!.ColumnName);
            Assert.AreEqual("table1", join.Predicates[0].Right.AsColumn()!.TableAlias);

            // The linking object's correlation to the parent entity is kept.
            Assert.AreEqual(1, structure.Predicates.Count);
            Column linkingSourceColumn = structure.Predicates[0].Left!.AsColumn()!;
            Assert.AreEqual("book_id", linkingSourceColumn.ColumnName);
            Assert.AreEqual(join.TableAlias, linkingSourceColumn.TableAlias);
            Assert.AreEqual(structure.SourceAlias, structure.Predicates[0].Right.AsColumn()!.TableAlias);
        }

        [TestMethod]
        public void ProcessOdataClause_NullPolicyStoresNullAndMissingOperationReturnsNull()
        {
            (TestSqlQueryStructure structure, _) = CreateStructure(EntitySourceType.Table, false);

            structure.ProcessOdataClause(null, EntityActionOperation.Read);

            Assert.IsTrue(structure.DbPolicyPredicatesForOperations.ContainsKey(EntityActionOperation.Read));
            Assert.IsNull(structure.GetDbPolicyForOperation(EntityActionOperation.Read));
            Assert.IsNull(structure.GetDbPolicyForOperation(EntityActionOperation.Create));
        }

        /// <summary>
        /// Verifies conversion errors expose source-kind context in development and safe field names in production.
        /// </summary>
        [DataTestMethod]
        [DataRow(EntitySourceType.StoredProcedure, true, "stored procedure parameter", DisplayName = "Development stored-procedure error describes the parameter kind")]
        [DataRow(EntitySourceType.Table, true, "column", DisplayName = "Development table error describes the column kind")]
        [DataRow(EntitySourceType.Table, false, "publicId", DisplayName = "Production table error uses the exposed field name")]
        [DataRow(EntitySourceType.StoredProcedure, false, "id", DisplayName = "Production stored-procedure error uses the parameter name")]
        public void GetParamAsSystemType_InvalidValueUsesSafeContextualMessage(
            EntitySourceType sourceType,
            bool isDevelopment,
            string expectedMessagePart)
        {
            (TestSqlQueryStructure structure, Mock<ISqlMetadataProvider> metadata) = CreateStructure(sourceType, isDevelopment);
            metadata.Setup(x => x.TryGetExposedColumnName("Book", "id", out It.Ref<string?>.IsAny))
                .Returns((string _, string _, out string? name) =>
                {
                    name = "publicId";
                    return true;
                });

            DataApiBuilderException exception = Assert.ThrowsException<DataApiBuilderException>(() =>
                structure.ParseWithContext("not-an-int", "id", typeof(int)));

            StringAssert.Contains(exception.Message, expectedMessagePart);
        }

        private static object InvokeParse(string value, Type targetType)
        {
            MethodInfo method = typeof(BaseSqlQueryStructure).GetMethod(
                "ParseParamAsSystemType",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            return method.Invoke(null, new object[] { value, targetType })!;
        }

        private static (TestSqlQueryStructure Structure, Mock<ISqlMetadataProvider> Metadata) CreateStructure(
            EntitySourceType sourceType,
            bool isDevelopment)
        {
            SourceDefinition sourceDefinition = new();
            sourceDefinition.Columns["id"] = new ColumnDefinition(typeof(int));
            StoredProcedureDefinition storedProcedureDefinition = new();
            storedProcedureDefinition.Columns["id"] = sourceDefinition.Columns["id"];
            DatabaseObject databaseObject = sourceType is EntitySourceType.StoredProcedure
                ? new DatabaseStoredProcedure("dbo", "books") { StoredProcedureDefinition = storedProcedureDefinition }
                : new DatabaseTable("dbo", "books") { TableDefinition = sourceDefinition };
            databaseObject.SourceType = sourceType;

            Mock<ISqlMetadataProvider> metadata = new();
            metadata.SetupGet(x => x.EntityToDatabaseObject).Returns(new Dictionary<string, DatabaseObject>
            {
                ["Book"] = databaseObject
            });
            metadata.Setup(x => x.IsDevelopmentMode()).Returns(isDevelopment);
            metadata.Setup(x => x.GetSourceDefinition("Book")).Returns(databaseObject.SourceDefinition);

            return (new TestSqlQueryStructure(metadata.Object), metadata);
        }

        private sealed class TestSqlQueryStructure : BaseSqlQueryStructure
        {
            public TestSqlQueryStructure(ISqlMetadataProvider metadataProvider)
                : base(metadataProvider, new Mock<IAuthorizationResolver>().Object, null!, entityName: "Book")
            {
            }

            public object ParseWithContext(string value, string fieldName, Type type) =>
                GetParamAsSystemType(value, fieldName, type);
        }
    }
}
