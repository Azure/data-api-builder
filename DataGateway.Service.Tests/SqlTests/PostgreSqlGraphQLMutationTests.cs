using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests
{

    [TestClass, TestCategory(TestCategory.POSTGRESQL)]
    public class PostgreSqlGraphQLMutationTests : SqlTestBase
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
            _graphQLService = new GraphQLService(_queryEngine, _mutationEngine, _metadataStoreProvider);
            _graphQLController = new GraphQLController(_graphQLService);
        }

        #endregion

        #region  Tests

        [TestMethod]
        public async Task InsertMutation()
        {
            string graphQLMutationName = "insertBook";
            string graphQLMutation = @"
                mutation {
                    insertBook(title: ""My New Book"", publisher_id: 1234) {
                        id
                        title
                    }
                }
            ";

            string postgresQuery = @"
                SELECT to_jsonb(subq) AS DATA
                FROM
                  (SELECT table0.id AS id,
                          table0.title AS title
                   FROM books AS table0
                   WHERE id = 5001
                     AND title = 'My New Book'
                     AND publisher_id = 1234
                   ORDER BY id
                   LIMIT 1) AS subq
            ";

            string actual = await GetGraphQLResultAsync(graphQLMutation, graphQLMutationName, _graphQLController);
            string expected = await GetDatabaseResultAsync(postgresQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);

            await ResetDbStateAsync();
        }

        [TestMethod]
        public async Task UpdateMutation()
        {
            string graphQLMutationName = "editBook";
            string graphQLMutation = @"
                mutation {
                    editBook(id: 1, title: ""Even Better Title"", publisher_id: 2345) {
                        title
                        publisher_id
                    }
                }
            ";

            string postgresQuery = @"
                SELECT to_jsonb(subq) AS DATA
                FROM
                  (SELECT table0.title AS title,
                          table0.publisher_id AS publisher_id
                   FROM books AS table0
                   WHERE id = 1
                   ORDER BY id
                   LIMIT 1) AS subq
            ";

            string actual = await GetGraphQLResultAsync(graphQLMutation, graphQLMutationName, _graphQLController);
            string expected = await GetDatabaseResultAsync(postgresQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);

            await ResetDbStateAsync();
        }

        [TestMethod]
        public async Task InsertMutationForNonGraphQLTypeTable()
        {
            string graphQLMutationName = "addAuthorToBook";
            string graphQLMutation = @"
                mutation {
                    addAuthorToBook(author_id: 123, book_id: 2)
                }
            ";

            string postgresQuery = @"
                SELECT to_jsonb(subq) AS DATA
                FROM
                  (SELECT COUNT(*) AS COUNT
                   FROM book_author_link AS table0
                   WHERE book_id = 2
                     AND author_id = 123) AS subq
            ";

            await GetGraphQLResultAsync(graphQLMutation, graphQLMutationName, _graphQLController);
            string dbResponse = await GetDatabaseResultAsync(postgresQuery);

            using JsonDocument result = JsonDocument.Parse(dbResponse);
            Assert.AreEqual(result.RootElement.GetProperty("count").GetInt64(), 1);

            await ResetDbStateAsync();
        }

        [TestMethod]
        public async Task NestedQueryingInMutation()
        {
            string graphQLMutationName = "insertBook";
            string graphQLMutation = @"
                mutation {
                    insertBook(title: ""My New Book"", publisher_id: 1234) {
                        id
                        title
                        publisher {
                            name
                        }
                    }
                }
            ";

            string postgresQuery = @"
                SELECT to_jsonb(subq3) AS DATA
                FROM
                  (SELECT table0.id AS id,
                          table0.title AS title,
                          table1_subq.data AS publisher
                   FROM books AS table0
                   LEFT OUTER JOIN LATERAL
                     (SELECT to_jsonb(subq2) AS DATA
                      FROM
                        (SELECT table1.name AS name
                         FROM publishers AS table1
                         WHERE table1.id = table0.publisher_id
                         ORDER BY table1.id
                         LIMIT 1) AS subq2) AS table1_subq ON TRUE
                   WHERE table0.id = 5001
                     AND table0.title = 'My New Book'
                     AND table0.publisher_id = 1234
                   ORDER BY table0.id
                   LIMIT 1) AS subq3
            ";

            string actual = await GetGraphQLResultAsync(graphQLMutation, graphQLMutationName, _graphQLController);
            string expected = await GetDatabaseResultAsync(postgresQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);

            await ResetDbStateAsync();
        }
        #endregion

        #region Negative Tests

        [TestMethod]
        public async Task InsertWithInvalidForeignKey()
        {
            string graphQLMutationName = "insertBook";
            string graphQLMutation = @"
                mutation {
                    insertBook(title: ""My New Book"", publisher_id: -1) {
                        id
                        title
                    }
                }
            ";

            JsonDocument result = await GetGraphQLControllerResultAsync(graphQLMutation, graphQLMutationName, _graphQLController);

            // TODO improve the error message returned by the graphql service to something more useful then smth generic
            Assert.IsTrue(result.RootElement.ToString().Contains("error"), "Error was expected");

            string postgresQuery = @"
                SELECT to_jsonb(subq) AS DATA
                FROM
                  (SELECT COUNT(*) AS COUNT
                   FROM books
                   WHERE publisher_id = -1 ) AS subq
            ";

            string dbResponse = await GetDatabaseResultAsync(postgresQuery);
            using JsonDocument dbResponseJson = JsonDocument.Parse(dbResponse);
            Assert.AreEqual(dbResponseJson.RootElement.GetProperty("count").GetInt64(), 0);

            await ResetDbStateAsync();
        }

        [TestMethod]
        public async Task UpdateWithInvalidForeignKey()
        {
            string graphQLMutationName = "editBook";
            string graphQLMutation = @"
                mutation {
                    editBook(id: 1, publisher_id: -1) {
                        id
                        title
                    }
                }
            ";

            JsonDocument result = await GetGraphQLControllerResultAsync(graphQLMutation, graphQLMutationName, _graphQLController);

            // TODO improve the error message returned by the graphql service to something more useful then smth generic
            Assert.IsTrue(result.RootElement.ToString().Contains("error"), "Error was expected");

            string postgresQuery = @"
                SELECT to_jsonb(subq) AS DATA
                FROM
                  (SELECT COUNT(*) AS COUNT
                   FROM books
                   WHERE id = 1 AND publisher_id = -1 ) AS subq
            ";

            string dbResponse = await GetDatabaseResultAsync(postgresQuery);
            using JsonDocument dbResponseJson = JsonDocument.Parse(dbResponse);
            Assert.AreEqual(dbResponseJson.RootElement.GetProperty("count").GetInt64(), 0);

            await ResetDbStateAsync();
        }

        [TestMethod]
        public async Task UpdateWithNoNewValues()
        {
            string graphQLMutationName = "editBook";
            string graphQLMutation = @"
                mutation {
                    editBook(id: 1) {
                        id
                        title
                    }
                }
            ";

            JsonDocument result = await GetGraphQLControllerResultAsync(graphQLMutation, graphQLMutationName, _graphQLController);
            Assert.IsTrue(result.RootElement.ToString().Contains(UpdateMutationHasNoUpdatesException.MESSAGE), "Error was expected");
        }
        #endregion
    }
}
