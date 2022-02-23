using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests
{

    [TestClass, TestCategory(TestCategory.POSTGRESQL)]
    public class PostgreSqlGraphQLQueryTests : SqlTestBase
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
        [TestMethod]
        public async Task MultipleResultQuery()
        {
            string graphQLQueryName = "getBooks";
            string graphQLQuery = @"{
                getBooks(first: 100) {
                    id
                    title
                }
            }";
            string postgresQuery = $"SELECT json_agg(to_jsonb(table0)) FROM (SELECT id, title FROM books ORDER BY id) as table0 LIMIT 100";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(postgresQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

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
                    authors(first: 100) {
                        id
                        name
                    }
                }
            }";
            string postgresQuery = @"
                SELECT COALESCE(jsonb_agg(to_jsonb(subq8)), '[]') AS data
                FROM
                  (SELECT table0.id AS id,
                          table0.title AS title,
                          table0.publisher_id AS publisher_id,
                          table1_subq.data AS publisher,
                          table2_subq.data AS reviews,
                          table3_subq.data AS authors
                   FROM books AS table0
                   LEFT OUTER JOIN LATERAL
                     (SELECT to_jsonb(subq5) AS data
                      FROM
                        (SELECT table1.id AS id,
                                table1.name AS name
                         FROM publishers AS table1
                         WHERE table0.publisher_id = table1.id
                         ORDER BY id
                         LIMIT 1) AS subq5) AS table1_subq ON TRUE
                   LEFT OUTER JOIN LATERAL
                     (SELECT COALESCE(jsonb_agg(to_jsonb(subq6)), '[]') AS data
                      FROM
                        (SELECT table2.id AS id,
                                table2.content AS content
                         FROM reviews AS table2
                         WHERE table0.id = table2.book_id
                         ORDER BY id
                         LIMIT 100) AS subq6) AS table2_subq ON TRUE
                   LEFT OUTER JOIN LATERAL
                     (SELECT COALESCE(jsonb_agg(to_jsonb(subq7)), '[]') AS data
                      FROM
                        (SELECT table3.id AS id,
                                table3.name AS name
                         FROM authors AS table3
                         INNER JOIN book_author_link AS table4 ON table4.author_id = table3.id
                         WHERE table0.id = table4.book_id
                         ORDER BY id
                         LIMIT 100) AS subq7) AS table3_subq ON TRUE
                   WHERE 1 = 1
                   ORDER BY id
                   LIMIT 100) AS subq8
            ";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(postgresQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// This deeply nests a many-to-one/one-to-many join multiple times to
        /// show that it still results in a valid query.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task DeeplyNestedManyToOneJoinQuery()
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

            string postgresQuery = @"
                SELECT COALESCE(jsonb_agg(to_jsonb(subq11)), '[]') AS data
                FROM
                  (SELECT table0.title AS title,
                          table1_subq.data AS publisher
                   FROM books AS table0
                   LEFT OUTER JOIN LATERAL
                     (SELECT to_jsonb(subq10) AS data
                      FROM
                        (SELECT table1.name AS name,
                                table2_subq.data AS books
                         FROM publishers AS table1
                         LEFT OUTER JOIN LATERAL
                           (SELECT COALESCE(jsonb_agg(to_jsonb(subq9)), '[]') AS data
                            FROM
                              (SELECT table2.title AS title,
                                      table3_subq.data AS publisher
                               FROM books AS table2
                               LEFT OUTER JOIN LATERAL
                                 (SELECT to_jsonb(subq8) AS data
                                  FROM
                                    (SELECT table3.name AS name,
                                            table4_subq.data AS books
                                     FROM publishers AS table3
                                     LEFT OUTER JOIN LATERAL
                                       (SELECT COALESCE(jsonb_agg(to_jsonb(subq7)), '[]') AS data
                                        FROM
                                          (SELECT table4.title AS title,
                                                  table5_subq.data AS publisher
                                           FROM books AS table4
                                           LEFT OUTER JOIN LATERAL
                                             (SELECT to_jsonb(subq6) AS data
                                              FROM
                                                (SELECT table5.name AS name
                                                 FROM publishers AS table5
                                                 WHERE table4.publisher_id = table5.id
                                                 ORDER BY id
                                                 LIMIT 1) AS subq6) AS table5_subq ON TRUE
                                           WHERE table3.id = table4.publisher_id
                                           ORDER BY id
                                           LIMIT 100) AS subq7) AS table4_subq ON TRUE
                                     WHERE table2.publisher_id = table3.id
                                     ORDER BY id
                                     LIMIT 1) AS subq8) AS table3_subq ON TRUE
                               WHERE table1.id = table2.publisher_id
                               ORDER BY id
                               LIMIT 100) AS subq9) AS table2_subq ON TRUE
                         WHERE table0.publisher_id = table1.id
                         ORDER BY id
                         LIMIT 1) AS subq10) AS table1_subq ON TRUE
                   WHERE 1 = 1
                   ORDER BY id
                   LIMIT 100) AS subq11
            ";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(postgresQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// This deeply nests a many-to-many join multiple times to show that
        /// it still results in a valid query.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task DeeplyNestedManyToManyJoinQuery()
        {
            string graphQLQueryName = "getBooks";
            string graphQLQuery = @"{
              getBooks(first: 100) {
                title
                authors(first: 100) {
                  name
                  books(first: 100) {
                    title
                    authors(first: 100) {
                      name
                    }
                  }
                }
              }
            }";

            string postgresQuery = @"
                SELECT COALESCE(jsonb_agg(to_jsonb(subq10)), '[]') AS data
                FROM
                  (SELECT table0.title AS title,
                          table1_subq.data AS authors
                   FROM books AS table0
                   LEFT OUTER JOIN LATERAL
                     (SELECT COALESCE(jsonb_agg(to_jsonb(subq9)), '[]') AS data
                      FROM
                        (SELECT table1.name AS name,
                                table2_subq.data AS books
                         FROM authors AS table1
                         INNER JOIN book_author_link AS table6 ON table6.author_id = table1.id
                         LEFT OUTER JOIN LATERAL
                           (SELECT COALESCE(jsonb_agg(to_jsonb(subq8)), '[]') AS data
                            FROM
                              (SELECT table2.title AS title,
                                      table3_subq.data AS authors
                               FROM books AS table2
                               INNER JOIN book_author_link AS table5 ON table5.book_id = table2.id
                               LEFT OUTER JOIN LATERAL
                                 (SELECT COALESCE(jsonb_agg(to_jsonb(subq7)), '[]') AS data
                                  FROM
                                    (SELECT table3.name AS name
                                     FROM authors AS table3
                                     INNER JOIN book_author_link AS table4 ON table4.author_id = table3.id
                                     WHERE table2.id = table4.book_id
                                     ORDER BY id
                                     LIMIT 100) AS subq7) AS table3_subq ON TRUE
                               WHERE table1.id = table5.author_id
                               ORDER BY id
                               LIMIT 100) AS subq8) AS table2_subq ON TRUE
                         WHERE table0.id = table6.book_id
                         ORDER BY id
                         LIMIT 100) AS subq9) AS table1_subq ON TRUE
                   WHERE 1 = 1
                   ORDER BY id
                   LIMIT 100) AS subq10
            ";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(postgresQuery);

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
            string postgresQuery = @"
                SELECT to_jsonb(subq) AS data
                FROM (
                    SELECT table0.title AS title
                    FROM books AS table0
                    WHERE id = 2
                    ORDER BY id
                    LIMIT 1
                ) AS subq
            ";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(postgresQuery);

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
            string postgresQuery = @"
                SELECT to_jsonb(subq) AS data
                FROM (
                    SELECT table0.content AS content
                    FROM reviews AS table0
                    WHERE id = 568 AND book_id = 1
                    ORDER BY id, book_id
                    LIMIT 1
                ) AS subq
            ";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(postgresQuery);

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

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);

            SqlTestHelper.PerformTestEqualJsonStrings("null", actual);
        }

        /// <sumary>
        /// Test if first param successfully limits list quries
        /// </summary>
        [TestMethod]
        public async Task TestFirstParamForListQueries()
        {
            string graphQLQueryName = "getBooks";
            string graphQLQuery = @"{
                getBooks(first: 1) {
                    title
                    publisher {
                        name
                        books(first: 3) {
                            title
                        }
                    }
                }
            }";

            string postgresQuery = @"
                SELECT COALESCE(jsonb_agg(to_jsonb(subq5)), '[]') AS DATA
                FROM
                  (SELECT table0.title AS title,
                          table1_subq.data AS publisher
                   FROM books AS table0
                   LEFT OUTER JOIN LATERAL
                     (SELECT to_jsonb(subq4) AS DATA
                      FROM
                        (SELECT table1.name AS name,
                                table2_subq.data AS books
                         FROM publishers AS table1
                         LEFT OUTER JOIN LATERAL
                           (SELECT COALESCE(jsonb_agg(to_jsonb(subq3)), '[]') AS DATA
                            FROM
                              (SELECT table2.title AS title
                               FROM books AS table2
                               WHERE table1.id = table2.publisher_id
                               ORDER BY id
                               LIMIT 3) AS subq3) AS table2_subq ON TRUE
                         WHERE table0.publisher_id = table1.id
                         ORDER BY id
                         LIMIT 1) AS subq4) AS table1_subq ON TRUE
                   WHERE 1 = 1
                   ORDER BY id
                   LIMIT 1) AS subq5
            ";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(postgresQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <sumary>
        /// Test if filter param successfully filters the query results
        /// </summary>
        [TestMethod]
        public async Task TestFilterParamForListQueries()
        {
            string graphQLQueryName = "getBooks";
            string graphQLQuery = @"{
                getBooks(_filter: ""id ge 1 and id le 4"") {
                    id
                    publisher {
                        books(first: 3, _filter: ""id ne 2"") {
                            id
                        }
                    }
                }
            }";

            string postgresQuery = @"
                SELECT COALESCE(jsonb_agg(to_jsonb(subq12)), '[]') AS data
                FROM
                  (SELECT table0.id AS id,
                          table1_subq.data AS publisher
                   FROM books AS table0
                   LEFT OUTER JOIN LATERAL
                     (SELECT to_jsonb(subq11) AS data
                      FROM
                        (SELECT table2_subq.data AS books
                         FROM publishers AS table1
                         LEFT OUTER JOIN LATERAL
                           (SELECT COALESCE(jsonb_agg(to_jsonb(subq10)), '[]') AS data
                            FROM
                              (SELECT table2.id AS id
                               FROM books AS table2
                               WHERE (id != 2)
                                 AND table1.id = table2.publisher_id
                               ORDER BY table2.id
                               LIMIT 3) AS subq10) AS table2_subq ON TRUE
                         WHERE table0.publisher_id = table1.id
                         ORDER BY table1.id
                         LIMIT 1) AS subq11) AS table1_subq ON TRUE
                   WHERE ((id >= 1)
                          AND (id <= 4))
                   ORDER BY table0.id
                   LIMIT 100) AS subq12
            ";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(postgresQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        #endregion

        #region Negative Tests

        [TestMethod]
        public async Task TestInvalidFirstParamQuery()
        {
            string graphQLQueryName = "getBooks";
            string graphQLQuery = @"{
                getBooks(first: -1) {
                    id
                    title
                }
            }";

            JsonElement result = await GetGraphQLControllerResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            SqlTestHelper.TestForErrorInGraphQLResponse(result.ToString(), statusCode: $"{DataGatewayException.SubStatusCodes.BadRequest}");
        }

        [TestMethod]
        public async Task TestInvalidFilterParamQuery()
        {
            string graphQLQueryName = "getBooks";
            string graphQLQuery = @"{
                getBooks(_filter: ""INVALID"") {
                    id
                    title
                }
            }";

            JsonElement result = await GetGraphQLControllerResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            SqlTestHelper.TestForErrorInGraphQLResponse(result.ToString(), statusCode: $"{DataGatewayException.SubStatusCodes.BadRequest}");
        }
        #endregion
    }
}
