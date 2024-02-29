// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Service.Tests.SqlTests;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Unittests
{
    [TestClass]
    public abstract class NestedCreateOrderHelperUnitTests
    {
        protected static string DatabaseEngine;

        [TestMethod, TestCategory(TestCategory.MSSQL)]
        public async Task ValidateInferredRelationshipInfoForMsSql()
        {
            DatabaseEngine = TestCategory.MSSQL;
            await InferMetadata();
            ValidateInferredRelationshipInfoForTables();
        }

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
        private static void ValidateInferredRelationshipInfoForTables()
        {
            // Validate that when custom source.fields/target.fields are defined in the config for a relationship of cardinality *:1
            // between Book - Stock but no FK constraint exists between them, we ÇANNOT successfully determine at the startup,
            // which entity is the referencing entity and hence keep ourselves open to the possibility of either entity acting
            // as the referencing entity. The actual referencing entity is determined during request execution.
            ValidateReferencingEntitiesForRelationship("Book", "Stock", new List<string>() { "Book", "Stock" });

            // Validate that when custom source.fields/target.fields defined in the config for a relationship of cardinality N:1
            // between Review - Book is the same as the FK constraint from Review -> Book,
            // we successfully determine at the startup, that Review is the referencing entity.
            ValidateReferencingEntitiesForRelationship("Review", "Book", new List<string>() { "Review" });

            // Validate that when custom source.fields/target.fields defined in the config for a relationship of cardinality 1:N
            // between Book - Review is the same as the FK constraint from Review -> Book,
            // we successfully determine at the startup, that Review is the referencing entity.
            ValidateReferencingEntitiesForRelationship("Book", "Review", new List<string>() { "Review" });

            // Validate that when custom source.fields/target.fields defined in the config for a relationship of cardinality 1:1
            // between Stock - stocks_price is the same as the FK constraint from stocks_price -> Stock,
            // we successfully determine at the startup, that stocks_price is the referencing entity.
            ValidateReferencingEntitiesForRelationship("Stock", "stocks_price", new List<string>() { "stocks_price" });

            // Validate that when no custom source.fields/target.fields are defined in the config for a relationship of cardinality N:1
            // between Book - Publisher and an FK constraint exists from Book->Publisher, we successfully determine at the startup,
            // that Book is the referencing entity.
            ValidateReferencingEntitiesForRelationship("Book", "Publisher", new List<string>() { "Book" });

            // Validate that when no custom source.fields/target.fields are defined in the config for a relationship of cardinality 1:N
            // between Publisher - Book and an FK constraint exists from Book->Publisher, we successfully determine at the startup,
            // that Book is the referencing entity.
            ValidateReferencingEntitiesForRelationship("Publisher", "Book", new List<string>() { "Book" });

            // Validate that when no custom source.fields/target.fields are defined in the config for a relationship of cardinality 1:1
            // between Book - BookWebsitePlacement and an FK constraint exists from BookWebsitePlacement->Book,
            // we successfully determine at the startup, that BookWebsitePlacement is the referencing entity.
            ValidateReferencingEntitiesForRelationship("Book", "BookWebsitePlacement", new List<string>() { "BookWebsitePlacement" });
        }

        private static void ValidateReferencingEntitiesForRelationship(
            string sourceEntityName,
            string targetEntityName,
            List<string> referencingEntityNames)
        {
            _sqlMetadataProvider.GetEntityNamesAndDbObjects().TryGetValue(sourceEntityName, out DatabaseObject sourceDbo);
            _sqlMetadataProvider.GetEntityNamesAndDbObjects().TryGetValue(targetEntityName, out DatabaseObject targetDbo);
            DatabaseTable sourceTable = (DatabaseTable)sourceDbo;
            DatabaseTable targetTable = (DatabaseTable)targetDbo;
            List<ForeignKeyDefinition> foreignKeys = sourceDbo.SourceDefinition.SourceEntityRelationshipMap[sourceEntityName].TargetEntityToFkDefinitionMap[targetEntityName];
            HashSet<DatabaseTable> expectedReferencingTables = new();
            HashSet<DatabaseTable> actualReferencingTables = new();
            foreach (string referencingEntityName in referencingEntityNames)
            {
                DatabaseTable referencingTable = referencingEntityName.Equals(sourceEntityName) ? sourceTable : targetTable;
                expectedReferencingTables.Add(referencingTable);
            }

            foreach (ForeignKeyDefinition foreignKey in foreignKeys)
            {
                if (foreignKey.ReferencedColumns.Count == 0)
                {
                    continue;
                }

                DatabaseTable actualReferencingTable = foreignKey.Pair.ReferencingDbTable;
                actualReferencingTables.Add(actualReferencingTable);
            }

            Assert.IsTrue(actualReferencingTables.SetEquals(expectedReferencingTables));
        }

        protected static async Task InferMetadata()
        {
            TestHelper.SetupDatabaseEnvironment(DatabaseEngine);
            RuntimeConfig runtimeConfig = SqlTestHelper.SetupRuntimeConfig();
            SqlTestHelper.RemoveAllRelationshipBetweenEntities(runtimeConfig);
            RuntimeConfigProvider runtimeConfigProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(runtimeConfig);
            SetUpSQLMetadataProvider(runtimeConfigProvider);
            await _sqlMetadataProvider.InitializeAsync();
        }
    }
}
