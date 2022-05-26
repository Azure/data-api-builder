using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.GraphQLBuilder.Queries;
using Azure.DataGateway.Service.Resolvers;
using Azure.DataGateway.Service.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests
{
    /// <summary>
    /// Used to store shared hard coded expected results and tests for pagination queries between
    /// MsSql and Postgres
    /// </summary>
    [TestClass]
    public abstract class GraphQLPaginationTestBase : SqlTestBase
    {
        #region Test Fixture Setup
        protected static GraphQLService _graphQLService;
        protected static GraphQLController _graphQLController;
        protected static readonly string _integrationTableName = "books";

        #endregion

        #region Tests

        /// <summary>
        /// Request a full connection object {items, after, hasNextPage}
        /// </summary>
        [TestMethod]
        public async Task RequestFullConnection()
        {
            string graphQLQueryName = "books";
            string after = SqlPaginationUtil.Base64Encode("[{\"Value\":1,\"Direction\":0,\"ColumnName\":\"id\"}]");
            string graphQLQuery = @"{
                books(first: 2," + $"after: \"{after}\")" + @"{
                    items {
                        title
                        publishers {
                            name
                        }
                    }
                    endCursor
                    hasNextPage
                }
            }";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = @"{
              ""items"": [
                {
                  ""title"": ""Also Awesome book"",
                  ""publishers"": {
                    ""name"": ""Big Company""
                  }
                },
                {
                  ""title"": ""Great wall of china explained"",
                  ""publishers"": {
                    ""name"": ""Small Town Publisher""
                  }
                }
              ],
              ""endCursor"": """ + SqlPaginationUtil.Base64Encode("[{\"Value\":3,\"Direction\":0,\"TableSchema\":\"\",\"TableName\":\"\",\"ColumnName\":\"id\"}]") + @""",
              ""hasNextPage"": true
            }";

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Request a full connection object {items, after, hasNextPage}
        /// without providing any parameters
        /// </summary>
        [TestMethod]
        public async Task RequestNoParamFullConnection()
        {
            string graphQLQueryName = "books";
            string graphQLQuery = @"{
                books {
                    items {
                        id
                        title
                    }
                    endCursor
                    hasNextPage
                }
            }";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = @"{
              ""items"": [
                {
                  ""id"": 1,
                  ""title"": ""Awesome book""
                },
                {
                  ""id"": 2,
                  ""title"": ""Also Awesome book""
                },
                {
                  ""id"": 3,
                  ""title"": ""Great wall of china explained""
                },
                {
                  ""id"": 4,
                  ""title"": ""US history in a nutshell""
                },
                {
                  ""id"": 5,
                  ""title"": ""Chernobyl Diaries""
                },
                {
                  ""id"": 6,
                  ""title"": ""The Palace Door""
                },
                {
                  ""id"": 7,
                  ""title"": ""The Groovy Bar""
                },
                {
                  ""id"": 8,
                  ""title"": ""Time to Eat""
                }
              ],
              ""endCursor"": """ + SqlPaginationUtil.Base64Encode("[{\"Value\":8,\"Direction\":0,\"TableSchema\":\"\",\"TableName\":\"\",\"ColumnName\":\"id\"}]") + @""",
              ""hasNextPage"": false
            }";

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Request only items from the pagination
        /// </summary>
        [TestMethod]
        public async Task RequestItemsOnly()
        {
            string graphQLQueryName = "books";
            string after = SqlPaginationUtil.Base64Encode("[{\"Value\":1,\"Direction\":0,\"ColumnName\":\"id\"}]");
            string graphQLQuery = @"{
                books(first: 2," + $"after: \"{after}\")" + @"{
                    items {
                        title
                        publisher_id
                    }
                }
            }";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = @"{
              ""items"": [
                {
                  ""title"": ""Also Awesome book"",
                  ""publisher_id"": 1234
                },
                {
                  ""title"": ""Great wall of china explained"",
                  ""publisher_id"": 2345
                }
              ]
            }";

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Request only after from the pagination
        /// </summary>
        /// <remarks>
        /// This is probably not a common use case, but it is necessary to test graphql's capabilites to only
        /// selectively retreive data
        /// </remarks>
        [TestMethod]
        public async Task RequestAfterTokenOnly()
        {
            string graphQLQueryName = "books";
            string after = SqlPaginationUtil.Base64Encode("[{\"Value\":1,\"Direction\":0,\"ColumnName\":\"id\"}]");
            string graphQLQuery = @"{
                books(first: 2," + $"after: \"{after}\")" + @"{
                    endCursor
                }
            }";

            JsonElement root = await GetGraphQLControllerResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            root = root.GetProperty("data").GetProperty(graphQLQueryName);
            string actual = SqlPaginationUtil.Base64Decode(root.GetProperty(QueryBuilder.PAGINATION_TOKEN_FIELD_NAME).GetString());
            string expected = "[{\"Value\":3,\"Direction\":0, \"TableSchema\":\"\",\"TableName\":\"\", \"ColumnName\":\"id\"}]";

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Request only hasNextPage from the pagination
        /// </summary>
        /// <remarks>
        /// This is probably not a common use case, but it is necessary to test graphql's capabilites to only
        /// selectively retreive data
        /// </remarks>
        [TestMethod]
        public async Task RequestHasNextPageOnly()
        {
            string graphQLQueryName = "books";
            string after = SqlPaginationUtil.Base64Encode("[{\"Value\":1,\"Direction\":0,\"ColumnName\":\"id\"}]");
            string graphQLQuery = @"{
                books(first: 2," + $"after: \"{after}\")" + @"{
                    hasNextPage
                }
            }";

            JsonElement root = await GetGraphQLControllerResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            root = root.GetProperty("data").GetProperty(graphQLQueryName);
            bool actual = root.GetProperty(QueryBuilder.HAS_NEXT_PAGE_FIELD_NAME).GetBoolean();

            Assert.AreEqual(true, actual);
        }

        /// <summary>
        /// Request an empty page
        /// </summary>
        [TestMethod]
        public async Task RequestEmptyPage()
        {
            string graphQLQueryName = "books";
            string after = SqlPaginationUtil.Base64Encode("[{\"Value\":1000000,\"Direction\":0,\"TableSchema\":\"\",\"TableName\":\"\",\"ColumnName\":\"id\"}]");
            string graphQLQuery = @"{
                 books(first: 2," + $"after: \"{after}\")" + @"{
                    items {
                        title
                    }
                    endCursor
                    hasNextPage
                }
            }";

            JsonElement root = await GetGraphQLControllerResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            root = root.GetProperty("data").GetProperty(graphQLQueryName);

            SqlTestHelper.PerformTestEqualJsonStrings(expected: "[]", root.GetProperty("items").ToString());
            Assert.AreEqual(null, root.GetProperty(QueryBuilder.PAGINATION_TOKEN_FIELD_NAME).GetString());
            Assert.AreEqual(false, root.GetProperty(QueryBuilder.HAS_NEXT_PAGE_FIELD_NAME).GetBoolean());
        }

        /// <summary>
        /// Request nested pagination queries
        /// </summary>
        [TestMethod]
        public async Task RequestNestedPaginationQueries()
        {
            string graphQLQueryName = "books";
            string after = SqlPaginationUtil.Base64Encode("[{\"Value\":1,\"Direction\":0,\"TableSchema\":\"\",\"TableName\":\"\",\"ColumnName\":\"id\"}]");
            string graphQLQuery = @"{
                 books(first: 2," + $"after: \"{after}\")" + @"{
                    items {
                        title
                        publishers {
                            name
                            books(first: 2, after:""" + after + @"""){
                                items {
                                    id
                                    title
                                }
                                endCursor
                                hasNextPage
                            }
                        }
                    }
                    endCursor
                    hasNextPage
                }
            }";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = @"{
              ""items"": [
                {
                  ""title"": ""Also Awesome book"",
                  ""publishers"": {
                    ""name"": ""Big Company"",
                    ""books"": {
                      ""items"": [
                        {
                          ""id"": 2,
                          ""title"": ""Also Awesome book""
                        }
                      ],
                      ""endCursor"": """ + SqlPaginationUtil.Base64Encode("[{\"Value\":2,\"Direction\":0,\"TableSchema\":\"\",\"TableName\":\"\",\"ColumnName\":\"id\"}]") + @""",
                      ""hasNextPage"": false
                    }
                  }
                },
                {
                  ""title"": ""Great wall of china explained"",
                  ""publishers"": {
                    ""name"": ""Small Town Publisher"",
                    ""books"": {
                      ""items"": [
                        {
                          ""id"": 3,
                          ""title"": ""Great wall of china explained""
                        },
                        {
                          ""id"": 4,
                          ""title"": ""US history in a nutshell""
                        }
                      ],
                      ""endCursor"": """ + SqlPaginationUtil.Base64Encode("[{\"Value\":4,\"Direction\":0,\"TableSchema\":\"\",\"TableName\":\"\",\"ColumnName\":\"id\"}]") + @""",
                      ""hasNextPage"": false
                    }
                  }
                }
              ],
              ""endCursor"": """ + SqlPaginationUtil.Base64Encode("[{\"Value\":3,\"Direction\":0,\"TableSchema\":\"\",\"TableName\":\"\",\"ColumnName\":\"id\"}]") + @""",
              ""hasNextPage"": true
            }";

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Does a paginated query as a subquery of a mutation result
        /// </summary>
        [TestMethod]
        public async Task RequestPaginatedQueryFromMutationResult()
        {
            string graphQLMutationName = "createBook";
            string after = SqlPaginationUtil.Base64Encode("[{\"Value\":1,\"Direction\":0,\"TableSchema\":\"\",\"TableName\":\"\",\"ColumnName\":\"id\"}]");
            string graphQLMutation = @"
                mutation {
                    createBook(item: { title: ""Books, Pages, and Pagination. The Book"", publisher_id: 1234 }) {
                        publishers {
                            books(first: 2, after: """ + after + @""") {
                                items {
                                    id
                                    title
                                }
                                endCursor
                                hasNextPage
                            }
                        }
                    }
                }
            ";
            string actual = await GetGraphQLResultAsync(graphQLMutation, graphQLMutationName, _graphQLController);
            string expected = @"{
              ""publishers"": {
                ""books"": {
                  ""items"": [
                    {
                      ""id"": 2,
                      ""title"": ""Also Awesome book""
                    },
                    {
                      ""id"": 5001,
                      ""title"": ""Books, Pages, and Pagination. The Book""
                    }
                  ],
                  ""endCursor"": """ + SqlPaginationUtil.Base64Encode("[{\"Value\":5001,\"Direction\":0,\"TableSchema\":\"\",\"TableName\":\"\",\"ColumnName\":\"id\"}]") + @""",
                  ""hasNextPage"": false
                }
              }
            }";

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);

            // reset database after mutation
            await ResetDbStateAsync();
        }

        /// <summary>
        /// Request deeply nested pagination queries
        /// </summary>
        [TestMethod]
        public async Task RequestDeeplyNestedPaginationQueries()
        {
            string graphQLQueryName = "books";
            string graphQLQuery = @"{
                books(first: 2){
                    items {
                        id
                        authors(first: 2) {
                            items {
                                name
                                books(first: 2) {
                                    items {
                                        id
                                        title
                                        reviews(first: 2) {
                                            items {
                                                id
                                                books {
                                                    id
                                                }
                                            content
                                            }
                                            endCursor
                                            hasNextPage
                                        }
                                    }
                                    hasNextPage
                                    endCursor
                                }
                            }
                        }
                    }
                    hasNextPage
                    endCursor
                }
            }";

            string after = "[{\"Value\":1,\"Direction\":0,\"TableSchema\":\"\",\"TableName\":\"\",\"ColumnName\":\"book_id\"}," +
                            "{\"Value\":568,\"Direction\":0,\"TableSchema\":\"\",\"TableName\":\"\",\"ColumnName\":\"id\"}]";
            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = @"
{
  ""items"": [
    {
      ""id"": 1,
      ""authors"": {
        ""items"": [
          {
            ""name"": ""Jelte"",
            ""books"": {
              ""items"": [
                {
                  ""id"": 1,
                  ""title"": ""Awesome book"",
                  ""reviews"": {
                    ""items"": [
                      {
                        ""id"": 567,
                        ""books"": {
                          ""id"": 1
                        },
                        ""content"": ""Indeed a great book""
                      },
                      {
                        ""id"": 568,
                        ""books"": {
                          ""id"": 1
                        },
                        ""content"": ""I loved it""
                      }
                    ],
                    ""endCursor"":  """ + SqlPaginationUtil.Base64Encode(after) + @""",
                    ""hasNextPage"": true
                  }
                },
                {
                  ""id"": 3,
                  ""title"": ""Great wall of china explained"",
                  ""reviews"": {
                    ""items"": [],
                    ""endCursor"": null,
                    ""hasNextPage"": false
                  }
                }
              ],
              ""hasNextPage"": true,
              ""endCursor"": """ + SqlPaginationUtil.Base64Encode("[{\"Value\":3,\"Direction\":0,\"TableSchema\":\"\",\"TableName\":\"\",\"ColumnName\":\"id\"}]") + @"""
            }
          }
        ]
      }
    },
    {
      ""id"": 2,
      ""authors"": {
        ""items"": [
          {
            ""name"": ""Aniruddh"",
            ""books"": {
              ""items"": [
                {
                  ""id"": 2,
                  ""title"": ""Also Awesome book"",
                  ""reviews"": {
                    ""items"": [],
                    ""endCursor"": null,
                    ""hasNextPage"": false
                  }
                },
                {
                  ""id"": 3,
                  ""title"": ""Great wall of china explained"",
                  ""reviews"": {
                    ""items"": [],
                    ""endCursor"": null,
                    ""hasNextPage"": false
                  }
                }
              ],
              ""hasNextPage"": true,
              ""endCursor"": """ + SqlPaginationUtil.Base64Encode("[{\"Value\":3,\"Direction\":0,\"TableSchema\":\"\",\"TableName\":\"\",\"ColumnName\":\"id\"}]") + @"""
            }
          }
        ]
      }
    }
  ],
  ""hasNextPage"": true,
  ""endCursor"": """ + SqlPaginationUtil.Base64Encode("[{\"Value\":2,\"Direction\":0,\"TableSchema\":\"\",\"TableName\":\"\",\"ColumnName\":\"id\"}]") + @"""
}";

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Do pagination on a table with a primary key with multiple columns
        /// </summary>
        [TestMethod]
        public async Task PaginateCompositePkTable()
        {
            string graphQLQueryName = "reviews";
            string after = SqlPaginationUtil.Base64Encode("[{\"Value\":1,\"Direction\":0,\"TableSchema\":\"\",\"TableName\":\"\",\"ColumnName\":\"book_id\"}," +
                                                           "{\"Value\":567,\"Direction\":0,\"TableSchema\":\"\",\"TableName\":\"\",\"ColumnName\":\"id\"}]");
            string graphQLQuery = @"{
                reviews(first: 2, after: """ + after + @""") {
                    items {
                        id
                        content
                    }
                    hasNextPage
                    endCursor
                }
            }";

            after = SqlPaginationUtil.Base64Encode("[{\"Value\":1,\"Direction\":0,\"TableSchema\":\"\",\"TableName\":\"\",\"ColumnName\":\"book_id\"}," +
                                                    "{\"Value\":569,\"Direction\":0,\"TableSchema\":\"\",\"TableName\":\"\",\"ColumnName\":\"id\"}]");
            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = @"{
              ""items"": [
                {
                  ""id"": 568,
                  ""content"": ""I loved it""
                },
                {
                  ""id"": 569,
                  ""content"": ""best book I read in years""
                }
              ],
              ""hasNextPage"": false,
              ""endCursor"": """ + after + @"""
            }";

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Restrict the pagination result using the _filter argument
        /// </summary>
        [TestMethod]
        public async Task PaginationWithFilterArgument()
        {
            string graphQLQueryName = "books";
            string after = SqlPaginationUtil.Base64Encode("[{\"Value\":1,\"Direction\":0,\"ColumnName\":\"id\"}]");
            string graphQLQuery = @"{
                books(first: 2, after: """ + after + @""", _filter: {publisher_id: {eq: 2345}}) {
                    items {
                        id
                        publisher_id
                    }
                    endCursor
                    hasNextPage
                }
            }";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = @"{
              ""items"": [
                {
                  ""id"": 3,
                  ""publisher_id"": 2345
                },
                {
                  ""id"": 4,
                  ""publisher_id"": 2345
                }
              ],
              ""endCursor"": """ + SqlPaginationUtil.Base64Encode("[{\"Value\":4,\"Direction\":0,\"TableSchema\":\"\",\"TableName\":\"\",\"ColumnName\":\"id\"}]") + @""",
              ""hasNextPage"": false
            }";

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Test paginating while ordering by a subset of columns of a composite pk
        /// </summary>
        [Ignore]
        [TestMethod]
        public async Task TestPaginationWithOrderByWithPartialPk()
        {
            string graphQLQueryName = "stocks";
            string graphQLQuery = @"{
                stocks(first: 2 orderBy: {pieceid: Desc}) {
                    items {
                        pieceid
                        categoryid
                    }
                    endCursor
                    hasNextPage
                }
            }";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = @"{
              ""items"": [
                {
                  ""pieceid"": 99,
                  ""categoryid"": 100
                },
                {
                  ""pieceid"": 1,
                  ""categoryid"": 0
                }
              ],
              ""endCursor"": """ + SqlPaginationUtil.Base64Encode(
                  "[{\"Value\":1,\"Direction\":1,\"TableSchema\":\"\",\"TableName\":\"\",\"ColumnName\":\"pieceid\"}," +
                  "{\"Value\":1,\"Direction\":0,\"TableSchema\":\"\",\"TableName\":\"\",\"ColumnName\":\"categoryid\"}]") + @""",
              ""hasNextPage"": true
            }";

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Paginate first two entries then paginate again with the returned after token.
        /// Verify both pagination query results
        /// </summary>
        [Ignore]
        [TestMethod]
        public async Task TestCallingPaginationTwiceWithOrderBy()
        {
            string graphQLQueryName = "books";
            string graphQLQuery1 = @"{
                books(first: 2 orderBy: {title: Desc publisher_id: Asc id: Desc}) {
                    items {
                        id
                        title
                        publisher_id
                    }
                    endCursor
                    hasNextPage
                }
            }";

            string actual1 = await GetGraphQLResultAsync(graphQLQuery1, graphQLQueryName, _graphQLController);

            string expectedAfter1 = SqlPaginationUtil.Base64Encode(
                  "[{\"Value\":\"Time to Eat\",\"Direction\":1,\"TableSchema\":\"\",\"TableName\":\"\",\"ColumnName\":\"title\"}," +
                  "{\"Value\":2324,\"Direction\":0,\"TableSchema\":\"\",\"TableName\":\"\",\"ColumnName\":\"publisher_id\"}," +
                  "{\"Value\":8,\"Direction\":1,\"TableSchema\":\"\",\"TableName\":\"\",\"ColumnName\":\"id\"}]");

            string expected1 = @"{
              ""items"": [
                {
                  ""id"": 4,
                  ""title"": ""US history in a nutshell"",
                  ""publisher_id"": 2345
                },
                {
                  ""id"": 8,
                  ""title"": ""Time to Eat"",
                  ""publisher_id"": 2324
                }
              ],
              ""endCursor"": """ + expectedAfter1 + @""",
              ""hasNextPage"": true
            }";

            SqlTestHelper.PerformTestEqualJsonStrings(expected1, actual1);

            string graphQLQuery2 = @"{
                books(first: 2, after: """ + expectedAfter1 + @""" orderBy: {title: Desc publisher_id: Asc id: Desc}) {
                    items {
                        id
                        title
                        publisher_id
                    }
                    endCursor
                    hasNextPage
                }
            }";

            string actual2 = await GetGraphQLResultAsync(graphQLQuery2, graphQLQueryName, _graphQLController);

            string expectedAfter2 = SqlPaginationUtil.Base64Encode(
                  "[{\"Value\":\"The Groovy Bar\",\"Direction\":1,\"TableSchema\":\"\",\"TableName\":\"\",\"ColumnName\":\"title\"}," +
                  "{\"Value\":2324,\"Direction\":0,\"TableSchema\":\"\",\"TableName\":\"\",\"ColumnName\":\"publisher_id\"}," +
                  "{\"Value\":7,\"Direction\":1,\"TableSchema\":\"\",\"TableName\":\"\",\"ColumnName\":\"id\"}]");

            string expected2 = @"{
              ""items"": [
                {
                  ""id"": 6,
                  ""title"": ""The Palace Door"",
                  ""publisher_id"": 2324
                },
                {
                  ""id"": 7,
                  ""title"": ""The Groovy Bar"",
                  ""publisher_id"": 2324
                }
              ],
              ""endCursor"": """ + expectedAfter2 + @""",
              ""hasNextPage"": true
            }";

            SqlTestHelper.PerformTestEqualJsonStrings(expected2, actual2);
        }

        /// <summary>
        /// Paginate ordering with a column for which multiple entries
        /// have the same value, and check that the column tie break is resolved properly
        /// </summary>
        [Ignore]
        [TestMethod]
        public async Task TestColumnTieBreak()
        {
            string graphQLQueryName = "books";
            string graphQLQuery1 = @"{
                books(first: 4 orderBy: {publisher_id: Desc}) {
                    items {
                        id
                        publisher_id
                    }
                    endCursor
                    hasNextPage
                }
            }";

            string actual1 = await GetGraphQLResultAsync(graphQLQuery1, graphQLQueryName, _graphQLController);

            string expectedAfter1 = SqlPaginationUtil.Base64Encode(
                  "[{\"Value\":2324,\"Direction\":1,\"TableSchema\":\"\",\"TableName\":\"\",\"ColumnName\":\"publisher_id\"}," +
                  "{\"Value\":7,\"Direction\":0,\"TableSchema\":\"\",\"TableName\":\"\",\"ColumnName\":\"id\"}]");

            string expected1 = @"{
              ""items"": [
                {
                  ""id"": 3,
                  ""publisher_id"": 2345
                },
                {
                  ""id"": 4,
                  ""publisher_id"": 2345
                },
                {
                  ""id"": 6,
                  ""publisher_id"": 2324
                },
                {
                  ""id"": 7,
                  ""publisher_id"": 2324
                }
              ],
              ""endCursor"": """ + expectedAfter1 + @""",
              ""hasNextPage"": true
            }";

            SqlTestHelper.PerformTestEqualJsonStrings(expected1, actual1);

            string graphQLQuery2 = @"{
                books(first: 2, after: """ + expectedAfter1 + @""" orderBy: {publisher_id: Desc}) {
                    items {
                        id
                        publisher_id
                    }
                    endCursor
                    hasNextPage
                }
            }";

            string actual2 = await GetGraphQLResultAsync(graphQLQuery2, graphQLQueryName, _graphQLController);

            string expectedAfter2 = SqlPaginationUtil.Base64Encode(
                  "[{\"Value\":2323,\"Direction\":1,\"TableSchema\":\"\",\"TableName\":\"\",\"ColumnName\":\"publisher_id\"}," +
                  "{\"Value\":5,\"Direction\":0,\"TableSchema\":\"\",\"TableName\":\"\",\"ColumnName\":\"id\"}]");

            string expected2 = @"{
              ""items"": [
                {
                  ""id"": 8,
                  ""publisher_id"": 2324
                },
                {
                  ""id"": 5,
                  ""publisher_id"": 2323
                }
              ],
              ""endCursor"": """ + expectedAfter2 + @""",
              ""hasNextPage"": true
            }";

            SqlTestHelper.PerformTestEqualJsonStrings(expected2, actual2);
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
                    items {
                        id
                    }
                }
            }";

            JsonElement result = await GetGraphQLControllerResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            SqlTestHelper.TestForErrorInGraphQLResponse(result.ToString(), statusCode: $"{DataGatewayException.SubStatusCodes.BadRequest}");
        }

        /// <summary>
        /// Request zero entries for a pagination page
        /// </summary>
        [TestMethod]
        public async Task RequestInvalidZeroFirst()
        {
            string graphQLQueryName = "books";
            string graphQLQuery = @"{
                books(first: 0) {
                    items {
                        id
                    }
                }
            }";

            JsonElement result = await GetGraphQLControllerResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            SqlTestHelper.TestForErrorInGraphQLResponse(result.ToString(), statusCode: $"{DataGatewayException.SubStatusCodes.BadRequest}");
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
                    items {
                        id
                    }
                }
            }";

            JsonElement result = await GetGraphQLControllerResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            SqlTestHelper.TestForErrorInGraphQLResponse(result.ToString(), statusCode: $"{DataGatewayException.SubStatusCodes.BadRequest}");
        }

        /// <summary>
        /// Supply a null after parameter
        /// </summary>
        [TestMethod]
        public async Task RequestInvalidAfterNull()
        {
            string graphQLQueryName = "books";
            string graphQLQuery = @"{
                books(after: ""null"") {
                    items {
                        id
                    }
                }
            }";

            JsonElement result = await GetGraphQLControllerResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            SqlTestHelper.TestForErrorInGraphQLResponse(result.ToString(), statusCode: $"{DataGatewayException.SubStatusCodes.BadRequest}");
        }

        /// <summary>
        /// Supply an invalid key to the after JSON
        /// </summary>
        [TestMethod]
        public async Task RequestInvalidAfterWithIncorrectKeys()
        {
            string graphQLQueryName = "books";
            string after = SqlPaginationUtil.Base64Encode("[{\"Value\":\"Great Book\",\"Direction\":0,\"ColumnName\":\"title\"}]");
            string graphQLQuery = @"{
                 books(" + $"after: \"{after}\")" + @"{
                    items {
                        title
                    }
                }
            }";

            JsonElement result = await GetGraphQLControllerResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            SqlTestHelper.TestForErrorInGraphQLResponse(result.ToString(), statusCode: $"{DataGatewayException.SubStatusCodes.BadRequest}");
        }

        /// <summary>
        /// Supply an invalid type to the key in the after JSON
        /// </summary>
        [TestMethod]
        public async Task RequestInvalidAfterWithIncorrectType()
        {
            string graphQLQueryName = "books";
            // note that the current implementation will accept "2" as
            // a valid value for id since it can be parsed to an int
            string after = SqlPaginationUtil.Base64Encode("[{\"Value\":\"two\",\"Direction\":0,\"ColumnName\":\"id\"}]");
            string graphQLQuery = @"{
                 books(" + $"after: \"{after}\")" + @"{
                    items {
                        title
                    }
                }
            }";

            JsonElement result = await GetGraphQLControllerResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            SqlTestHelper.TestForErrorInGraphQLResponse(result.ToString(), statusCode: $"{DataGatewayException.SubStatusCodes.BadRequest}");
        }

        /// <summary>
        /// Test with after which does not include all orderBy columns
        /// </summary>
        [Ignore]
        [TestMethod]
        public async Task RequestInvalidAfterWithUnmatchingOrderByColumns1()
        {
            string graphQLQueryName = "books";
            string after = SqlPaginationUtil.Base64Encode("[{\"Value\":2,\"Direction\":0,\"ColumnName\":\"id\"}]");
            string graphQLQuery = @"{
                 books(" + $"after: \"{after}\"" + @" orderBy: {id: Asc title: Desc}) {
                    items {
                        id
                        title
                    }
                }
            }";

            JsonElement result = await GetGraphQLControllerResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            SqlTestHelper.TestForErrorInGraphQLResponse(result.ToString(), statusCode: $"{DataGatewayException.SubStatusCodes.BadRequest}");
        }

        /// <summary>
        /// Test with after which has unnecessary columns
        /// </summary>
        [Ignore]
        [TestMethod]
        public async Task RequestInvalidAfterWithUnmatchingOrderByColumns2()
        {
            string graphQLQueryName = "books";
            string after = SqlPaginationUtil.Base64Encode(
                "[{\"Value\":2,\"Direction\":0,\"ColumnName\":\"id\"}," +
                "{\"Value\":1234,\"Direction\":1,\"ColumnName\":\"publisher_id\"}]");
            string graphQLQuery = @"{
                 books(" + $"after: \"{after}\"" + @" orderBy: {id: Asc title: Desc}) {
                    items {
                        id
                        title
                    }
                }
            }";

            JsonElement result = await GetGraphQLControllerResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            SqlTestHelper.TestForErrorInGraphQLResponse(result.ToString(), statusCode: $"{DataGatewayException.SubStatusCodes.BadRequest}");
        }

        /// <summary>
        /// Test with after which has columns which don't match the direction of
        /// orderby columns
        /// </summary>
        [Ignore]
        [TestMethod]
        public async Task RequestInvalidAfterWithUnmatchingOrderByColumns3()
        {
            string graphQLQueryName = "books";
            string after = SqlPaginationUtil.Base64Encode("[{\"Value\":2,\"Direction\":0,\"ColumnName\":\"id\"}]");
            string graphQLQuery = @"{
                 books(" + $"after: \"{after}\"" + @" orderBy: {id: Desc}) {
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
    }
}
