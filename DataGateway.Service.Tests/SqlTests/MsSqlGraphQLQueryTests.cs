using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Services;
using HotChocolate.Language;
using Microsoft.VisualStudio.TestTools.UnitTesting;

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

        /// <summary>
        /// Sets up test fixture for class, only to be run once per test run, as defined by
        /// MSTest decorator.
        /// </summary>
        /// <param name="context"></param>
        [ClassInitialize]
        public static async Task InitializeTestFixture(TestContext context)
        {
            await InitializeTestFixture(context, TestCategory.MSSQL);

            // Setup GraphQL Components
            //
            _graphQLService = new GraphQLService(_queryEngine, mutationEngine: null, _metadataStoreProvider, new DocumentCache(), new Sha256DocumentHashProvider());
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
                getBooks(first: 100) {
                    id
                    title
                }
            }";
            string msSqlQuery = $"SELECT id, title FROM books ORDER BY id FOR JSON PATH, INCLUDE_NULL_VALUES";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(msSqlQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        [TestMethod]
        public async Task MultipleResultQueryWithVariables()
        {
            string graphQLQueryName = "getBooks";
            string graphQLQuery = @"query ($first: Int!) {
                getBooks(first: $first) {
                    id
                    title
                }
            }";
            string msSqlQuery = $"SELECT id, title FROM books ORDER BY id FOR JSON PATH, INCLUDE_NULL_VALUES";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController, new() { { "first", 100 } });
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
                    authors(first: 100) {
                        id
                        name
                    }
                }
            }";
            string msSqlQuery = @"
                SELECT TOP 100 [table0].[id] AS [id],
                    [table0].[title] AS [title],
                    [table0].[publisher_id] AS [publisher_id],
                    JSON_QUERY([table1_subq].[data]) AS [publisher],
                    JSON_QUERY(COALESCE([table2_subq].[data], '[]')) AS [reviews],
                    JSON_QUERY(COALESCE([table3_subq].[data], '[]')) AS [authors]
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
                OUTER APPLY (
                    SELECT TOP 100 [table3].[id] AS [id],
                        [table3].[name] AS [name]
                    FROM [authors] AS [table3]
                    INNER JOIN [book_author_link] AS [table4] ON [table4].[author_id] = [table3].[id]
                    WHERE [table0].[id] = [table4].[book_id]
                    ORDER BY [id]
                    FOR JSON PATH,
                        INCLUDE_NULL_VALUES
                    ) AS [table3_subq]([data])
                WHERE 1 = 1
                ORDER BY [id]
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(msSqlQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Test One-To-One relationship both directions
        /// (book -> website placement, website placememnt -> book)
        /// <summary>
        [TestMethod]
        public async Task OneToOneJoinQuery()
        {
            string graphQLQueryName = "getBooks";
            string graphQLQuery = @"query {
                getBooks {
                    id
                    website_placement {
                        id
                        price
                        book {
                            id
                        }
                    }
                }
            }";

            string msSqlQuery = @"
                SELECT TOP 100 [table0].[id] AS [id],
                    JSON_QUERY([table1_subq].[data]) AS [website_placement]
                FROM [books] AS [table0]
                OUTER APPLY (
                    SELECT TOP 1 [table1].[id] AS [id],
                        [table1].[price] AS [price],
                        JSON_QUERY([table2_subq].[data]) AS [book]
                    FROM [book_website_placements] AS [table1]
                    OUTER APPLY (
                        SELECT TOP 1 [table2].[id] AS [id]
                        FROM [books] AS [table2]
                        WHERE [table1].[book_id] = [table2].[id]
                        ORDER BY [table2].[id]
                        FOR JSON PATH,
                            INCLUDE_NULL_VALUES,
                            WITHOUT_ARRAY_WRAPPER
                        ) AS [table2_subq]([data])
                    WHERE [table0].[id] = [table1].[book_id]
                    ORDER BY [table1].[id]
                    FOR JSON PATH,
                        INCLUDE_NULL_VALUES,
                        WITHOUT_ARRAY_WRAPPER
                    ) AS [table1_subq]([data])
                WHERE 1 = 1
                ORDER BY [table0].[id]
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES
            ";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(msSqlQuery);

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

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(msSqlQuery);

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

            string msSqlQuery = @"
                SELECT TOP 100 [table0].[title] AS [title],
                    JSON_QUERY(COALESCE([table6_subq].[data], '[]')) AS [authors]
                FROM [books] AS [table0]
                OUTER APPLY (
                    SELECT TOP 100 [table6].[name] AS [name],
                        JSON_QUERY(COALESCE([table7_subq].[data], '[]')) AS [books]
                    FROM [authors] AS [table6]
                    INNER JOIN [book_author_link] AS [table11] ON [table11].[author_id] = [table6].[id]
                    OUTER APPLY (
                        SELECT TOP 100 [table7].[title] AS [title],
                            JSON_QUERY(COALESCE([table8_subq].[data], '[]')) AS [authors]
                        FROM [books] AS [table7]
                        INNER JOIN [book_author_link] AS [table10] ON [table10].[book_id] = [table7].[id]
                        OUTER APPLY (
                            SELECT TOP 100 [table8].[name] AS [name]
                            FROM [authors] AS [table8]
                            INNER JOIN [book_author_link] AS [table9] ON [table9].[author_id] = [table8].[id]
                            WHERE [table7].[id] = [table9].[book_id]
                            ORDER BY [id]
                            FOR JSON PATH,
                                INCLUDE_NULL_VALUES
                            ) AS [table8_subq]([data])
                        WHERE [table6].[id] = [table10].[author_id]
                        ORDER BY [id]
                        FOR JSON PATH,
                            INCLUDE_NULL_VALUES
                        ) AS [table7_subq]([data])
                    WHERE [table0].[id] = [table11].[book_id]
                    ORDER BY [id]
                    FOR JSON PATH,
                        INCLUDE_NULL_VALUES
                    ) AS [table6_subq]([data])
                WHERE 1 = 1
                ORDER BY [id]
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES
            ";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(msSqlQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// This deeply nests a many-to-many join multiple times to show that
        /// it still results in a valid query.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task DeeplyNestedManyToManyJoinQueryWithVariables()
        {
            string graphQLQueryName = "getBooks";
            string graphQLQuery = @"query ($first: Int) {
              getBooks(first: $first) {
                title
                authors(first: $first) {
                  name
                  books(first: $first) {
                    title
                    authors(first: $first) {
                      name
                    }
                  }
                }
              }
            }";

            string msSqlQuery = @"
                SELECT TOP 100 [table0].[title] AS [title],
                    JSON_QUERY(COALESCE([table6_subq].[data], '[]')) AS [authors]
                FROM [books] AS [table0]
                OUTER APPLY (
                    SELECT TOP 100 [table6].[name] AS [name],
                        JSON_QUERY(COALESCE([table7_subq].[data], '[]')) AS [books]
                    FROM [authors] AS [table6]
                    INNER JOIN [book_author_link] AS [table11] ON [table11].[author_id] = [table6].[id]
                    OUTER APPLY (
                        SELECT TOP 100 [table7].[title] AS [title],
                            JSON_QUERY(COALESCE([table8_subq].[data], '[]')) AS [authors]
                        FROM [books] AS [table7]
                        INNER JOIN [book_author_link] AS [table10] ON [table10].[book_id] = [table7].[id]
                        OUTER APPLY (
                            SELECT TOP 100 [table8].[name] AS [name]
                            FROM [authors] AS [table8]
                            INNER JOIN [book_author_link] AS [table9] ON [table9].[author_id] = [table8].[id]
                            WHERE [table7].[id] = [table9].[book_id]
                            ORDER BY [id]
                            FOR JSON PATH,
                                INCLUDE_NULL_VALUES
                            ) AS [table8_subq]([data])
                        WHERE [table6].[id] = [table10].[author_id]
                        ORDER BY [id]
                        FOR JSON PATH,
                            INCLUDE_NULL_VALUES
                        ) AS [table7_subq]([data])
                    WHERE [table0].[id] = [table11].[book_id]
                    ORDER BY [id]
                    FOR JSON PATH,
                        INCLUDE_NULL_VALUES
                    ) AS [table6_subq]([data])
                WHERE 1 = 1
                ORDER BY [id]
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES
            ";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController, new() { { "first", 100 } });
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

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
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

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
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

            string msSqlQuery = @"
                SELECT TOP 1 [table0].[title] AS [title],
                    JSON_QUERY([table1_subq].[data]) AS [publisher]
                FROM [books] AS [table0]
                OUTER APPLY (
                    SELECT TOP 1 [table1].[name] AS [name],
                        JSON_QUERY(COALESCE([table2_subq].[data], '[]')) AS [books]
                    FROM [publishers] AS [table1]
                    OUTER APPLY (
                        SELECT TOP 3 [table2].[title] AS [title]
                        FROM [books] AS [table2]
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

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(msSqlQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <sumary>
        /// Test if filter and filterOData param successfully filters the query results
        /// </summary>
        [TestMethod]
        public async Task TestFilterAndFilterODataParamForListQueries()
        {
            string graphQLQueryName = "getBooks";
            string graphQLQuery = @"{
                getBooks(_filter: {id: {gte: 1} and: [{id: {lte: 4}}]}) {
                    id
                    publisher {
                        books(first: 3, _filterOData: ""id ne 2"") {
                            id
                        }
                    }
                }
            }";

            string msSqlQuery = @"
                SELECT TOP 100 [table0].[id] AS [id],
                    JSON_QUERY([table1_subq].[data]) AS [publisher]
                FROM [books] AS [table0]
                OUTER APPLY (
                    SELECT TOP 1 JSON_QUERY(COALESCE([table2_subq].[data], '[]')) AS [books]
                    FROM [publishers] AS [table1]
                    OUTER APPLY (
                        SELECT TOP 3 [table2].[id] AS [id]
                        FROM [books] AS [table2]
                        WHERE (id != 2)
                            AND [table1].[id] = [table2].[publisher_id]
                        ORDER BY [table2].[id]
                        FOR JSON PATH,
                            INCLUDE_NULL_VALUES
                        ) AS [table2_subq]([data])
                    WHERE [table0].[publisher_id] = [table1].[id]
                    ORDER BY [table1].[id]
                    FOR JSON PATH,
                        INCLUDE_NULL_VALUES,
                        WITHOUT_ARRAY_WRAPPER
                    ) AS [table1_subq]([data])
                WHERE (
                        (id >= 1)
                        AND (id <= 4)
                        )
                ORDER BY [table0].[id]
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES
            ";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(msSqlQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Get all instances of a type with nullable interger fields
        /// </summary>
        [TestMethod]
        public async Task TestQueryingTypeWithNullableIntFields()
        {
            string graphQLQueryName = "getMagazines";
            string graphQLQuery = @"{
                getMagazines{
                    id
                    title
                    issue_number
                }
            }";

            string msSqlQuery = $"SELECT TOP 100 id, title, issue_number FROM [foo].[magazines] ORDER BY id FOR JSON PATH, INCLUDE_NULL_VALUES";

            _ = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);

            _ = await GetDatabaseResultAsync(msSqlQuery);
        }

        /// <summary>
        /// Get all instances of a type with nullable string fields
        /// </summary>
        [TestMethod]
        public async Task TestQueryingTypeWithNullableStringFields()
        {
            string graphQLQueryName = "getWebsiteUsers";
            string graphQLQuery = @"{
                getWebsiteUsers{
                    id
                    username
                }
            }";

            string msSqlQuery = $"SELECT TOP 100 id, username FROM website_users ORDER BY id FOR JSON PATH, INCLUDE_NULL_VALUES";

            _ = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);

            _ = await GetDatabaseResultAsync(msSqlQuery);
        }

        /// <summary>
        /// Test to check graphQL support for aliases(arbitrarily set by user while making request).
        /// book_id and book_title are aliases used for corresponding query fields.
        /// The response for the query will contain the alias instead of raw db column.
        /// </summary>
        [TestMethod]
        public async Task TestAliasSupportForGraphQLQueryFields()
        {
            string graphQLQueryName = "getBooks";
            string graphQLQuery = @"{
                getBooks(first: 2) {
                    book_id: id
                    book_title: title
                }
            }";
            string msSqlQuery = $"SELECT TOP 2 id AS book_id, title AS book_title FROM books ORDER by id FOR JSON PATH, INCLUDE_NULL_VALUES";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(msSqlQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Test to check graphQL support for aliases(arbitrarily set by user while making request).
        /// book_id is an alias, while title is the raw db field.
        /// The response for the query will use the alias where it is provided in the query.
        /// </summary>
        [TestMethod]
        public async Task TestSupportForMixOfRawDbFieldFieldAndAlias()
        {
            string graphQLQueryName = "getBooks";
            string graphQLQuery = @"{
                getBooks(first: 2) {
                    book_id: id
                    title
                }
            }";
            string msSqlQuery = $"SELECT TOP 2 id AS book_id, title AS title FROM books ORDER by id FOR JSON PATH, INCLUDE_NULL_VALUES";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(msSqlQuery);

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
                getBooks(_filterOData: ""INVALID"") {
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
