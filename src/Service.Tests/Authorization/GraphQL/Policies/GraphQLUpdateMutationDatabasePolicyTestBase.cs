// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
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
    public abstract class GraphQLUpdateMutationDatabasePolicyTestBase : SqlTestBase
    {
        /// <summary>
        /// Do: Test Authenticated GraphQL Update Mutation which triggers
        /// policy processing of policy that allows operation.
        /// Check: Record updated.
        /// </summary>
        /// <param name="dbQuery"></param>
        /// <param name="roleName"></param>
        /// <param name="isAuthenticated"></param>
        /// <returns></returns>
        [TestMethod]
        public async Task UpdateMutation_Success_Policy(string dbQuery, string roleName, bool isAuthenticated)
        {
            string graphQLMutationName = "updatebook";
            string graphQLMutation = @"mutation {
                updatebook(
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

            // Update Book Policy: @item.id eq 9
            // Test that the update is successful when policy allows operation.
            // Confirm that one result matches the update request metadata.
            JsonElement result = await ExecuteGraphQLRequestAsync(
                graphQLMutation,
                graphQLMutationName,
                isAuthenticated: isAuthenticated,
                clientRoleHeader: roleName);

            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, result.ToString());
        }

        /// <summary>
        /// Do: Test Authenticated GraphQL Update Mutation which triggers
        /// policy processing of policy that prevents mutation operation.
        /// Check: Record not updated.
        /// "policy_tester_07"
        /// </summary>
        /// <param name="dbQuery"></param>
        /// <param name="roleName"></param>
        /// <param name="isAuthenticated"></param>
        /// <returns></returns>
        [TestMethod]
        public async Task UpdateMutation_ErrorMessage_Policy(string dbQuery, string roleName, bool isAuthenticated, string expectedErrorMessage, bool mutationShouldComplete)
        {
            string graphQLMutationName = "updateJournal";
            string graphQLMutation = @"mutation {
                updateJournal(
                    id: 1
                    item: {
                        journalname: ""UpdatedJournalName""
                    }
                )
                {
                    id,
                    journalname
                }
            }
            ";

            // Update Book Policy: @item.id ne 9
            // Test that the update fails due to restrictive update policy.
            // Confirm that no result matches the update request metadata.
            JsonElement result = await ExecuteGraphQLRequestAsync(
                graphQLMutation,
                graphQLMutationName,
                isAuthenticated: isAuthenticated,
                clientRoleHeader: roleName);

            SqlTestHelper.TestForErrorInGraphQLResponse(
                result.ToString(),
                message: expectedErrorMessage
            );

            string dbResponse = await GetDatabaseResultAsync(dbQuery);

            if (mutationShouldComplete)
            {
                Assert.IsNotNull(dbResponse);
            }
            else
            {
                Assert.AreEqual(expected: new JsonArray().ToString(), actual: dbResponse);
            }
        }

        /// <summary>
        /// Do: Test Authenticated GraphQL Update Mutation which triggers
        /// policy processing of policy that prevents mutation operation.
        /// Check: Record not updated.
        /// "policy_tester_07"
        /// </summary>
        /// <param name="dbQuery"></param>
        /// <param name="roleName"></param>
        /// <param name="isAuthenticated"></param>
        /// <returns></returns>
        [TestMethod]
        public async Task UpdateMutation_Anonymous_Policy(string dbQuery, string roleName, bool isAuthenticated, string expectedErrorMessage, bool mutationShouldComplete, int id)
        {
            string graphQLMutationName = "updateNotebook";
            string graphQLMutation = @"mutation {
                updateNotebook(
                    id: " + id + @"
                    item: {
                        notebookname: ""UpdatedNoteBookName"",
                        color: ""pink""
                    }
                )
                {
                    id,
                    notebookname
                }
            }
            ";

            // Update Book Policy: @item.id ne 9
            // Test that the update fails due to restrictive update policy.
            // Confirm that no result matches the update request metadata.
            JsonElement result = await ExecuteGraphQLRequestAsync(
                graphQLMutation,
                graphQLMutationName,
                isAuthenticated: isAuthenticated,
                clientRoleHeader: roleName);

            SqlTestHelper.TestForErrorInGraphQLResponse(
                result.ToString(),
                message: expectedErrorMessage
            );

            string dbResponse = await GetDatabaseResultAsync(dbQuery);

            if (mutationShouldComplete)
            {
                Assert.IsNotNull(dbResponse);
            }
            else
            {
                Assert.AreEqual(expected: null, actual: dbResponse);
            }
        }
    }
}

