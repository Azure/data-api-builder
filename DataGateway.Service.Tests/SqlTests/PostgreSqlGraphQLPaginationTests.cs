using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Resolvers;
using Azure.DataGateway.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests
{

    [TestClass, TestCategory(TestCategory.POSTGRESQL)]
    public class PostgreSqlGraphQLPaginationTests : SqlTestBase
    {

        #region Test Fixture Setup
        private static GraphQLService _graphQLService;
        private static GraphQLController _graphQLController;
        private static readonly string _integrationTableName = "books";

        /// <summary>
        /// Sets up test fixture for class, only to be run once per test run, as defined by
        /// MSTest decorator.
        /// </summary>
        /// <param name="context"></param>
        [ClassInitialize]
        public static async Task InitializeTestFixture(TestContext context)
        {
            await InitializeTestFixture(context, _integrationTableName, TestCategory.POSTGRESQL);

            // Setup GraphQL Components
            _graphQLService = new GraphQLService(_queryEngine, mutationEngine: null, _metadataStoreProvider);
            _graphQLController = new GraphQLController(_graphQLService);
        }

        #endregion

        #region Tests

        /// <summary>
        /// Request a full connection object {nodes, endCursor, hasNextPage}
        /// </summary>
        [TestMethod]
        public async Task RequestFullConnection()
        {
            string graphQLQueryName = "books";
            string after = SqlPaginationUtil.Base64Encode("{ \"id\": 4 }");
            string graphQLQuery = @"{
                books(first: 2," + $"after: \"{after}\")" + @"{
                    nodes {
                        title
                        publisher {
                            name
                        }
                    }
                    endCursor
                    hasNextPage
                }
            }";
            string postgresQuery = @"
                SELECT to_jsonb(paginatedquery) AS DATA
                FROM
                  (SELECT COALESCE(jsonb_agg(json_build_object('title', title, 'publisher', publisher)), '[]') AS nodes,
                          CASE
                              WHEN max(id) IS NOT NULL THEN ENCODE(CONVERT_TO(CONCAT('{ ', '""id"": ', max(id), ' }'), 'UTF-8'), 'BASE64')
                              ELSE NULL
                          END AS ""endCursor"",
                          COALESCE(max(___rowcount___), 0) > 2 AS ""hasNextPage""
                   FROM
                     (SELECT *,
                             COUNT(*) OVER() AS ___rowcount___
                      FROM
                        (SELECT table0.title AS title,
                                table1_subq.data AS publisher,
                                table0.id AS id
                         FROM books AS table0
                         LEFT OUTER JOIN LATERAL
                           (SELECT to_jsonb(subq3) AS DATA
                            FROM
                              (SELECT table1.name AS name
                               FROM publishers AS table1
                               WHERE table0.publisher_id = table1.id
                               ORDER BY id
                               LIMIT 1) AS subq3) AS table1_subq ON TRUE
                         WHERE id > 4
                         ORDER BY id
                         LIMIT 3) AS count_wrapper
                      LIMIT 2) AS subq4) AS paginatedquery
            ";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(postgresQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Request a full connection object {nodes, endCursor, hasNextPage}
        /// without providing any parameters
        /// </summary>
        [TestMethod]
        public async Task RequestNoParamFullConnection()
        {
            string graphQLQueryName = "books";
            string graphQLQuery = @"{
                books {
                    nodes {
                        id
                        title
                    }
                    endCursor
                    hasNextPage
                }
            }";
            string msSqlQuery = @"
                SELECT to_jsonb(paginatedquery) AS DATA
                FROM
                  (SELECT COALESCE(jsonb_agg(json_build_object('id', id, 'title', title)), '[]') AS nodes,
                          CASE
                              WHEN max(id) IS NOT NULL THEN ENCODE(CONVERT_TO(CONCAT('{ ', '""id"": ', max(id), ' }'), 'UTF-8'), 'BASE64')
                              ELSE NULL
                          END AS ""endCursor"",
                                            COALESCE(max(___rowcount___), 0) > 100 AS ""hasNextPage""
                   FROM
                     (SELECT *,
                             COUNT(*) OVER() AS ___rowcount___
                      FROM
                        (SELECT table0.id AS id,
                                table0.title AS title
                         FROM books AS table0
                         WHERE 1 = 1
                         ORDER BY id
                         LIMIT 101) AS count_wrapper
                      LIMIT 100) AS subq1) AS paginatedquery
            ";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(msSqlQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Request only nodes from the pagination
        /// </summary>
        [TestMethod]
        public async Task RequestNodesOnly()
        {
            string graphQLQueryName = "books";
            string after = SqlPaginationUtil.Base64Encode("{ \"id\": 4 }");
            string graphQLQuery = @"{
                books(first: 2," + $"after: \"{after}\")" + @"{
                    nodes {
                        title
                        publisher_id
                    }
                }
            }";
            string postgresQuery = @"
                SELECT to_jsonb(paginatedquery) AS DATA
                FROM
                  (SELECT COALESCE(jsonb_agg(json_build_object('title', title, 'publisher_id', publisher_id)), '[]') AS nodes
                   FROM
                     (SELECT table0.title AS title,
                             table0.publisher_id AS publisher_id
                      FROM books AS table0
                      WHERE id > 4
                      ORDER BY id
                      LIMIT 2) AS subq2) AS paginatedquery
            ";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(postgresQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Request only endCursor from the pagination
        /// </summary>
        /// <remarks>
        /// This is probably not a common use case, but it necessary to test graphql's capabilites to only
        /// selectively retreive data
        /// </remarks>
        [TestMethod]
        public async Task RequestEndCursorOnly()
        {
            string graphQLQueryName = "books";
            string after = SqlPaginationUtil.Base64Encode("{ \"id\": 4 }");
            string graphQLQuery = @"{
                books(first: 2," + $"after: \"{after}\")" + @"{
                    endCursor
                }
            }";
            string postgresQuery = @"
                SELECT to_jsonb(paginatedquery) AS DATA
                FROM
                  (SELECT CASE
                              WHEN max(id) IS NOT NULL THEN ENCODE(CONVERT_TO(CONCAT('{ ', '""id"": ', max(id), ' }'), 'UTF-8'), 'BASE64')
                              ELSE NULL
                          END AS ""endCursor""
                   FROM
                     (SELECT table0.id AS id
                      FROM books AS table0
                      WHERE id > 4
                      ORDER BY id
                      LIMIT 2) AS subq2) AS paginatedquery
            ";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(postgresQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Request only hasNextPage from the pagination
        /// </summary>
        /// <remarks>
        /// This is probably not a common use case, but it necessary to test graphql's capabilites to only
        /// selectively retreive data
        /// </remarks>
        [TestMethod]
        public async Task RequestHasNextPageOnly()
        {
            string graphQLQueryName = "books";
            string after = SqlPaginationUtil.Base64Encode("{ \"id\": 4 }");
            string graphQLQuery = @"{
                books(first: 2," + $"after: \"{after}\")" + @"{
                    hasNextPage
                }
            }";
            string postgresQuery = @"
                SELECT to_jsonb(paginatedquery) AS DATA
                FROM
                  (SELECT COALESCE(max(___rowcount___), 0) > 2 AS ""hasNextPage""
                   FROM
                     (SELECT *,
                             COUNT(*) OVER() AS ___rowcount___
                      FROM
                        (SELECT *
                         FROM books AS table0
                         WHERE id > 4
                         ORDER BY id
                         LIMIT 3) AS count_wrapper
                      LIMIT 2) AS subq2) AS paginatedquery
            ";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(postgresQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Request an empty page
        /// </summary>
        [TestMethod]
        public async Task RequestEmptyPage()
        {
            string graphQLQueryName = "books";
            string after = SqlPaginationUtil.Base64Encode("{ \"id\": 1000000 }");
            string graphQLQuery = @"{
                 books(first: 2," + $"after: \"{after}\")" + @"{
                    nodes {
                        title
                    }
                    endCursor
                    hasNextPage
                }
            }";

            using JsonDocument result = await GetGraphQLControllerResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            JsonElement root = result.RootElement.GetProperty("data").GetProperty(graphQLQueryName);

            SqlTestHelper.PerformTestEqualJsonStrings(expected: "[]", root.GetProperty("nodes").ToString());
            Assert.AreEqual(null, root.GetProperty("endCursor").GetString());
            Assert.AreEqual(false, root.GetProperty("hasNextPage").GetBoolean());
        }

        #endregion

        #region Negative Tests

        /// <summary>
        /// Request an invalid number of entries for a pagination page
        /// </summary>
        [TestMethod]
        public async Task RequestInvalidFirst()
        {
            string graphQLQueryName = "books";
            string graphQLQuery = @"{
                books(first: -1) {
                    nodes {
                        id
                    }
                }
            }";

            using JsonDocument result = await GetGraphQLControllerResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            SqlTestHelper.TestForErrorInGraphQLResponse(result.RootElement.ToString(), statusCode: $"{DatagatewayException.SubStatusCodes.BadRequest}");
        }

        /// <summary>
        /// Supply a non JSON after parameter
        /// </summary>
        [TestMethod]
        public async Task RequestInvalidAfterWithNonJsonString()
        {
            string graphQLQueryName = "books";
            string graphQLQuery = @"{
                books(after: ""aaaaaaaaa"") {
                    nodes {
                        id
                    }
                }
            }";

            using JsonDocument result = await GetGraphQLControllerResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            SqlTestHelper.TestForErrorInGraphQLResponse(result.RootElement.ToString(), statusCode: $"{DatagatewayException.SubStatusCodes.BadRequest}");
        }

        /// <summary>
        /// Supply an invalid key to the after JSON
        /// </summary>
        [TestMethod]
        public async Task RequestInvalidAfterWithIncorrectKeys()
        {
            string graphQLQueryName = "books";
            string after = SqlPaginationUtil.Base64Encode("{ \"title\": \"Great Book\" }");
            string graphQLQuery = @"{
                 books(" + $"after: \"{after}\")" + @"{
                    nodes {
                        title
                    }
                }
            }";

            using JsonDocument result = await GetGraphQLControllerResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            SqlTestHelper.TestForErrorInGraphQLResponse(result.RootElement.ToString(), statusCode: $"{DatagatewayException.SubStatusCodes.BadRequest}");
        }

        /// <summary>
        /// Supply an invalid type to the key in the after JSON
        /// </summary>
        [TestMethod]
        public async Task RequestInvalidAfterWithIncorrectType()
        {
            string graphQLQueryName = "books";
            string after = SqlPaginationUtil.Base64Encode("{ \"id\": \"Great Book\" }");
            string graphQLQuery = @"{
                 books(" + $"after: \"{after}\")" + @"{
                    nodes {
                        title
                    }
                }
            }";

            using JsonDocument result = await GetGraphQLControllerResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            SqlTestHelper.TestForErrorInGraphQLResponse(result.RootElement.ToString());
        }

        #endregion
    }
}
