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
    public class MsSqlGraphQLPaginationTests : SqlTestBase
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
            _graphQLService = new GraphQLService(_queryEngine, mutationEngine: null, _metadataStoreProvider);
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
            string after = SqlPaginationUtil.Base64Encode("{ \"id\": 4 }");
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
            string msSqlQuery = @"
                SELECT [items] = JSON_QUERY('[' + COALESCE(STRING_AGG([jsonelems], ', '), '') + ']'),
                    [endCursor] = CASE
                        WHEN max([id]) IS NOT NULL
                            THEN (
                                    SELECT CAST(CONCAT (
                                                '{',
                                                '""id"":',
                                                max(id),
                                                '}'
                                                ) AS VARBINARY(MAX))
                                    FOR XML PATH(''),
                                        BINARY BASE64
                                    )
                        ELSE NULL
                        END,
                    [hasNextPage] = CAST(CASE
                            WHEN max(___rowcount___) > 2
                                THEN 1
                            ELSE 0
                            END AS BIT)
                FROM (
                    SELECT TOP 2 [jsonelems] = (
                            SELECT [title],
                                [publisher]
                            FOR JSON PATH,
                                INCLUDE_NULL_VALUES,
                                WITHOUT_ARRAY_WRAPPER
                            ),
                        [id],
                        COUNT(*) OVER () AS ___rowcount___
                    FROM (
                        SELECT TOP 3 [table0].[title] AS [title],
                            JSON_QUERY([table1_subq].[data]) AS [publisher],
                            [table0].[id] AS [id]
                        FROM [books] AS [table0]
                        OUTER APPLY (
                            SELECT TOP 1 [table1].[name] AS [name]
                            FROM [publishers] AS [table1]
                            WHERE [table0].[publisher_id] = [table1].[id]
                            ORDER BY [id]
                            FOR JSON PATH,
                                INCLUDE_NULL_VALUES,
                                WITHOUT_ARRAY_WRAPPER
                            ) AS [table1_subq]([data])
                        WHERE [id] > 4
                        ORDER BY [id]
                        ) AS wrapper
                    ) AS paginatedquery
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(msSqlQuery);

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
            string msSqlQuery = @"
                SELECT [items] = JSON_QUERY('[' + COALESCE(STRING_AGG([jsonelems], ', '), '') + ']'),
                    [endCursor] = CASE
                        WHEN max([id]) IS NOT NULL
                            THEN (
                                    SELECT CAST(CONCAT (
                                                '{',
                                                '""id"":',
                                                max(id),
                                                '}'
                                                ) AS VARBINARY(MAX))
                                    FOR XML PATH(''),
                                        BINARY BASE64
                                    )
                        ELSE NULL
                        END,
                    [hasNextPage] = CAST(CASE
                            WHEN max(___rowcount___) > 100
                                THEN 1
                            ELSE 0
                            END AS BIT)
                FROM (
                    SELECT TOP 100 [jsonelems] = (
                            SELECT [id],
                                [title]
                            FOR JSON PATH,
                                INCLUDE_NULL_VALUES,
                                WITHOUT_ARRAY_WRAPPER
                            ),
                        [id],
                        COUNT(*) OVER () AS ___rowcount___
                    FROM (
                        SELECT TOP 101 [table0].[id] AS [id],
                            [table0].[title] AS [title]
                        FROM [books] AS [table0]
                        WHERE 1 = 1
                        ORDER BY [id]
                        ) AS wrapper
                    ) AS paginatedquery
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(msSqlQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Request only items from the pagination
        /// </summary>
        [TestMethod]
        public async Task RequestItemsOnly()
        {
            string graphQLQueryName = "books";
            string after = SqlPaginationUtil.Base64Encode("{ \"id\": 4 }");
            string graphQLQuery = @"{
                books(first: 2," + $"after: \"{after}\")" + @"{
                    items {
                        title
                        publisher_id
                    }
                }
            }";
            string msSqlQuery = @"
                SELECT [items] = JSON_QUERY('[' + COALESCE(STRING_AGG([jsonelems], ', '), '') + ']')
                FROM (
                    SELECT [jsonelems] = (
                            SELECT [title],
                                [publisher_id]
                            FOR JSON PATH,
                                INCLUDE_NULL_VALUES,
                                WITHOUT_ARRAY_WRAPPER
                            )
                    FROM (
                        SELECT TOP 2 [table0].[title] AS [title],
                            [table0].[publisher_id] AS [publisher_id]
                        FROM [books] AS [table0]
                        WHERE [id] > 4
                        ORDER BY [id]
                        ) AS wrapper
                    ) AS paginatedquery
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(msSqlQuery);

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
            string msSqlQuery = @"
                SELECT [endCursor] = CASE
                        WHEN max([id]) IS NOT NULL
                            THEN (
                                    SELECT CAST(CONCAT (
                                                '{',
                                                '""id"":',
                                                max(id),
                                                '}'
                                                ) AS VARBINARY(MAX))
                                    FOR XML PATH(''),
                                        BINARY BASE64
                                    )
                        ELSE NULL
                        END
                FROM (
                    SELECT TOP 2 [table0].[id] AS [id]
                    FROM [books] AS [table0]
                    WHERE [id] > 4
                    ORDER BY [id]
                    ) AS paginatedquery
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(msSqlQuery);

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
            string msSqlQuery = @"
                SELECT [hasNextPage] = CAST(CASE
                            WHEN max(___rowcount___) > 2
                                THEN 1
                            ELSE 0
                            END AS BIT)
                FROM (
                    SELECT TOP 2 COUNT(*) OVER () AS ___rowcount___
                    FROM (
                        SELECT TOP 3 *
                        FROM [books] AS [table0]
                        WHERE [id] > 4
                        ORDER BY [id]
                        ) AS wrapper
                    ) AS paginatedquery
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(msSqlQuery);

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
