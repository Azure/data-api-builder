// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Reflection;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
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

            Assert.AreEqual(new TimeOnly(12, 34, 56), InvokeParse("12:34:56", typeof(TimeSpan)));

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

        [DataTestMethod]
        [DataRow(true)]
        [DataRow(false)]
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
                structure.AddJoinPredicatesForRelatedEntity("Author", "table1", structure));
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

        [DataTestMethod]
        [DataRow(EntitySourceType.StoredProcedure, true, "stored procedure parameter")]
        [DataRow(EntitySourceType.Table, true, "column")]
        [DataRow(EntitySourceType.Table, false, "publicId")]
        [DataRow(EntitySourceType.StoredProcedure, false, "id")]
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
