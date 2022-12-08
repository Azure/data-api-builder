using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.Tests.SqlTests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Authorization.GraphQL
{
    /// <summary>
    /// Tests Database Authorization Policies applied to GraphQL Queries
    /// </summary>
    [TestClass]
    public abstract class GraphQLDeleteMutationDatabasePolicyTestBase : SqlTestBase
    {
        /// <summary>
        /// Tests Authenticated GraphQL Delete Mutation which triggers
        /// policy processing. Tests deleteBook with policy that
        /// allows/prevents operation.
        /// - Operation allowed: confirm record deleted.
        /// - Operation forbidden: confirm record not deleted.
        /// </summary>
        [TestMethod]
        public async Task DeleteMutation_Policy(string dbQuery)
        {
            string graphQLMutationName = "deletebook";
            string graphQLMutation = @"mutation {
                deletebook(id: 9)
                {
                    title,
                    publisher_id
                }
            }
            ";

            // Delete Book Policy: @item.id ne 9
            // Test that the delete fails due to restrictive delete policy.
            // Confirm that records are not deleted.
            await ExecuteGraphQLRequestAsync(
                graphQLMutation,
                graphQLMutationName,
                isAuthenticated: true,
                clientRoleHeader: "policy_tester_07");

            string expected = await GetDatabaseResultAsync(dbQuery);
            Assert.IsNotNull(expected, message: "Expected result was null, erroneous delete occurred.");

            // Delete Book Policy: @item.id eq 9
            // Test that the delete is successful when policy allows operation.
            // Confirm that record is deleted.
            await ExecuteGraphQLRequestAsync(
                graphQLMutation,
                graphQLMutationName,
                isAuthenticated: true,
                clientRoleHeader: "policy_tester_08");

            string dbResponse = await GetDatabaseResultAsync(dbQuery);
            Assert.IsTrue(new JsonArray().ToString().Equals(dbResponse),
                message:
                "Expected result was not empty, delete operation failed.");
        }
    }
}
