// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Service.Exceptions;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass]
    public class MultipleCreateOrderHelperEdgeTests
    {
        [TestMethod]
        public void GetReferencingEntityName_RejectsMissingMetadata()
        {
            Mock<ISqlMetadataProvider> metadata = CreateMetadata(new Dictionary<string, DatabaseObject>());

            Assert.ThrowsException<DataApiBuilderException>(() => MultipleCreateOrderHelper.GetReferencingEntityName(
                new Mock<IMiddlewareContext>().Object,
                "relationship",
                "Source",
                "Target",
                metadata.Object,
                new Dictionary<string, IValueNode?>(),
                null,
                2));
        }

        [TestMethod]
        public void GetReferencingEntityName_RejectsNonTableEntities()
        {
            Dictionary<string, DatabaseObject> objects = new()
            {
                ["Source"] = new DatabaseView("dbo", "source"),
                ["Target"] = new DatabaseTable("dbo", "target")
            };
            Mock<ISqlMetadataProvider> metadata = CreateMetadata(objects);

            Assert.ThrowsException<DataApiBuilderException>(() => MultipleCreateOrderHelper.GetReferencingEntityName(
                new Mock<IMiddlewareContext>().Object,
                "relationship",
                "Source",
                "Target",
                metadata.Object,
                new Dictionary<string, IValueNode?>(),
                null,
                1));
        }

        [TestMethod]
        public void GetReferencingEntityName_RejectsEntitiesBackedBySameTable()
        {
            Dictionary<string, DatabaseObject> objects = new()
            {
                ["Source"] = new DatabaseTable("dbo", "shared"),
                ["Target"] = new DatabaseTable("dbo", "shared")
            };
            Mock<ISqlMetadataProvider> metadata = CreateMetadata(objects);

            Assert.ThrowsException<DataApiBuilderException>(() => MultipleCreateOrderHelper.GetReferencingEntityName(
                new Mock<IMiddlewareContext>().Object,
                "relationship",
                "Source",
                "Target",
                metadata.Object,
                new Dictionary<string, IValueNode?>(),
                null,
                1));
        }

        /// <summary>
        /// Verifies many-to-many relationships return no direct referencing entity because insertion is routed through the linking table.
        /// </summary>
        [TestMethod]
        public void GetReferencingEntityName_ManyToManyReturnsEmptyForLinkingTableHandling()
        {
            Dictionary<string, DatabaseObject> objects = new()
            {
                ["Source"] = new DatabaseTable("dbo", "source"),
                ["Target"] = new DatabaseTable("dbo", "target")
            };
            Mock<ISqlMetadataProvider> metadata = CreateMetadata(objects);

            string result = MultipleCreateOrderHelper.GetReferencingEntityName(
                new Mock<IMiddlewareContext>().Object,
                "relationship",
                "Source",
                "Target",
                metadata.Object,
                new Dictionary<string, IValueNode?>(),
                null,
                1,
                isMNRelationship: true);

            Assert.AreEqual(string.Empty, result);
        }

        [TestMethod]
        public void GetBackingColumnDataFromFields_ReturnsOnlyMappedScalarFields()
        {
            Mock<ISqlMetadataProvider> metadata = new();
            metadata.Setup(x => x.TryGetArrayElementSyntaxKind("Book", It.IsAny<string>(), out It.Ref<SyntaxKind>.IsAny))
                .Returns(false);
            metadata.Setup(x => x.TryGetBackingColumn("Book", "title", out It.Ref<string?>.IsAny))
                .Returns((string _, string _, out string? backing) =>
                {
                    backing = "book_title";
                    return true;
                });
            List<ObjectFieldNode> fields = new()
            {
                new("title", "DAB"),
                new("unmapped", 7),
                new("relationship", new ObjectValueNode(new ObjectFieldNode("id", 1)))
            };
            Dictionary<string, IValueNode?> result = MultipleCreateOrderHelper.GetBackingColumnDataFromFields(
                new Mock<IMiddlewareContext>().Object,
                "Book",
                fields,
                metadata.Object);

            Assert.AreEqual(1, result.Count);
            Assert.IsTrue(result.ContainsKey("book_title"));
        }

        [TestMethod]
        public void GetBackingColumnDataFromFields_ArrayElementKindCanBeScalar()
        {
            Mock<ISqlMetadataProvider> metadata = new();
            SyntaxKind scalarKind = SyntaxKind.FloatValue;
            metadata.Setup(x => x.TryGetArrayElementSyntaxKind("Book", "vector", out scalarKind)).Returns(true);
            string? backing = "embedding";
            metadata.Setup(x => x.TryGetBackingColumn("Book", "vector", out backing)).Returns(true);

            Dictionary<string, IValueNode?> result = MultipleCreateOrderHelper.GetBackingColumnDataFromFields(
                new Mock<IMiddlewareContext>().Object,
                "Book",
                new[] { new ObjectFieldNode("vector", new ListValueNode(new FloatValueNode(1.5))) },
                metadata.Object);

            Assert.IsTrue(result.ContainsKey("embedding"));
        }

        private static Mock<ISqlMetadataProvider> CreateMetadata(IReadOnlyDictionary<string, DatabaseObject> objects)
        {
            Mock<ISqlMetadataProvider> metadata = new();
            metadata.Setup(x => x.GetEntityNamesAndDbObjects()).Returns(objects);
            return metadata;
        }
    }
}
