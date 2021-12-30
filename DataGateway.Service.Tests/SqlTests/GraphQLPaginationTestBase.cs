using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Resolvers;
using Azure.DataGateway.Services;
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
        protected static Dictionary<string, object> ExpectedJsonResultsPerTest { get; } = new()
        {
            ["RequestFullConnection"] = new
            {
                items = new[] {
                    new {
                        title = "Also Awesome book",
                        publisher = new {
                            name = "Big Company"
                        }
                    },
                    new {
                        title = "Great wall of china explained",
                        publisher = new {
                            name = "Small Town Publisher"
                        }
                    }
                },
                endCursor = SqlPaginationUtil.Base64Encode("{\"id\":3}"),
                hasNextPage = true
            },
            ["RequestNoParamFullConnection"] = new
            {
                items = new[] {
                    new {
                        id = 1,
                        title =  "Awesome book"
                    },
                    new {
                        id = 2,
                        title = "Also Awesome book"
                    },
                    new {
                        id = 3,
                        title = "Great wall of china explained"
                    },
                    new {
                        id = 4,
                        title = "US history in a nutshell"
                    }
                },
                endCursor = SqlPaginationUtil.Base64Encode("{\"id\":4}"),
                hasNextPage = false
            },
            ["RequestItemsOnly"] = new
            {
                items = new[] {
                    new {
                        title = "Also Awesome book",
                        publisher_id = 1234
                    },
                    new {
                        title = "Great wall of china explained",
                        publisher_id = 2345
                    }
                }
            },
            ["RequestNestedPaginationQueries"] = new
            {
                items = new[] {
                    new {
                        title = "Also Awesome book",
                        publisher = new {
                            name = "Big Company",
                            paginatedBooks = new {
                                items = new[] {
                                    new {
                                        id = 2,
                                        title = "Also Awesome book"
                                    }
                                },
                                endCursor = SqlPaginationUtil.Base64Encode("{\"id\":2}"),
                                hasNextPage = false
                            }
                        }
                    },
                    new {
                        title = "Great wall of china explained",
                        publisher = new {
                            name = "Small Town Publisher",
                            paginatedBooks = new {
                                items = new[] {
                                    new {
                                        id = 3,
                                        title = "Great wall of china explained"
                                    },
                                    new {
                                        id = 4,
                                        title = "US history in a nutshell"
                                    }
                                },
                                endCursor = SqlPaginationUtil.Base64Encode("{\"id\":4}"),
                                hasNextPage = false
                            }
                        }
                    }
                },
                endCursor = SqlPaginationUtil.Base64Encode("{\"id\":3}"),
                hasNextPage = true
            }
        };

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
            string expected = GetExpectedResultForTest(nameof(RequestFullConnection));

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
            string expected = GetExpectedResultForTest(nameof(RequestNoParamFullConnection));

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
            string expected = GetExpectedResultForTest(nameof(RequestItemsOnly));

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
            string after = SqlPaginationUtil.Base64Encode("{\"id\":1}");
            string graphQLQuery = @"{
                books(first: 2," + $"after: \"{after}\")" + @"{
                    endCursor
                }
            }";

            using JsonDocument result = await GetGraphQLControllerResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            JsonElement root = result.RootElement.GetProperty("data").GetProperty(graphQLQueryName);
            string actual = SqlPaginationUtil.Base64Decode(root.GetProperty("endCursor").GetString());
            string expected = "{\"id\":3}";

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
            string after = SqlPaginationUtil.Base64Encode("{\"id\":1}");
            string graphQLQuery = @"{
                books(first: 2," + $"after: \"{after}\")" + @"{
                    hasNextPage
                }
            }";

            using JsonDocument result = await GetGraphQLControllerResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            JsonElement root = result.RootElement.GetProperty("data").GetProperty(graphQLQueryName);
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

            using JsonDocument result = await GetGraphQLControllerResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            JsonElement root = result.RootElement.GetProperty("data").GetProperty(graphQLQueryName);

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
            string expected = GetExpectedResultForTest(nameof(RequestNestedPaginationQueries));

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
                    items {
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
                    items {
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
                    items {
                        title
                    }
                }
            }";

            using JsonDocument result = await GetGraphQLControllerResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            SqlTestHelper.TestForErrorInGraphQLResponse(result.RootElement.ToString(), statusCode: $"{DatagatewayException.SubStatusCodes.BadRequest}");
        }

        #endregion

        #region Helper Fucntions

        /// <summary>
        /// Gets the expected result from ExpectedJsonResultsPerTest and Serializes them
        /// </summary>
        private static string GetExpectedResultForTest(string testName)
        {
            return JsonSerializer.Serialize(ExpectedJsonResultsPerTest[testName]);
        }

        #endregion
    }
}
