using System.IO;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Services;
using Azure.DataGateway.Service.Services.MetadataProviders;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests
{
    [TestClass]
    public abstract class SqlMetadataProviderTests : SqlTestBase
    {
        [TestMethod]
        public void TestDerivedDatabaseSchemaIsValid()
        {
            SqlGraphQLFileMetadataProvider expectedMetadataProvider
                = new(_graphQLMetadataProvider);
            string testResolverConfigJson = File.ReadAllText("sql-config-test.json");
            expectedMetadataProvider.GraphQLResolverConfig =
                GraphQLFileMetadataProvider.GetDeserializedConfig(testResolverConfigJson);
            DatabaseSchema expectedSchema =
                expectedMetadataProvider.GraphQLResolverConfig.DatabaseSchema!;

            DatabaseSchema derivedDatabaseSchema = _graphQLMetadataProvider.GetResolvedConfig().DatabaseSchema!;
            foreach ((string tableName, TableDefinition expectedTableDefinition) in expectedSchema.Tables)
            {
                TableDefinition actualTableDefinition;
                Assert.IsTrue(derivedDatabaseSchema.Tables.TryGetValue(tableName, out actualTableDefinition),
                    $"Could not find table definition for table '{tableName}'");

                CollectionAssert.AreEqual(
                    expectedTableDefinition.PrimaryKey,
                    actualTableDefinition.PrimaryKey,
                    $"Did not find the expected primary keys for table {tableName}");

                foreach ((string columnName, ColumnDefinition expectedColumnDefinition) in expectedTableDefinition.Columns)
                {
                    ColumnDefinition actualColumnDefinition;
                    Assert.IsTrue(actualTableDefinition.Columns.TryGetValue(columnName, out actualColumnDefinition),
                        $"Could not find column definition for column '{columnName}' of table '{tableName}'");

                    Assert.AreEqual(expectedColumnDefinition.IsAutoGenerated, actualColumnDefinition.IsAutoGenerated);
                    Assert.AreEqual(expectedColumnDefinition.HasDefault, actualColumnDefinition.HasDefault,
                        $"Expected HasDefault property of column '{columnName}' of table '{tableName}' " +
                        $"does not match actual.");
                    Assert.AreEqual(expectedColumnDefinition.IsNullable, actualColumnDefinition.IsNullable,
                        $"Expected IsNullable property of column '{columnName}' of table '{tableName}' " +
                        $"does not match actual.");
                }
            }
        }
    }
}
