// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Authorization.GraphQL.Policies.Mutation.Update
{
    [TestClass, TestCategory(TestCategory.POSTGRESQL)]

    public class PostgresGraphQLUpdateMutationPolicyTests : GraphQLUpdateMutationDatabasePolicyTestBase
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
        /// Do: Test Authenticated GraphQL Update Mutation which triggers
        /// policy processing of policy that allows operation.
        /// Check: Record updated.
        /// </summary>
        /// <param name="roleName"></param>
        /// <param name="isAuthenticated"></param>
        /// <returns></returns>
        [TestMethod]
        public async Task UpdateMutation_Success_Policy()
        {
            string dbQuery = @"
                SELECT to_jsonb(""subq"") AS ""data"" 
                FROM(
                    SELECT
                    ""table0"".""id"" AS ""id"",
                    ""table0"".""title"" AS ""title""
                    FROM ""public"".""books"" AS ""table0""
                    WHERE(""title"" = 'UpdatedBookTitle') AND ""table0"".""id"" = 9
                    ORDER BY ""table0"".""id"" ASC LIMIT 1)
                AS ""subq""
            ";

            await UpdateMutation_Success_Policy(dbQuery, roleName: "policy_tester_08", isAuthenticated: true);
        }

        /// <summary>
        /// Do: Test Authenticated GraphQL Update Mutation which triggers
        /// policy processing of policy that prevents operation.
        /// Check: Record not updated.
        /// </summary>
        /// <param name="roleName"></param>
        /// <param name="isAuthenticated"></param>
        /// <param name="expectedErrorMessage"></param>
        /// <param name="resultsExpected"></param>
        /// <returns></returns>
        [TestMethod]
        [DataRow("policy_tester_noupdate", true, "Could not find item with", false, DisplayName = "Update Mutation Prohibited by Policy")]
        [DataRow("policy_tester_update_noread", true, "The current user is not authorized to access this resource", true, DisplayName = "Update Mutation Succeeds, Disallowed Post-Update READ")]
        public async Task UpdateMutation_ErrorMessage_Policy(string roleName, bool isAuthenticated, string expectedErrorMessage, bool mutationShouldComplete)
        {
            string dbQuery = @"
                SELECT to_jsonb(""subq"") AS ""data"" 
                FROM(
                    SELECT
                    ""table0"".""id"" AS ""id"",
                    ""table0"".""journalname"" AS ""journalname""
                    FROM ""public"".""journals"" AS ""table0""
                    WHERE(""journalname"" = 'UpdatedJournalName') AND ""table0"".""id"" = 1
                    ORDER BY ""table0"".""id"" ASC LIMIT 1)
                AS ""subq""
            ";

            await UpdateMutation_ErrorMessage_Policy(dbQuery, roleName, isAuthenticated, expectedErrorMessage, mutationShouldComplete);
        }

        /// <summary>
        /// Do: Test Authenticated GraphQL Update Mutation which triggers
        /// policy processing of policy that prevents operation.
        /// Check: Record not updated.
        /// </summary>
        /// <param name="roleName"></param>
        /// <param name="isAuthenticated"></param>
        /// <param name="expectedErrorMessage"></param>
        /// <param name="resultsExpected"></param>
        /// <returns></returns>
        [TestMethod]
        [DataRow("anonymous", false, null, true, 1, DisplayName = "Anonymous Update Mutation Succeeds, Disallowed Post-Update READ")]
        [DataRow("anonymous", false, null, true, 2, DisplayName = "Anonymous Update Mutation Succeeds, Allowed Post-Update READ")]
        public async Task UpdateMutation_Anonymous_Policy(string roleName, bool isAuthenticated, string expectedErrorMessage, bool mutationShouldComplete, int id)
        {
            string dbQuery = @"
                SELECT to_jsonb(""subq"") AS ""data"" 
                FROM(
                    SELECT
                    ""table0"".""id"" AS ""id"",
                    ""table0"".""notebookname"" AS ""notebookname""
                    FROM ""public"".""notebooks"" AS ""table0""
                    WHERE(""notebookname"" = 'UpdatedNoteBookName') AND ""table0"".""id"" = " + id + @" 
                    ORDER BY ""table0"".""id"" ASC LIMIT 1)
                AS ""subq""
                ";

            await UpdateMutation_Anonymous_Policy(dbQuery, roleName, isAuthenticated, expectedErrorMessage, mutationShouldComplete, id);
        }

        /// <summary>
        /// Runs after every test to reset the database state
        /// </summary>
        [TestCleanup]
        public async Task TestCleanup()
        {
            await ResetDbStateAsync();
        }
    }

}
