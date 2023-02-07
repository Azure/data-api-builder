// **************************************
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
//
// @file: MySqlGraphQLDeleteMutationPolicyTests.cs
// **************************************

using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Authorization.GraphQL.Policies.Mutation.Delete
{
    /// <summary>
    /// Tests Database Authorization Policies applied to GraphQL Queries
    /// </summary>
    [TestClass, TestCategory(TestCategory.MYSQL)]
    public class MySqlGraphQLDeleteMutationPolicyTests : GraphQLDeleteMutationDatabasePolicyTestBase
    {
        /// <summary>
        /// Set the database engine for the tests
        /// </summary>
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.MYSQL;
            await InitializeTestFixture(context);
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
                  SELECT JSON_OBJECT('id', id, 'title', title ) AS data
                    FROM(
                        SELECT id, title
                        FROM `books` AS `table0` 
                        WHERE id = 9 AND title = 'Policy-Test-01'
                    ) as subq
            ";

            await DeleteMutation_Policy(dbQuery);
        }
    }
}
