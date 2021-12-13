using System;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Azure.DataGateway.Service.Tests.SqlTests
{
    /// <summary>
    /// Test GraphQL Queries validating proper resolver/engine operation.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class MsSqlGraphQLQueryTests : SqlTestBase
    {
        #region Test Fixture Setup
        private static GraphQLService _graphQLService;
        private static GraphQLController _graphQLController;
        private static readonly string _integrationTableName = "character";

        /// <summary>
        /// Sets up test fixture for class, only to be run once per test run, as defined by
        /// MSTest decorator.
        /// </summary>
        /// <param name="context"></param>
        [ClassInitialize]
        public static void InitializeTestFixture(TestContext context)
        {
            InitializeTestFixture(context, _integrationTableName, TestCategory.MSSQL);

            // Setup GraphQL Components
            //
            _graphQLService = new GraphQLService(_queryEngine, mutationEngine: null, _metadataStoreProvider);
            _graphQLController = new GraphQLController(_graphQLService);
        }

        #endregion

        #region Tests
        /// <summary>
        /// Gets array of results for querying more than one item.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task MultipleResultQuery()
        {
            string graphQLQueryName = "getBooks";
            string graphQLQuery = @"{
                getBooks(first: 123) {
                    id
                    title
                }
            }";
            string msSqlQuery = $"SELECT id, title FROM books ORDER BY id FOR JSON PATH, INCLUDE_NULL_VALUES";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName);
            string expected = await GetDatabaseResultAsync(msSqlQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Gets array of results for querying more than one item.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task MultipleResultJoinQuery()
        {
            string graphQLQueryName = "getBooks";
            string graphQLQuery = @"{
                getBooks(first: 100) {
                    id
                    title
                    publisher_id
                    publisher {
                        id
                        name
                    }
                    reviews(first: 100) {
                        id
                        content
                    }
                }
            }";
            string msSqlQuery = @"
                SELECT TOP 100 [table0].[id] AS [id],
                    [table0].[title] AS [title],
                    [table0].[publisher_id] AS [publisher_id],
                    JSON_QUERY([table1_subq].[data]) AS [publisher],
                    JSON_QUERY(COALESCE([table2_subq].[data], '[]')) AS [reviews]
                FROM [books] AS [table0]
                OUTER APPLY (
                    SELECT TOP 1 [table1].[id] AS [id],
                        [table1].[name] AS [name]
                    FROM [publishers] AS [table1]
                    WHERE [table0].[publisher_id] = [table1].[id]
                    ORDER BY [id]
                    FOR JSON PATH,
                        INCLUDE_NULL_VALUES,
                        WITHOUT_ARRAY_WRAPPER
                    ) AS [table1_subq]([data])
                OUTER APPLY (
                    SELECT TOP 100 [table2].[id] AS [id],
                        [table2].[content] AS [content]
                    FROM [reviews] AS [table2]
                    WHERE [table0].[id] = [table2].[book_id]
                    ORDER BY [id]
                    FOR JSON PATH,
                        INCLUDE_NULL_VALUES
                    ) AS [table2_subq]([data])
                WHERE 1 = 1
                ORDER BY [id]
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName);
            string expected = await GetDatabaseResultAsync(msSqlQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Gets array of results for querying more than one item.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task DeeplyNestedJoinQuery()
        {
            string graphQLQueryName = "getBooks";
            string graphQLQuery = @"{
              getBooks(first: 100) {
                title
                publisher {
                  name
                  books(first: 100) {
                    title
                    publisher {
                      name
                      books(first: 100) {
                        title
                        publisher {
                          name
                        }
                      }
                    }
                  }
                }
              }
            }";
            string msSqlQuery = @"
                SELECT TOP 100 [table0].[title] AS [title],
                    JSON_QUERY([table1_subq].[data]) AS [publisher]
                FROM [books] AS [table0]
                OUTER APPLY (
                    SELECT TOP 1 [table1].[name] AS [name],
                        JSON_QUERY(COALESCE([table2_subq].[data], '[]')) AS [books]
                    FROM [publishers] AS [table1]
                    OUTER APPLY (
                        SELECT TOP 100 [table2].[title] AS [title],
                            JSON_QUERY([table3_subq].[data]) AS [publisher]
                        FROM [books] AS [table2]
                        OUTER APPLY (
                            SELECT TOP 1 [table3].[name] AS [name],
                                JSON_QUERY(COALESCE([table4_subq].[data], '[]')) AS [books]
                            FROM [publishers] AS [table3]
                            OUTER APPLY (
                                SELECT TOP 100 [table4].[title] AS [title],
                                    JSON_QUERY([table5_subq].[data]) AS [publisher]
                                FROM [books] AS [table4]
                                OUTER APPLY (
                                    SELECT TOP 1 [table5].[name] AS [name]
                                    FROM [publishers] AS [table5]
                                    WHERE [table4].[publisher_id] = [table5].[id]
                                    ORDER BY [id]
                                    FOR JSON PATH,
                                        INCLUDE_NULL_VALUES,
                                        WITHOUT_ARRAY_WRAPPER
                                    ) AS [table5_subq]([data])
                                WHERE [table3].[id] = [table4].[publisher_id]
                                ORDER BY [id]
                                FOR JSON PATH,
                                    INCLUDE_NULL_VALUES
                                ) AS [table4_subq]([data])
                            WHERE [table2].[publisher_id] = [table3].[id]
                            ORDER BY [id]
                            FOR JSON PATH,
                                INCLUDE_NULL_VALUES,
                                WITHOUT_ARRAY_WRAPPER
                            ) AS [table3_subq]([data])
                        WHERE [table1].[id] = [table2].[publisher_id]
                        ORDER BY [id]
                        FOR JSON PATH,
                            INCLUDE_NULL_VALUES
                        ) AS [table2_subq]([data])
                    WHERE [table0].[publisher_id] = [table1].[id]
                    ORDER BY [id]
                    FOR JSON PATH,
                        INCLUDE_NULL_VALUES,
                        WITHOUT_ARRAY_WRAPPER
                    ) AS [table1_subq]([data])
                WHERE 1 = 1
                ORDER BY [id]
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES
            ";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName);
            string expected = await GetDatabaseResultAsync(msSqlQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        [TestMethod]
        public async Task QueryWithSingleColumnPrimaryKey()
        {
            string graphQLQueryName = "getBook";
            string graphQLQuery = @"{
                getBook(id: 2) {
                    title
                }
            }";
            string msSqlQuery = @"
                SELECT title FROM books
                WHERE id = 2 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER
            ";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName);
            string expected = await GetDatabaseResultAsync(msSqlQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        [TestMethod]
        public async Task QueryWithMultileColumnPrimaryKey()
        {
            string graphQLQueryName = "getReview";
            string graphQLQuery = @"{
                getReview(id: 568, book_id: 1) {
                    content
                }
            }";
            string msSqlQuery = @"
                SELECT TOP 1 content FROM reviews
                WHERE id = 568 AND book_id = 1 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER
            ";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName);
            string expected = await GetDatabaseResultAsync(msSqlQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        [TestMethod]
        public async Task QueryWithNullResult()
        {
            string graphQLQueryName = "getBook";
            string graphQLQuery = @"{
                getBook(id: -9999) {
                    title
                }
            }";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName);

            SqlTestHelper.PerformTestEqualJsonStrings("null", actual);
        }

        #endregion

        #region Query Test Helper Functions
        /// <summary>
        /// Sends graphQL query through graphQL service, consisting of gql engine processing (resolvers, object serialization)
        /// returning JSON formatted result from 'data' property.
        /// </summary>
        /// <param name="graphQLQuery"></param>
        /// <param name="graphQLQueryName"></param>
        /// <returns>string in JSON format</returns>
        public static async Task<string> GetGraphQLResultAsync(string graphQLQuery, string graphQLQueryName)
        {
            string graphqlQueryJson = JObject.FromObject(new
            {
                query = graphQLQuery
            }).ToString();
            Console.WriteLine(graphqlQueryJson);
            _graphQLController.ControllerContext.HttpContext = GetHttpContextWithBody(graphqlQueryJson);
            JsonDocument graphQLResult = await _graphQLController.PostAsync();
            Console.WriteLine(graphQLResult.RootElement.ToString());
            JsonElement graphQLResultData = graphQLResult.RootElement.GetProperty("data").GetProperty(graphQLQueryName);

            // JsonElement.ToString() prints null values as empty strings instead of "null"
            return graphQLResultData.GetRawText();
        }

        #endregion
    }
}
