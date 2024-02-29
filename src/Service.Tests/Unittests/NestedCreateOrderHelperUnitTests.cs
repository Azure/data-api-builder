// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Service.Tests.SqlTests;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Azure.DataApiBuilder.Core.Resolvers;
using HotChocolate.Resolvers;
using Moq;
using HotChocolate.Execution;

namespace Azure.DataApiBuilder.Service.Tests.Unittests
{
    [TestClass]
    public abstract class NestedCreateOrderHelperUnitTests : SqlTestBase
    {
        /// <summary>
        /// Test to validate that when an FK constraint exists between the entities, we can determine which of the entity
        /// acts as the referencing entity based on the database metadata that we collect at the startup.
        /// The entity which holds the foreign key reference acts as the referencing entity.
        /// </summary>
        [TestMethod]
        public void InferReferencingEntityBasedOnEntityMetadata()
        {
            // Validate that for a relationship of cardinality N:1 between Review - Book where FK constraint
            // exists from Review -> Book, irrespective of which entity is the present at higher level in nested create operation,
            // we successfully determine at the startup, that Review is the referencing entity.

            // Review is the higher level entity.
            ValidateReferencingEntityForRelationship(
                higherLevelEntityName: "Review",
                lowerLevelEntityName: "Book",
                expectedreferencingEntityName: "Review" );

            // Book is the higher level entity.
            ValidateReferencingEntityForRelationship(
                higherLevelEntityName: "Book",
                lowerLevelEntityName: "Review",
                expectedreferencingEntityName: "Review");

            // Validate that for a relationship of cardinality 1:N between Book - Publisher where FK constraint
            // exists from Book -> Publisher,irrespective of which entity is the present at higher level in nested create operation,
            // we successfully determine at the startup, that Book is the referencing entity.

            // Book is the higher level entity.
            ValidateReferencingEntityForRelationship(
                higherLevelEntityName: "Book",
                lowerLevelEntityName: "Publisher",
                expectedreferencingEntityName: "Book");

            // Publisher is the higher level entity.
            ValidateReferencingEntityForRelationship(
                higherLevelEntityName: "Publisher",
                lowerLevelEntityName: "Book",
                expectedreferencingEntityName: "Book");

            // Validate that for a relationship of cardinality 1:1 between Stock - stocks_price where FK constraint
            // exists from stocks_price -> Stock, we successfully determine at the startup, that stocks_price is the
            // referencing entity.
            // Stock is the higher level entity.
            ValidateReferencingEntityForRelationship(
                higherLevelEntityName: "Stock",
                lowerLevelEntityName: "stocks_price",
                expectedreferencingEntityName: "stocks_price");
        }

        private static void ValidateReferencingEntityForRelationship(
            string higherLevelEntityName,
            string lowerLevelEntityName,
            string expectedreferencingEntityName)
        {
            // Setup mock IMiddlewareContext.
            Mock<IMiddlewareContext> context = new();
            Mock<IVariableValueCollection> variables = new();
            context.Setup(x => x.Variables).Returns(variables.Object);

            // Get the referencing entity.
            string actualReferencingEntityName = NestedCreateOrderHelper.GetReferencingEntityName(
                context: context.Object,
                sourceEntityName: higherLevelEntityName,
                targetEntityName: lowerLevelEntityName,
                metadataProvider: _sqlMetadataProvider,
                columnDataInSourceBody: new(),
                targetNodeValue: null);
            Assert.AreEqual(expectedreferencingEntityName, actualReferencingEntityName);
        }
    }
}
