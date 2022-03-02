using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Exceptions;
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
        /// Request a full connection object {items, endCursor, hasNextPage}
        /// </summary>
        [TestMethod]
        public async Task RequestFullConnection()
        {
            string graphQLQueryName = "books";
            string after = SqlPaginationUtil.Base64Encode("{\"id\":1}");
            string graphQLQuery = @"{
                books(first: 2," + $"after: \"{after}\")" + @"{
                    items {
                        title
                        publisher {
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
                  ""publisher"": {
                    ""name"": ""Big Company""
                  }
                },
                {
                  ""title"": ""Great wall of china explained"",
                  ""publisher"": {
                    ""name"": ""Small Town Publisher""
                  }
                }
              ],
              ""endCursor"": """ + SqlPaginationUtil.Base64Encode("{\"id\":3}") + @""",
              ""hasNextPage"": true
            }";

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Request a full connection object {items, endCursor, hasNextPage}
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
              ""endCursor"": """ + SqlPaginationUtil.Base64Encode("{\"id\":8}") + @""",
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
            string after = SqlPaginationUtil.Base64Encode("{\"id\":1}");
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
        /// Request only endCursor from the pagination
        /// </summary>
        /// <remarks>
        /// This is probably not a common use case, but it is necessary to test graphql's capabilites to only
        /// selectively retreive data
        /// </remarks>
        [TestMethod]
        public async Task RequestEndCursorOnly()
        {
            string graphQLQueryName = "books";
            string after = SqlPaginationUtil.Base64Encode("{\"id\":1}");
            string graphQLQuery = @"{
                books(first: 2," + $"after: \"{after}\")" + @"{
                    endCursor
                }
            }";

            JsonElement root = await GetGraphQLControllerResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            root = root.GetProperty("data").GetProperty(graphQLQueryName);
            string actual = SqlPaginationUtil.Base64Decode(root.GetProperty("endCursor").GetString());
            string expected = "{\"id\":3}";

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
            string after = SqlPaginationUtil.Base64Encode("{\"id\":1}");
            string graphQLQuery = @"{
                books(first: 2," + $"after: \"{after}\")" + @"{
                    hasNextPage
                }
            }";

            JsonElement root = await GetGraphQLControllerResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            root = root.GetProperty("data").GetProperty(graphQLQueryName);
            bool actual = root.GetProperty("hasNextPage").GetBoolean();

            Assert.AreEqual(true, actual);
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
            Assert.AreEqual(null, root.GetProperty("endCursor").GetString());
            Assert.AreEqual(false, root.GetProperty("hasNextPage").GetBoolean());
        }

        /// <summary>
        /// Request nested pagination queries
        /// </summary>
        [TestMethod]
        public async Task RequestNestedPaginationQueries()
        {
            string graphQLQueryName = "books";
            string after = SqlPaginationUtil.Base64Encode("{\"id\":1}");
            string graphQLQuery = @"{
                 books(first: 2," + $"after: \"{after}\")" + @"{
                    items {
                        title
                        publisher {
                            name
                            paginatedBooks(first: 2, after:""" + after + @"""){
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
                  ""publisher"": {
                    ""name"": ""Big Company"",
                    ""paginatedBooks"": {
                      ""items"": [
                        {
                          ""id"": 2,
                          ""title"": ""Also Awesome book""
                        }
                      ],
                      ""endCursor"": """ + SqlPaginationUtil.Base64Encode("{\"id\":2}") + @""",
                      ""hasNextPage"": false
                    }
                  }
                },
                {
                  ""title"": ""Great wall of china explained"",
                  ""publisher"": {
                    ""name"": ""Small Town Publisher"",
                    ""paginatedBooks"": {
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
                      ""endCursor"": """ + SqlPaginationUtil.Base64Encode("{\"id\":4}") + @""",
                      ""hasNextPage"": false
                    }
                  }
                }
              ],
              ""endCursor"": """ + SqlPaginationUtil.Base64Encode("{\"id\":3}") + @""",
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
            string graphQLMutationName = "insertBook";
            string after = SqlPaginationUtil.Base64Encode("{\"id\":1}");
            string graphQLMutation = @"
                mutation {
                    insertBook(title: ""Books, Pages, and Pagination. The Book"", publisher_id: 1234) {
                        publisher {
                            paginatedBooks(first: 2, after: """ + after + @""") {
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
              ""publisher"": {
                ""paginatedBooks"": {
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
                  ""endCursor"": """ + SqlPaginationUtil.Base64Encode("{\"id\":5001}") + @""",
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
                    items{
                        id
                        authors(first: 2) {
                            name
                            paginatedBooks(first: 2) {
                                items {
                                    id
                                    title
                                    paginatedReviews(first: 2)
                                    {
                                        items {
                                            id
                                            book{
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
                    hasNextPage
                    endCursor
                }
            }";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = @"{
              ""items"": [
                {
                  ""id"": 1,
                  ""authors"": [
                    {
                      ""name"": ""Jelte"",
                      ""paginatedBooks"": {
                        ""items"": [
                          {
                            ""id"": 1,
                            ""title"": ""Awesome book"",
                            ""paginatedReviews"": {
                              ""items"": [
                                {
                                  ""id"": 567,
                                  ""book"": {
                                    ""id"": 1
                                  },
                                  ""content"": ""Indeed a great book""
                                },
                                {
                                  ""id"": 568,
                                  ""book"": {
                                    ""id"": 1
                                  },
                                  ""content"": ""I loved it""
                                }
                              ],
                              ""endCursor"": """ + SqlPaginationUtil.Base64Encode("{\"book_id\":1,\"id\":568}") + @""",
                              ""hasNextPage"": true
                            }
                          },
                          {
                            ""id"": 3,
                            ""title"": ""Great wall of china explained"",
                            ""paginatedReviews"": {
                              ""items"": [],
                              ""endCursor"": null,
                              ""hasNextPage"": false
                            }
                          }
                        ],
                        ""hasNextPage"": true,
                        ""endCursor"": """ + SqlPaginationUtil.Base64Encode("{\"id\":3}") + @"""
                      }
                    }
                  ]
                },
                {
                  ""id"": 2,
                  ""authors"": [
                    {
                      ""name"": ""Aniruddh"",
                      ""paginatedBooks"": {
                        ""items"": [
                          {
                            ""id"": 2,
                            ""title"": ""Also Awesome book"",
                            ""paginatedReviews"": {
                              ""items"": [],
                              ""endCursor"": null,
                              ""hasNextPage"": false
                            }
                          },
                          {
                            ""id"": 3,
                            ""title"": ""Great wall of china explained"",
                            ""paginatedReviews"": {
                              ""items"": [],
                              ""endCursor"": null,
                              ""hasNextPage"": false
                            }
                          }
                        ],
                        ""hasNextPage"": true,
                        ""endCursor"": """ + SqlPaginationUtil.Base64Encode("{\"id\":3}") + @"""
                      }
                    }
                  ]
                }
              ],
              ""hasNextPage"": true,
              ""endCursor"": """ + SqlPaginationUtil.Base64Encode("{\"id\":2}") + @"""
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
            string after = SqlPaginationUtil.Base64Encode("{\"book_id\":1,\"id\":567}");
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
              ""endCursor"": """ + SqlPaginationUtil.Base64Encode("{\"book_id\":1,\"id\":569}") + @"""
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
            string after = SqlPaginationUtil.Base64Encode("{\"id\":1}");
            string graphQLQuery = @"{
                books(first: 2, after: """ + after + @""", _filter: ""publisher_id eq 2345"") {
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
              ""endCursor"": """ + SqlPaginationUtil.Base64Encode("{\"id\":4}") + @""",
              ""hasNextPage"": false
            }";

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
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
        /// Supply an invalid key to the after JSON
        /// </summary>
        [TestMethod]
        public async Task RequestInvalidAfterWithIncorrectKeys()
        {
            string graphQLQueryName = "books";
            string after = SqlPaginationUtil.Base64Encode("{ \"title\": \"Great Book\" }");
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
            string after = SqlPaginationUtil.Base64Encode("{ \"id\": \"1\" }");
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

        #endregion
    }
}
