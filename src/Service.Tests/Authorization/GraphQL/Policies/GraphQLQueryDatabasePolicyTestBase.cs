// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.Tests.SqlTests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Authorization.GraphQL
{
    /// <summary>
    /// Tests Database Authorization Policies applied to GraphQL Queries
    /// </summary>
    [TestClass]
    public abstract class GraphQLQueryDatabasePolicyTestBase : SqlTestBase
    {
        /// <summary>
        /// Tests Authenticated GraphQL Queries which trigger
        /// policy processing. Tests QueryByPK with policies that
        /// filter results:
        /// - To 0 records to detect expected null result
        /// - To 1 record to validate result returns as expected.
        /// </summary>
        [TestMethod]
        public async Task QueryByPK_Policy(string dbQuery)
        {
            string graphQLQueryName = "book_by_pk";
            string graphQLQuery = @"{
                book_by_pk(id: 9) {
                    id,
                    title 
                }
                }";

            // Tests Book Read Policy: @item.title eq 'Policy-Test-01'
            // Expects a non-null result to compare against expected database results.
            JsonElement actual = await ExecuteGraphQLRequestAsync(
                graphQLQuery,
                graphQLQueryName,
                isAuthenticated: true,
                clientRoleHeader: "policy_tester_01");
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());

            // Tests Book Read Policy: @item.title ne 'Policy-Test-01'
            // Expects a null result, HotChocolate  returns -> "book_by_pk": null
            actual = await ExecuteGraphQLRequestAsync(
                graphQLQuery,
                graphQLQueryName,
                isAuthenticated: true,
                clientRoleHeader: "policy_tester_02");
            Assert.AreEqual(expected: true, actual: actual.ValueKind is JsonValueKind.Null);

            // Tests Book Read Policy: @item.id ne 9
            // Expects a null result, HotChocolate  returns -> "book_by_pk": null
            actual = await ExecuteGraphQLRequestAsync(
                graphQLQuery,
                graphQLQueryName,
                isAuthenticated: true,
                clientRoleHeader: "policy_tester_05");
            Assert.AreEqual(expected: true, actual: actual.ValueKind is JsonValueKind.Null);
        }

        /// <summary>
        /// Tests GraphQL QueryByPK which denotes a field containing a selection set {publishers}
        /// which represents a query to fetch the publisher metadata associated to the fetched book record.
        /// Tests how various policies are applied to such a sub-query. The clientRoleHeader define in the
        /// request is used to evaluate the database policy in the top-level and nested queries.
        /// </summary>
        [TestMethod]
        public async Task QueryByPK_NestedRequest_Policy()
        {
            string graphQLQueryName = "book_by_pk";
            string graphQLQuery = @"{
                book_by_pk(id: 10) {
                    id,
                    title,
                    publishers{
                        name
                    }
                }
                }";

            // Tests Book Read Policy: @item.title ne 'Policy-Test-01' and Publisher Read Policy: @item.id ne 1940
            // Target Record: id: 10, title: 'Policy-Test-02' publisher_id: 1940
            // The book policy doesn't restrict this result, but the publisher policy prevents resolving of record.
            // Because publishers is non-nullable in the GraphQL schema, expect HotChocolate to return error.
            JsonElement actual = await base.ExecuteGraphQLRequestAsync(
                graphQLQuery,
                graphQLQueryName,
                isAuthenticated: true,
                clientRoleHeader: "policy_tester_02");
            SqlTestHelper.TestForErrorInGraphQLResponse(
                actual.ToString(),
                message: "Cannot return null for non-nullable field.",
                path: @"[""book_by_pk"",""publishers""]"
            );

            // Tests Book Read Policy: @item.id ne 10 and no Publisher Read Policy.
            // Target Record: id: 10, title: 'Policy-Test-02' publisher_id: 1940
            // The book policy restricts this result.
            // Expects a null result, HotChocolate  returns -> "book_by_pk": null
            actual = await base.ExecuteGraphQLRequestAsync(
                graphQLQuery,
                graphQLQueryName,
                isAuthenticated: true,
                clientRoleHeader: "policy_tester_06");
            Assert.AreEqual(expected: true, actual: actual.ValueKind is JsonValueKind.Null);
        }

        /// <summary>
        /// Tests a GraphQL query that may fetch multiple result records,
        /// but does not include any nested queries.
        /// When a policy is applied to such top-level query, results are restricted
        /// to the expected records.
        /// </summary>
        [TestMethod]
        public async Task QueryMany_Policy(string dbQuery, string roleName)
        {
            string graphQLQueryName = "books";
            string graphQLQuery = @"query {
                books {
                    items {
                        id,
                        title
                    }
            }}";

            JsonElement actual = await base.ExecuteGraphQLRequestAsync(
                graphQLQuery,
                graphQLQueryName,
                isAuthenticated: true,
                clientRoleHeader: roleName);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.GetProperty("items").ToString());
        }

        /// <summary>
        /// Tests a GraphQL query that may fetch multiple result records
        /// on a table with a nullable field, but does not include any nested queries.
        /// When a policy is applied to such top-level query, results are restricted
        /// to the expected records.
        /// </summary>
        [TestMethod]
        public async Task QueryMany_Policy_Nullable(string dbQuery)
        {
            string graphQLQueryName = "fungi";
            string graphQLQuery = @"query {
                fungi {
                    items {
                        speciesid,
                        region
                    }
            }}";

            // Tests Fungi Read Policy: @item.region ne 'northeast'
            // Due to restrictive book policy, expects all fungi records except
            // id: 1 region: 'northeast'
            JsonElement actual = await base.ExecuteGraphQLRequestAsync(
                graphQLQuery,
                graphQLQueryName,
                isAuthenticated: true,
                clientRoleHeader: "policy_tester_01");
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.GetProperty("items").ToString());
        }

        /// <summary>
        /// Tests a GraphQL query that may fetch multiple result records.
        /// When a policy is applied at the top-level query, results are restricted
        /// to the expected records.
        /// When a policy is applied at the nested query level, results may be nulled/trigger
        /// a GraphQL error due to non-nullable fields resolving to null results.
        /// </summary>
        [TestMethod]
        public async Task QueryMany_NestedRequest_Policy(string dbQuery, string roleName, bool expectError)
        {
            string graphQLQueryName = "books";
            string graphQLQuery = @"query {
                books {
                    items {
                        id,
                        title,
                        publishers{
                            id,
                            name
                        }
                    }
            }}";

            JsonElement actual = await base.ExecuteGraphQLRequestAsync(
                graphQLQuery,
                graphQLQueryName,
                isAuthenticated: true,
                clientRoleHeader: roleName);

            if (expectError)
            {
                SqlTestHelper.TestForErrorInGraphQLResponse(
                    actual.ToString(),
                    message: "Cannot return null for non-nullable field.",
                    path: @"[""books"",""items"",0,""publishers""]"
                );
            }
            else
            {
                string expected = await GetDatabaseResultAsync(dbQuery);
                SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.GetProperty("items").ToString());
            }
        }
    }
}
