// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Authorization.GraphQL.Policies.Mutation.Delete
{
    /// <summary>
    /// Tests Database Authorization Policies applied to GraphQL Queries
    /// </summary>
    [TestClass, TestCategory(TestCategory.POSTGRESQL)]
    public class PostgresGraphQLDeleteMutationPolicyTests : GraphQLDeleteMutationDatabasePolicyTestBase
    {
        /// <summary>
        /// Set the database engine for the tests
        /// </summary>
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.POSTGRESQL;
            await InitializeTestFixture();
        }

        /// <summary>
        /// Tests Authenticated GraphQL Delete Mutation which triggers
        /// policy processing. Tests deleteBook with policy that
        /// allows/prevents operation.
        /// - Operation allowed: confirm record deleted.
        /// - Operation forbidden: confirm record not deleted.
        /// </summary>
        [TestMethod]
        public async Task DeleteMutation_Policy()
        {
            string dbQuery = @"
                SELECT to_jsonb(""subq"") AS ""data"" 
                FROM(
                    SELECT
                    ""table0"".""id"" AS ""id"",
                    ""table0"".""title"" AS ""title""
                    FROM ""public"".""books"" AS ""table0""
                    WHERE(""title"" = 'Policy-Test-01') AND ""table0"".""id"" = 9
                    ORDER BY ""table0"".""id"" ASC LIMIT 1)
                AS ""subq""
            ";

            await DeleteMutation_Policy(dbQuery);
        }
    }
}
