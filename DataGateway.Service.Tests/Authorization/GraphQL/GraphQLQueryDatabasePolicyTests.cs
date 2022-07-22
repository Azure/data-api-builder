using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Tests.SqlTests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.Authorization.GraphQL
{
    [TestClass]
    public class GraphQLQueryDatabasePolicyTests : SqlTestBase
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
        /// Tests Authenticated GraphQL Queries which trigger
        /// policy processing. Tests QueryByPK with policies that
        /// filter results:
        /// - To 0 records to detect expected null result
        /// - To 1 record to validate result returns as expected.
        /// </summary>
        [TestMethod]
        public async Task QueryByPK_Policy()
        {
            string dbQuery = @"
                SELECT
                [table0].[id] AS [id],
                [table0].[title] AS [title]
                FROM [dbo].[books] AS [table0] 
                WHERE ([table0].[id] = 9)
                AND [table0].[title] = 'Policy-Test-01' 
                ORDER BY [table0].[id] ASC 
                FOR JSON PATH, INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER";

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
            Assert.AreEqual(expected: expected, actual: actual.ToString());

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

        // SQL Query by pk nested
        // Positive: top-level policy allows result, nested policy allows result
        // Positive: top-level policy allows result, nested policy nullifies result (Nullable field)
        // Negative: top-level policy nullifies result, nested policy allows result
        // Negative: top-level policy allows result, nested policy nullifies result (NON Nullable field)
        // <>: triple nested circular reference , fail or succeed nicely.

        // SQL Query Many non nested
        // Positive: policy allows result
        // Negative: policy nullifies result
        [TestMethod]
        public async Task QueryMany_PolicyAllowsTopLevelResult()
        {
            string dbQuery = @"
                SELECT TOP 100
                [table0].[id] AS [id],
                [table0].[title] AS [title]
                FROM [dbo].[books] AS [table0] 
                WHERE ([title] = 'Policy-Test-01') 
                ORDER BY [table0].[id] ASC 
                FOR JSON PATH, INCLUDE_NULL_VALUES";

            string graphQLQueryName = "books";
            string graphQLQuery = @"query {
                books {
                    items {
                        id,
                        title
                    }
            }}";

            JsonElement actual = await base.ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: true, clientRoleHeader: "policy_tester_01");
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.GetProperty("items").ToString());
        }

        [TestMethod]
        public async Task QueryMany_PolicyDisallowsTopLevelResult()
        {
            string dbQuery = @"
                SELECT TOP 100
                [table0].[id] AS [id],
                [table0].[title] AS [title]
                FROM [dbo].[books] AS [table0] 
                WHERE ([title] != 'Policy-Test-01') 
                ORDER BY [table0].[id] ASC 
                FOR JSON PATH, INCLUDE_NULL_VALUES";

            string graphQLQueryName = "books";
            string graphQLQuery = @"query {
                books {
                    items {
                        id,
                        title
                    }
            }}";

            JsonElement actual = await base.ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: true, clientRoleHeader: "policy_tester_02");
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.GetProperty("items").ToString());
        }

        // SQL Query Many nested
        // Positive: top-level policy allows result, nested policy allows result
        [TestMethod]
        public async Task QueryMany_Policy_AllowedTopLevelResult_AllowedNestedResult()
        {
            string dbQuery = @"
                SELECT TOP 100 
                [table0].[id] AS [id], 
                [table0].[title] AS [title], 
                JSON_QUERY (
                [table1_subq].[data]) AS [publishers] 
                FROM [dbo].[books] AS [table0] 
                OUTER APPLY (
                SELECT TOP 1 
                [table1].[id] AS [id], 
                [table1].[name] AS [name] 
                FROM [dbo].[publishers] AS [table1] 
                WHERE ([id] = 1940) AND [table0].[publisher_id] = [table1].[id] 
                ORDER BY [table1].[id] ASC 
                FOR JSON PATH, INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER) 
                AS [table1_subq]([data]) 
                WHERE ([title] = 'Policy-Test-01') 
                ORDER BY [table0].[id] ASC 
                FOR JSON PATH, INCLUDE_NULL_VALUES
            ";

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

            JsonElement actual = await base.ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: true, clientRoleHeader: "policy_tester_01");
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.GetProperty("items").ToString());
        }

        // Positive: top-level policy allows result, nested policy nullifies result (Nullable field)
        [TestMethod]
        public async Task QueryMany_Policy_AllowedTopLevelResult_DisallowedNestedResult()
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

            JsonElement actual = await base.ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: true, clientRoleHeader: "policy_tester_03");

            SqlTestHelper.TestForErrorInGraphQLResponse(
                actual.ToString(),
                message: "Cannot return null for non-nullable field.",
                path: @"[""books"",""items"",0,""publishers""]"
            );
        }

        // Negative: top-level policy nullifies result, nested policy allows result
        // Policy on Publishers filters out a result for one of the book results,
        // nullifying the entire result.
        [TestMethod]
        public async Task QueryMany_Policy_DisAllowedTopLevelResult_AllowedNestedResult()
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

            JsonElement actual = await base.ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: true, clientRoleHeader: "policy_tester_04");

            // We expect a GraphQL response error. 
            SqlTestHelper.TestForErrorInGraphQLResponse(
                actual.ToString(),
                message: "Cannot return null for non-nullable field.",
                path: @"[""books"",""items"",0,""publishers""]"
            );
        }
        // Negative: top-level policy allows result, nested policy nullifies result (NON Nullable field)
        // <>: triple nested circular reference , fail or succeed nicely.
    }
}
