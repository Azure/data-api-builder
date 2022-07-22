using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Tests.SqlTests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.Authorization.GraphQL
{
    /// <summary>
    /// Tests Database Authorization Policies applied to GraphQL Queries
    /// </summary>
    [TestClass]
    public class GraphQLUpdateMutationDatabasePolicyTests : SqlTestBase
    {
        /// <summary>
        /// Set the database engine for the tests
        /// </summary>
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.MSSQL;
            await InitializeTestFixture(context);
        }

        /// <summary>
        /// Tests Authenticated GraphQL Update Mutation which triggers
        /// policy processing. Tests updateBook with policy that
        /// allows/prevents operation.
        /// - Operation allowed: confirm record updated.
        /// - Operation forbidden: confirm record not updated.
        /// </summary>
        [TestMethod]
        public async Task UpdateMutation_Policy()
        {
            string dbQuery = @"
                SELECT TOP 1
                [table0].[id] AS [id],
                [table0].[title] AS [title]
                FROM [dbo].[books] AS [table0] 
                WHERE ([table0].[id] = 9 and [table0].[title] = 'UpdatedBookTitle') 
                ORDER BY [table0].[id] ASC 
                FOR JSON PATH, INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER";
            string expectedErrorMessageSubString = "Could not find entity with";
            string graphQLMutationName = "updateBook";
            string graphQLMutation = @"mutation {
                updateBook(
                    id: 9
                    item: {
                        title: ""UpdatedBookTitle"",
                        publisher_id: 2345
                    }
                )
                {
                    id,
                    title
                }
            }
            ";

            // Update Book Policy: @item.id ne 9
            // Test that the update fails due to restrictive update policy.
            // Confirm that no result matches the update request metadata.
            JsonElement result = await ExecuteGraphQLRequestAsync(
                graphQLMutation,
                graphQLMutationName,
                isAuthenticated: true,
                clientRoleHeader: "policy_tester_07");

            SqlTestHelper.TestForErrorInGraphQLResponse(
                result.ToString(),
                message: expectedErrorMessageSubString
            );

            string dbResponse = await GetDatabaseResultAsync(dbQuery);
            Assert.IsNull(dbResponse);

            // Update Book Policy: @item.id eq 9
            // Test that the update is successful when policy allows operation.
            // Confirm that one result matches the update request metadata.
            result = await ExecuteGraphQLRequestAsync(
                graphQLMutation,
                graphQLMutationName,
                isAuthenticated: true,
                clientRoleHeader: "policy_tester_08");

            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, result.ToString());
        }
    }
}

