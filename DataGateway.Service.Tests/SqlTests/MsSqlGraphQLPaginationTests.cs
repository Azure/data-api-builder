using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Resolvers;
using Azure.DataGateway.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests
{

    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class MsSqlGraphQLPaginationTests : GraphQLPaginationTestBase
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
            await InitializeTestFixture(context, _integrationTableName, TestCategory.MSSQL);

            // Setup GraphQL Components
            _graphQLService = new GraphQLService(_metadataStoreProvider, _resolverMiddlewareMaker);
            _graphQLController = new GraphQLController(_graphQLService);
        }

        #endregion

        #region Tests

        /// <summary>
        /// Request a full connection object {items, endCursor, hasNextPage}
        /// </summary>
        [TestMethod]
        public async Task RequestFullConnection()
        {
            string graphQLQueryName = "books";
            string after = SqlPaginationUtil.Base64Encode("{ \"id\": 1 }");
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
            string expected = ExpectedJsonResultsPerTest["RequestFullConnection"];

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
            string expected = ExpectedJsonResultsPerTest["RequestNoParamFullConnection"];

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Request only items from the pagination
        /// </summary>
        [TestMethod]
        public async Task RequestItemsOnly()
        {
            string graphQLQueryName = "books";
            string after = SqlPaginationUtil.Base64Encode("{ \"id\": 1 }");
            string graphQLQuery = @"{
                books(first: 2," + $"after: \"{after}\")" + @"{
                    items {
                        title
                        publisher_id
                    }
                }
            }";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = ExpectedJsonResultsPerTest["RequestItemsOnly"];

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
            string after = SqlPaginationUtil.Base64Encode("{ \"id\": 1 }");
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
                            paginatedBooks(first: 2){
                                items {
                                    id
                                    title
                                }
                                hasNextPage
                            }
                        }
                    }
                    endCursor
                    hasNextPage
                }
            }";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = ExpectedJsonResultsPerTest["RequestNestedPaginationQueries"];

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
    }
}
