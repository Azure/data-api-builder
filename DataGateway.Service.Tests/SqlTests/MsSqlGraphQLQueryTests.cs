using System.Collections.Generic;
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
            _graphQLService = new GraphQLService(
                _runtimeConfigPath,
                _queryEngine,
                _mutationEngine,
                graphQLMetadataProvider: null,
                new DocumentCache(),
                new Sha256DocumentHashProvider(),
                _sqlMetadataProvider);
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
            string graphQLQueryName = "books";
            string graphQLQuery = @"{
                books(first: 100) {
                    items {
                        id
                        title
                    }
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
            string graphQLQueryName = "books";
            string graphQLQuery = @"query ($first: Int!) {
                books(first: $first) {
                    items {
                        id
                        title
                    }
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
            string graphQLQueryName = "books";
            string graphQLQuery = @"{
                books(first: 100) {
                    items {
                        id
                        title
                        publisher_id
                        publishers {
                            id
                            name
                        }
                        reviews(first: 100) {
                            items {
                                id
                                content
                            }
                        }
                        authors(first: 100) {
                            items {
                                id
                                name
                            }
                        }
                    }
                }
            }";

            string expected = @"
[
  {
    ""id"": 1,
    ""title"": ""Awesome book"",
    ""publisher_id"": 1234,
    ""publishers"": {
                ""id"": 1234,
      ""name"": ""Big Company""
    },
    ""reviews"": {
                ""items"": [
                  {
                    ""id"": 567,
          ""content"": ""Indeed a great book""
                  },
        {
                    ""id"": 568,
          ""content"": ""I loved it""
        },
        {
                    ""id"": 569,
          ""content"": ""best book I read in years""
        }
      ]
    },
    ""authors"": {
                ""items"": [
                  {
                    ""id"": 123,
          ""name"": ""Jelte""
                  }
      ]
    }
        },
  {
    ""id"": 2,
    ""title"": ""Also Awesome book"",
    ""publisher_id"": 1234,
    ""publishers"": {
      ""id"": 1234,
      ""name"": ""Big Company""
    },
    ""reviews"": {
      ""items"": []
    },
    ""authors"": {
      ""items"": [
        {
          ""id"": 124,
          ""name"": ""Aniruddh""
        }
      ]
    }
  },
  {
    ""id"": 3,
    ""title"": ""Great wall of china explained"",
    ""publisher_id"": 2345,
    ""publishers"": {
        ""id"": 2345,
      ""name"": ""Small Town Publisher""
    },
    ""reviews"": {
        ""items"": []
    },
    ""authors"": {
        ""items"": [
          {
            ""id"": 123,
          ""name"": ""Jelte""
          },
        {
            ""id"": 124,
          ""name"": ""Aniruddh""
        }
      ]
    }
},
  {
    ""id"": 4,
    ""title"": ""US history in a nutshell"",
    ""publisher_id"": 2345,
    ""publishers"": {
        ""id"": 2345,
      ""name"": ""Small Town Publisher""
    },
    ""reviews"": {
        ""items"": []
    },
    ""authors"": {
        ""items"": [
          {
            ""id"": 123,
          ""name"": ""Jelte""
          },
        {
            ""id"": 124,
          ""name"": ""Aniruddh""
        }
      ]
    }
},
  {
    ""id"": 5,
    ""title"": ""Chernobyl Diaries"",
    ""publisher_id"": 2323,
    ""publishers"": {
        ""id"": 2323,
      ""name"": ""TBD Publishing One""
    },
    ""reviews"": {
        ""items"": []
    },
    ""authors"": {
        ""items"": []
    }
},
  {
    ""id"": 6,
    ""title"": ""The Palace Door"",
    ""publisher_id"": 2324,
    ""publishers"": {
        ""id"": 2324,
      ""name"": ""TBD Publishing Two Ltd""
    },
    ""reviews"": {
        ""items"": []
    },
    ""authors"": {
        ""items"": []
    }
},
  {
    ""id"": 7,
    ""title"": ""The Groovy Bar"",
    ""publisher_id"": 2324,
    ""publishers"": {
        ""id"": 2324,
      ""name"": ""TBD Publishing Two Ltd""
    },
    ""reviews"": {
        ""items"": []
    },
    ""authors"": {
        ""items"": []
    }
},
  {
    ""id"": 8,
    ""title"": ""Time to Eat"",
    ""publisher_id"": 2324,
    ""publishers"": {
        ""id"": 2324,
      ""name"": ""TBD Publishing Two Ltd""
    },
    ""reviews"": {
        ""items"": []
    },
    ""authors"": {
        ""items"": []
    }
}
]";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Test One-To-One relationship both directions
        /// (book -> website placement, website placememnt -> book)
        /// <summary>
        [TestMethod]
        public async Task OneToOneJoinQuery()
        {
            string graphQLQueryName = "books_by_pk";
            string graphQLQuery = @"query {
                books_by_pk(id: 1) {
                  id
                  websiteplacement {
                    id
                    price
                    book {
                      id
                    }
                  }
                }
            }";

            string msSqlQuery = @"
                SELECT
                  TOP 1 [table0].[id] AS [id],
                  JSON_QUERY ([table1_subq].[data]) AS [websiteplacement]
                FROM
                  [books] AS [table0]
                  OUTER APPLY (
                    SELECT
                      TOP 1 [table1].[id] AS [id],
                      [table1].[price] AS [price],
                      JSON_QUERY ([table2_subq].[data]) AS [book]
                    FROM
                      [book_website_placements] AS [table1]
                      OUTER APPLY (
                        SELECT
                          TOP 1 [table2].[id] AS [id]
                        FROM
                          [books] AS [table2]
                        WHERE
                          [table1].[book_id] = [table2].[id]
                        ORDER BY
                          [table2].[id] Asc FOR JSON PATH,
                          INCLUDE_NULL_VALUES,
                          WITHOUT_ARRAY_WRAPPER
                      ) AS [table2_subq]([data])
                    WHERE
                      [table1].[book_id] = [table0].[id]
                    ORDER BY
                      [table1].[id] Asc FOR JSON PATH,
                      INCLUDE_NULL_VALUES,
                      WITHOUT_ARRAY_WRAPPER
                  ) AS [table1_subq]([data])
                WHERE
                  [table0].[id] = 1
                ORDER BY
                  [table0].[id] Asc FOR JSON PATH,
                  INCLUDE_NULL_VALUES,
                  WITHOUT_ARRAY_WRAPPER";

            string actual = await base.GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
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
            string graphQLQueryName = "books";
            string graphQLQuery = @"{
              books(first: 100) {
                items {
                  title
                  publishers {
                    name
                    books(first: 100) {
                      items {
                        title
                        publishers {
                          name
                          books(first: 100) {
                            items {
                              title
                              publishers {
                                name
                              }
                            }
                          }
                        }
                      }
                    }
                  }
                }
              }
            }";

            // Too big of a result to check for the exact contents.
            // For correctness of results, we use different tests.
            // This test is only to validate we can handle deeply nested graphql queries.
            await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
        }

        /// <summary>
        /// This deeply nests a many-to-many join multiple times to show that
        /// it still results in a valid query.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task DeeplyNestedManyToManyJoinQuery()
        {
            string graphQLQueryName = "books";
            string graphQLQuery = @"
{
    books(first: 100) {
        items {
            title
            authors(first: 100) {
                items {
                    name
                    books(first: 100) {
                        items {
                            title
                            authors(first: 100) {
                               items {
                                    name
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}";

            await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
        }

        /// <summary>
        /// This deeply nests a many-to-many join multiple times to show that
        /// it still results in a valid query.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task DeeplyNestedManyToManyJoinQueryWithVariables()
        {
            string graphQLQueryName = "books";
            string graphQLQuery = @"
            query ($first: Int) {
                books(first: $first) {
                    items {
                        title
                        authors(first: $first) {
                            items {
                                name
                                books(first: $first) {
                                    items {
                                        title
                                        authors(first: $first) {
                                          items {
                                            name
                                          }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }";

            await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController,
                new() { { "first", 100 } });

        }

        [TestMethod]
        public async Task QueryWithSingleColumnPrimaryKey()
        {
            string graphQLQueryName = "books_by_pk";
            string graphQLQuery = @"{
                books_by_pk(id: 2) {
                    title
                }
            }";
            string msSqlQuery = @"
                SELECT title FROM books
                WHERE id = 2 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER
            ";

            string actual = await base.GetGraphQLResultAsync(
                graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(msSqlQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        [TestMethod]
        public async Task QueryWithMultileColumnPrimaryKey()
        {
            string graphQLQueryName = "reviews_by_pk";
            string graphQLQuery = @"{
                reviews_by_pk(id: 568, book_id: 1) {
                    content
                }
            }";
            string msSqlQuery = @"
                SELECT TOP 1 content FROM reviews
                WHERE id = 568 AND book_id = 1 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER
            ";

            string actual = await base.GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(msSqlQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        [TestMethod]
        public async Task QueryWithNullResult()
        {
            string graphQLQueryName = "books_by_pk";
            string graphQLQuery = @"{
                books_by_pk(id: -9999) {
                    title
                }
            }";

            string actual = await base.GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);

            SqlTestHelper.PerformTestEqualJsonStrings("null", actual);
        }

        /// <sumary>
        /// Test if first param successfully limits list quries
        /// </summary>
        [TestMethod]
        public async Task TestFirstParamForListQueries()
        {
            string graphQLQueryName = "books";
            string graphQLQuery = @"{
                books(first: 1) {
                    items {
                        title
                        publishers {
                            name
                            books(first: 3) {
                                items {
                                    title
                                }
                            }
                        }
                    }
                }
            }";

            string expected = @"
[
  {
    ""title"": ""Awesome book"",
    ""publishers"": {
                ""name"": ""Big Company"",
      ""books"": {
                    ""items"": [
                      {
                        ""title"": ""Awesome book""
                      },
          {
                        ""title"": ""Also Awesome book""
          }
        ]
      }
            }
        }
]";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <sumary>
        /// Test if filter and filterOData param successfully filters the query results
        /// </summary>
        [TestMethod]
        public async Task TestFilterAndFilterODataParamForListQueries()
        {
            string graphQLQueryName = "books";
            string graphQLQuery = @"{
                books(_filter: {id: {gte: 1} and: [{id: {lte: 4}}]}) {
                    items {
                        id
                        publishers {
                            books(first: 3, _filterOData: ""id ne 2"") {
                                items {
                                    id
                                }
                            }
                        }
                    }
                }
            }";

            string expected = @"
[
  {
    ""id"": 1,
    ""publishers"": {
                ""books"": {
                    ""items"": [
                      {
                        ""id"": 1
                      }
        ]
      }
            }
        },
  {
    ""id"": 2,
    ""publishers"": {
      ""books"": {
        ""items"": [
          {
            ""id"": 1
          }
        ]
      }
    }
  },
  {
    ""id"": 3,
    ""publishers"": {
        ""books"": {
            ""items"": [
              {
                ""id"": 3
              },
          {
                ""id"": 4
          }
        ]
      }
    }
},
  {
    ""id"": 4,
    ""publishers"": {
        ""books"": {
            ""items"": [
              {
                ""id"": 3
              },
          {
                ""id"": 4
          }
        ]
      }
    }
}
]";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Get all instances of a type with nullable interger fields
        /// </summary>
        [TestMethod]
        public async Task TestQueryingTypeWithNullableIntFields()
        {
            string graphQLQueryName = "magazines";
            string graphQLQuery = @"{
                magazines {
                    items {
                        id
                        title
                        issue_number
                    }
                }
            }";

            string msSqlQuery = $"SELECT TOP 100 id, title, issue_number FROM magazines ORDER BY id FOR JSON PATH, INCLUDE_NULL_VALUES";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(msSqlQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Get all instances of a type with nullable string fields
        /// </summary>
        [TestMethod]
        public async Task TestQueryingTypeWithNullableStringFields()
        {
            string graphQLQueryName = "website_users";
            string graphQLQuery = @"{
                website_users {
                    items {
                        id
                        username
                    }
                }
            }";

            string msSqlQuery = $"SELECT TOP 100 id, username FROM website_users ORDER BY id FOR JSON PATH, INCLUDE_NULL_VALUES";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(msSqlQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Test to check graphQL support for aliases(arbitrarily set by user while making request).
        /// book_id and book_title are aliases used for corresponding query fields.
        /// The response for the query will contain the alias instead of raw db column.
        /// </summary>
        [TestMethod]
        public async Task TestAliasSupportForGraphQLQueryFields()
        {
            string graphQLQueryName = "books";
            string graphQLQuery = @"{
                books(first: 2) {
                    items {
                        book_id: id
                        book_title: title
                    }
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
            string graphQLQueryName = "books";
            string graphQLQuery = @"{
                books(first: 2) {
                    items {
                        book_id: id
                        title
                    }
                }
            }";
            string msSqlQuery = $"SELECT TOP 2 id AS book_id, title AS title FROM books ORDER by id FOR JSON PATH, INCLUDE_NULL_VALUES";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(msSqlQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Tests orderBy on a list query
        /// </summary>
        [Ignore]
        [TestMethod]
        public async Task TestOrderByInListQuery()
        {
            string graphQLQueryName = "getBooks";
            string graphQLQuery = @"{
                getBooks(first: 100 orderBy: {title: Desc}) {
                    id
                    title
                }
            }";
            string msSqlQuery = $"SELECT TOP 100 id, title FROM books ORDER BY title DESC, id ASC FOR JSON PATH, INCLUDE_NULL_VALUES";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(msSqlQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Use multiple order options and order an entity with a composite pk
        /// </summary>
        [Ignore]
        [TestMethod]
        public async Task TestOrderByInListQueryOnCompPkType()
        {
            string graphQLQueryName = "getReviews";
            string graphQLQuery = @"{
                getReviews(orderBy: {content: Asc id: Desc}) {
                    id
                    content
                }
            }";
            string msSqlQuery = $"SELECT TOP 100 id, content FROM reviews ORDER BY content ASC, id DESC, book_id ASC FOR JSON PATH, INCLUDE_NULL_VALUES";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(msSqlQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Tests null fields in orderBy are ignored
        /// meaning that null pk columns are included in the ORDER BY clause
        /// as ASC by default while null non-pk columns are completely ignored
        /// </summary>
        [Ignore]
        [TestMethod]
        public async Task TestNullFieldsInOrderByAreIgnored()
        {
            string graphQLQueryName = "getBooks";
            string graphQLQuery = @"{
                getBooks(first: 100 orderBy: {title: Desc id: null publisher_id: null}) {
                    id
                    title
                }
            }";
            string msSqlQuery = $"SELECT TOP 100 id, title FROM books ORDER BY title DESC, id ASC FOR JSON PATH, INCLUDE_NULL_VALUES";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(msSqlQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Tests that an orderBy with only null fields results in default pk sorting
        /// </summary>
        [Ignore]
        [TestMethod]
        public async Task TestOrderByWithOnlyNullFieldsDefaultsToPkSorting()
        {
            string graphQLQueryName = "books";
            string graphQLQuery = @"{
                books(first: 100 orderBy: {title: null}) {
                    items {
                        id
                        title
                    }
                }
            }";
            string msSqlQuery = $"SELECT TOP 100 id, title FROM books ORDER BY id ASC FOR JSON PATH, INCLUDE_NULL_VALUES";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(msSqlQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        #endregion

        #region Negative Tests

        [TestMethod]
        public async Task TestInvalidFirstParamQuery()
        {
            string graphQLQueryName = "books";
            string graphQLQuery = @"{
                books(first: -1) {
                    items {
                        id
                        title
                    }
                }
            }";

            JsonElement result = await GetGraphQLControllerResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            SqlTestHelper.TestForErrorInGraphQLResponse(result.ToString(), statusCode: $"{DataGatewayException.SubStatusCodes.BadRequest}");
        }

        [TestMethod]
        public async Task TestInvalidFilterParamQuery()
        {
            string graphQLQueryName = "books";
            string graphQLQuery = @"{
                books(_filterOData: ""INVALID"") {
                    items {
                        id
                        title
                    }
                }
            }";

            JsonElement result = await GetGraphQLControllerResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            SqlTestHelper.TestForErrorInGraphQLResponse(result.ToString(), statusCode: $"{DataGatewayException.SubStatusCodes.BadRequest}");
        }

        #endregion

        protected override async Task<string> GetGraphQLResultAsync(
            string graphQLQuery, string graphQLQueryName,
            GraphQLController graphQLController,
            Dictionary<string, object> variables = null,
            bool failOnErrors = true)
        {
            string dataResult = await base.GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, graphQLController, variables, failOnErrors);

            return JsonDocument.Parse(dataResult).RootElement.GetProperty("items").ToString();
        }
    }
}
