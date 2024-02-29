// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Service.Tests.SqlTests;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Azure.DataApiBuilder.Core.Resolvers;
using HotChocolate.Resolvers;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.Unittests
{
    [TestClass]
    public abstract class NestedCreateOrderHelperUnitTests : SqlTestBase
    {
        [TestMethod]
        /// <summary>
        /// Helper method for tests which validate that the relationship data is correctly inferred based on the info provided
        /// in the config and the metadata collected from the database. It runs the test against various test cases verifying that
        /// when a relationship is defined in the config between source and target entity and:
        ///
        /// a) An FK constraint exists in the database between the two entities: We successfully determine which is the referencing
        /// entity based on the FK constraint. If custom source.fields/target.fields are provided, preference is given to those fields.
        ///
        /// b) No FK constraint exists in the database between the two entities: We ÇANNOT determine which entity is the referencing
        /// entity and hence we keep ourselves open to the possibility of either entity acting as the referencing entity.
        /// The actual referencing entity is determined during request execution.
        /// </summary>
        public void InferReferencingEntityBasedOnEntityMetadata()
        {
            // Validate that when custom source.fields/target.fields are defined in the config for a relationship of cardinality *:1
            // between Book - Stock but no FK constraint exists between them, we ÇANNOT successfully determine at the startup,
            // which entity is the referencing entity and hence keep ourselves open to the possibility of either entity acting
            // as the referencing entity. The actual referencing entity is determined during request execution.
            //ValidateReferencingEntitiesForRelationship("Book", "Stock", "Book");

            // Validate that when custom source.fields/target.fields defined in the config for a relationship of cardinality N:1
            // between Review - Book is the same as the FK constraint from Review -> Book,
            // we successfully determine at the startup, that Review is the referencing entity.
            ValidateReferencingEntitiesForRelationship("Review", "Book", "Review" );

            // Validate that when custom source.fields/target.fields defined in the config for a relationship of cardinality 1:N
            // between Book - Review is the same as the FK constraint from Review -> Book,
            // we successfully determine at the startup, that Review is the referencing entity.
            ValidateReferencingEntitiesForRelationship("Book", "Review", "Review" );

            // Validate that when custom source.fields/target.fields defined in the config for a relationship of cardinality 1:1
            // between Stock - stocks_price is the same as the FK constraint from stocks_price -> Stock,
            // we successfully determine at the startup, that stocks_price is the referencing entity.
            ValidateReferencingEntitiesForRelationship("Stock", "stocks_price", "stocks_price" );

            // Validate that when no custom source.fields/target.fields are defined in the config for a relationship of cardinality N:1
            // between Book - Publisher and an FK constraint exists from Book->Publisher, we successfully determine at the startup,
            // that Book is the referencing entity.
            ValidateReferencingEntitiesForRelationship("Book", "Publisher", "Book" );

            // Validate that when no custom source.fields/target.fields are defined in the config for a relationship of cardinality 1:N
            // between Publisher - Book and an FK constraint exists from Book->Publisher, we successfully determine at the startup,
            // that Book is the referencing entity.
            ValidateReferencingEntitiesForRelationship("Publisher", "Book", "Book" );

            // Validate that when no custom source.fields/target.fields are defined in the config for a relationship of cardinality 1:1
            // between Book - BookWebsitePlacement and an FK constraint exists from BookWebsitePlacement->Book,
            // we successfully determine at the startup, that BookWebsitePlacement is the referencing entity.
            //ValidateReferencingEntitiesForRelationship("Book", "BookWebsitePlacement", new List<string>() { "BookWebsitePlacement" });
        }

        private static void ValidateReferencingEntitiesForRelationship(
            string sourceEntityName,
            string targetEntityName,
            string expectedreferencingEntityNames)
        {
            Mock<IMiddlewareContext> context = new();
            string actualReferencingEntityName = NestedCreateOrderHelper.GetReferencingEntityName(
                context.Object,
                sourceEntityName,
                targetEntityName,
                _sqlMetadataProvider,
                new(),
                null);
            Assert.AreEqual(expectedreferencingEntityNames, actualReferencingEntityName);
        }
    }
}
