// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Data;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass]
    public class DatabaseObjectUnitTests
    {
        [TestMethod]
        public void SourceDefinition_ReturnsDefinitionForEverySupportedSourceType()
        {
            SourceDefinition tableDefinition = new();
            ViewDefinition viewDefinition = new();
            StoredProcedureDefinition storedProcedureDefinition = new();

            Assert.AreSame(tableDefinition, new DatabaseTable("dbo", "books")
            {
                SourceType = EntitySourceType.Table,
                TableDefinition = tableDefinition
            }.SourceDefinition);
            Assert.AreSame(viewDefinition, new DatabaseView("dbo", "books_view")
            {
                SourceType = EntitySourceType.View,
                ViewDefinition = viewDefinition
            }.SourceDefinition);
            Assert.AreSame(storedProcedureDefinition, new DatabaseStoredProcedure("dbo", "get_books")
            {
                SourceType = EntitySourceType.StoredProcedure,
                StoredProcedureDefinition = storedProcedureDefinition
            }.SourceDefinition);
        }

        [TestMethod]
        public void SourceDefinition_UnsupportedSourceType_Throws()
        {
            DatabaseTable databaseObject = new("dbo", "books")
            {
                SourceType = (EntitySourceType)int.MaxValue,
                TableDefinition = new()
            };

            Exception exception = Assert.ThrowsException<Exception>(() => _ = databaseObject.SourceDefinition);
            StringAssert.Contains(exception.Message, "Unsupported EntitySourceType");
        }

        [TestMethod]
        public void StoredProcedureDefinition_UnknownParameter_ReturnsNull()
        {
            StoredProcedureDefinition definition = new()
            {
                Parameters = new Dictionary<string, ParameterDefinition>
                {
                    ["id"] = new() { DbType = DbType.Int32 }
                }
            };

            Assert.AreEqual(DbType.Int32, definition.GetDbTypeForParam("id"));
            Assert.IsNull(definition.GetDbTypeForParam("missing"));
        }

        [DataTestMethod]
        [DataRow(RelationshipRole.Target, RelationshipRole.None, true)]
        [DataRow(RelationshipRole.None, RelationshipRole.Target, false)]
        public void ForeignKeyDefinition_ResolveTargetColumns_ReturnsRoleColumns(
            RelationshipRole referencingRole,
            RelationshipRole referencedRole,
            bool expectReferencing)
        {
            ForeignKeyDefinition definition = CreateForeignKeyDefinition(referencingRole, referencedRole);

            CollectionAssert.AreEqual(
                expectReferencing ? definition.ReferencingColumns : definition.ReferencedColumns,
                definition.ResolveTargetColumns());
        }

        [TestMethod]
        public void ForeignKeyDefinition_ResolveTargetColumns_WithoutTargetRole_Throws()
        {
            ForeignKeyDefinition definition = CreateForeignKeyDefinition(RelationshipRole.Source, RelationshipRole.Linking);

            StringAssert.Contains(
                Assert.ThrowsException<Exception>(() => definition.ResolveTargetColumns()).Message,
                "Unable to resolve target columns");
        }

        [DataTestMethod]
        [DataRow(RelationshipRole.Source, RelationshipRole.None, true)]
        [DataRow(RelationshipRole.None, RelationshipRole.Source, false)]
        public void ForeignKeyDefinition_ResolveSourceColumns_ReturnsRoleColumns(
            RelationshipRole referencingRole,
            RelationshipRole referencedRole,
            bool expectReferencing)
        {
            ForeignKeyDefinition definition = CreateForeignKeyDefinition(referencingRole, referencedRole);

            CollectionAssert.AreEqual(
                expectReferencing ? definition.ReferencingColumns : definition.ReferencedColumns,
                definition.ResolveSourceColumns());
        }

        [TestMethod]
        public void ForeignKeyDefinition_ResolveSourceColumns_WithoutSourceRole_Throws()
        {
            ForeignKeyDefinition definition = CreateForeignKeyDefinition(RelationshipRole.Target, RelationshipRole.Linking);

            StringAssert.Contains(
                Assert.ThrowsException<Exception>(() => definition.ResolveSourceColumns()).Message,
                "Unable to resolve source columns");
        }

        [TestMethod]
        public void ForeignKeyDefinition_Equality_UsesPairAndOrderedColumns()
        {
            ForeignKeyDefinition first = CreateForeignKeyDefinition(RelationshipRole.Source, RelationshipRole.Target);
            ForeignKeyDefinition equal = CreateForeignKeyDefinition(RelationshipRole.Source, RelationshipRole.Target);
            ForeignKeyDefinition different = CreateForeignKeyDefinition(RelationshipRole.Source, RelationshipRole.Target);
            different.ReferencedColumns = new() { "different" };

            Assert.IsTrue(first.Equals((object)equal));
            Assert.IsTrue(first.Equals(equal));
            Assert.IsFalse(first.Equals((ForeignKeyDefinition?)null));
            Assert.IsFalse(first.Equals(different));
            _ = first.GetHashCode();
            _ = equal.GetHashCode();
        }

        [TestMethod]
        public void RelationshipPair_ConstructorsAndEquality_UseDatabaseObjects()
        {
            DatabaseTable referencing = new("dbo", "books");
            DatabaseTable referenced = new("dbo", "publishers");
            RelationShipPair unnamed = new(referencing, referenced);
            RelationShipPair named = new("book_publisher", referencing, referenced);
            RelationShipPair equal = new("other_name", new DatabaseTable("DBO", "BOOKS"), new DatabaseTable("dbo", "Publishers"));

            Assert.AreEqual(string.Empty, unnamed.RelationshipName);
            Assert.AreEqual("book_publisher", named.RelationshipName);
            Assert.IsTrue(named.Equals((object)equal));
            Assert.IsTrue(named.Equals(equal));
            Assert.IsFalse(named.Equals((RelationShipPair?)null));
            Assert.AreEqual(named.GetHashCode(), equal.GetHashCode());
        }

        private static ForeignKeyDefinition CreateForeignKeyDefinition(
            RelationshipRole referencingRole,
            RelationshipRole referencedRole)
        {
            return new ForeignKeyDefinition
            {
                ReferencingEntityRole = referencingRole,
                ReferencedEntityRole = referencedRole,
                Pair = new RelationShipPair(
                    new DatabaseTable("dbo", "books"),
                    new DatabaseTable("dbo", "publishers")),
                ReferencingColumns = new() { "publisher_id" },
                ReferencedColumns = new() { "id" }
            };
        }
    }
}
