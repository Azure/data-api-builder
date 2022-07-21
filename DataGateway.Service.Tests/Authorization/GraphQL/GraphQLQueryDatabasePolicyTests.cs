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
        /// SQL Query by pk non nested
        /// Positive: policy allows result
        /// <summary>
        [TestMethod]
        public async Task QueryByPK_PolicyAllowsTopLevelResult()
        {
            string dbQuery = @"
                SELECT
                [table0].[id] AS [id],
                [table0].[title] AS [title]
                FROM [dbo].[books] AS [table0] 
                WHERE ([table0].[id] = 9)
                AND [table0].[id] = 9 
                ORDER BY [table0].[id] ASC 
                FOR JSON PATH, INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER";

            string graphQLQueryName = "book_by_pk";
            string graphQLQuery = @"{
                book_by_pk(id: 9) {
                        id,
                        title 
                    }
                }";

            JsonElement actual = await base.ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: true, clientRoleHeader: "policy_tester_01");
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        [TestMethod]
        public async Task QueryByPK_PolicyDisallowsTopLevelResult()
        {
            string dbQuery = @"
                SELECT
                [table0].[id] AS [id],
                [table0].[title] AS [title]
                FROM [dbo].[books] AS [table0] 
                WHERE ([table0].[id] = 9)
                and [table0].[title] != 'Policy-Test-01'
                ORDER BY [table0].[id] ASC 
                FOR JSON PATH, INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER";

            string graphQLQueryName = "book_by_pk";
            string graphQLQuery = @"{
                book_by_pk(id: 9) {
                    id,
                    title 
                }
                }";

            JsonElement actual = await base.ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: true, clientRoleHeader: "policy_tester_02");
            string expected = await GetDatabaseResultAsync(dbQuery);

            Assert.AreEqual(expected: expected is null, actual: actual.ValueKind is JsonValueKind.Null);
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
