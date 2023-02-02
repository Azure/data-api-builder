using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Queries;
using Azure.DataApiBuilder.Service.Resolvers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLPaginationTests
{
    /// <summary>
    /// Used to store shared hard coded expected results and tests for pagination queries between
    /// MsSql and Postgres
    /// </summary>
    [TestClass]
    public abstract class GraphQLPaginationTestBase : SqlTestBase
    {
        #region Tests

        /// <summary>
        /// Request a full connection object {items, after, hasNextPage}
        /// </summary>
        [TestMethod]
        public async Task RequestFullConnection()
        {
            string graphQLQueryName = "books";
            string defaultSchema = _sqlMetadataProvider.GetDefaultSchemaName();
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

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
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
              ""endCursor"": """ +
              SqlPaginationUtil.Base64Encode("[{\"Value\":3,\"Direction\":0,\"TableSchema\":\"" + defaultSchema + "\",\"TableName\":\"books\",\"ColumnName\":\"id\"}]") + @""",
              ""hasNextPage"": true
            }";

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
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

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
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
                },
                {
                  ""id"": 9,
                  ""title"": ""Policy-Test-01""
                },
                {
                  ""id"": 10,
                  ""title"": ""Policy-Test-02""
                },
                {
                  ""id"": 11,
                  ""title"": ""Policy-Test-04""
                },
                {
                  ""id"": 12,
                  ""title"": ""Time to Eat 2""
                },
                {
                  ""id"": 13,
                  ""title"": ""Before Sunrise""
                },
                {
                  ""id"": 14,
                  ""title"": ""Before Sunset""
                }
              ],
              ""endCursor"": """ + SqlPaginationUtil.Base64Encode("[{\"Value\":14,\"Direction\":0,\"TableSchema\":\""
                    + _sqlMetadataProvider.GetDefaultSchemaName() + "\",\"TableName\":\"books\",\"ColumnName\":\"id\"}]") + @""",
              ""hasNextPage"": false
            }";

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
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

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
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

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Request only after from the pagination for different data types.
        /// </summary>
        /// <remarks>
        /// This is probably not a common use case, but it is necessary to test graphql's capabilites to only
        /// selectively retreive data.
        /// </remarks>
        [DataTestMethod]
        [DataRow("id", 1, 4, "", "",
            DisplayName = "Test after token for primary key values.")]
        [DataRow("byte_types", 0, 255, 2, 4, DisplayName = "Test after token for byte values.")]
        [DataRow("short_types", -32768, 32767, 3, 4, DisplayName = "Test after token for short values.")]
        [DataRow("int_types", -2147483648, 2147483647, 3, 4,
            DisplayName = "Test after token for int values with mapped name.")]
        [DataRow("long_types", -9223372036854775808, 9.223372036854776E+18, 3, 4,
            DisplayName = "Test after token for long values.")]
        [DataRow("string_types", "\"\"", "\"null\"", 1, 4,
            DisplayName = "Test after token for string values.")]
        [DataRow("single_types", -3.4E38, 3.4E38, 3, 4,
            DisplayName = "Test after token for single values.")]
        [DataRow("float_types", -1.7E308, 1.7E308, 3, 4,
            DisplayName = "Test after token for float values.")]
        [DataRow("decimal_types", -9.292929, 0.333333, 2, 1,
            DisplayName = "Test after token for decimal values.")]
        [DataRow("boolean_types", "false", "true", 2, 4,
            DisplayName = "Test after token for boolean values.")]
        [DataRow("datetime_types", "\"1753-01-01T00:00:00.000\"",
            "\"9999-12-31T23:59:59.997\"", 3, 4,
            DisplayName = "Test after token for datetime values.")]
        [DataRow("bytearray_types", "\"AAAAAA==\"", "\"/////w==\"", 3, 4,
            DisplayName = "Test after token for bytearray values.")]
        [TestMethod]
        public async Task RequestAfterTokenOnly(
            string exposedFieldName,
            object afterValue,
            object endCursorValue,
            object afterIdValue,
            object endCursorIdValue)
        {
            string graphQLQueryName = "supportedTypes";
            string after;
            string defaultSchema = _sqlMetadataProvider.GetDefaultSchemaName();
            if ("id".Equals(exposedFieldName))
            {
                after = SqlPaginationUtil.Base64Encode(
                    "[{\"Value\":" + afterValue + ", \"Direction\":0,\"TableSchema\":\"" + defaultSchema + "\", " +
                    "\"TableName\":\"type_table\",\"ColumnName\":\"id\"}]");
            }
            else
            {
                after = SqlPaginationUtil.Base64Encode(
                "[{\"Value\":" + afterValue + ",\"Direction\":0,\"TableSchema\":\"" + defaultSchema + "\", " +
                "\"TableName\":\"type_table\",\"ColumnName\":\"" + exposedFieldName + "\"}," +
                "{\"Value\":" + afterIdValue + ", \"Direction\":0,\"TableSchema\":\"" + defaultSchema + "\", " +
                "\"TableName\":\"type_table\",\"ColumnName\":\"id\"}]");
            }

            string graphQLQuery = @"{
                supportedTypes(first: 3," + $"after: \"{after}\" " +
                 $"orderBy: {{ {exposedFieldName} : ASC }} )" + @"{
                    endCursor
                }
            }";

            JsonElement root = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            string actual = SqlPaginationUtil.Base64Decode(root.GetProperty(QueryBuilder.PAGINATION_TOKEN_FIELD_NAME).GetString());
            string expected;
            if ("id".Equals(exposedFieldName))
            {
                expected = "[{\"Value\":" + endCursorValue + ", \"Direction\":0,\"TableSchema\":\"" + defaultSchema + "\", " +
                    "\"TableName\":\"type_table\",\"ColumnName\":\"id\"}]";
            }
            else
            {
                expected = "[{\"Value\":" + endCursorValue + ", \"Direction\":0,\"TableSchema\":\"" + defaultSchema + "\", " +
                    "\"TableName\":\"type_table\",\"ColumnName\":\"" + exposedFieldName + "\"}," +
                    "{\"Value\":" + endCursorIdValue + ", \"Direction\":0,\"TableSchema\":\"" + defaultSchema + "\", " +
                    "\"TableName\":\"type_table\",\"ColumnName\":\"id\"}]";
            }

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
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

            JsonElement root = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
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

            JsonElement root = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);

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
            string after = SqlPaginationUtil.Base64Encode("[{\"Value\":1,\"Direction\":0,\"TableSchema\":\""
                + _sqlMetadataProvider.GetDefaultSchemaName() + "\",\"TableName\":\"books\",\"ColumnName\":\"id\"}]");
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

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
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
                        },
                        {
                          ""id"": 13,
                          ""title"": ""Before Sunrise""
                        }
                      ],
                      ""endCursor"": """ + SqlPaginationUtil.Base64Encode("[{\"Value\":13,\"Direction\":0,\"TableSchema\":\""
                      + _sqlMetadataProvider.GetDefaultSchemaName() + "\",\"TableName\":\"books\",\"ColumnName\":\"id\"}]") + @""",
                      ""hasNextPage"": true
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
                      ""endCursor"": """ + SqlPaginationUtil.Base64Encode("[{\"Value\":4,\"Direction\":0,\"TableSchema\":\""
                      + _sqlMetadataProvider.GetDefaultSchemaName() + "\",\"TableName\":\"books\",\"ColumnName\":\"id\"}]") + @""",
                      ""hasNextPage"": false
                    }
                  }
                }
              ],
              ""endCursor"": """ + SqlPaginationUtil.Base64Encode("[{\"Value\":3,\"Direction\":0,\"TableSchema\":\""
                + _sqlMetadataProvider.GetDefaultSchemaName() + "\",\"TableName\":\"books\",\"ColumnName\":\"id\"}]") + @""",
              ""hasNextPage"": true
            }";

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Does a paginated query as a subquery of a mutation result
        /// </summary>
        [TestMethod]
        public async Task RequestPaginatedQueryFromMutationResult()
        {
            string graphQLMutationName = "createbook";
            string after = SqlPaginationUtil.Base64Encode("[{\"Value\":1,\"Direction\":0,\"TableSchema\":\""
                + _sqlMetadataProvider.GetDefaultSchemaName() + "\",\"TableName\":\"books\",\"ColumnName\":\"id\"}]");
            string graphQLMutation = @"
                mutation {
                    createbook(item: { title: ""Books, Pages, and Pagination. The Book"", publisher_id: 1234 }) {
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
            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            string expected = @"{
              ""publishers"": {
                ""books"": {
                  ""items"": [
                    {
                      ""id"": 2,
                      ""title"": ""Also Awesome book""
                    },
                    {
                      ""id"": 13,
                      ""title"": ""Before Sunrise""
                    }
                  ],
                  ""endCursor"": """ + SqlPaginationUtil.Base64Encode("[{\"Value\":13,\"Direction\":0,\"TableSchema\":\""
                    + _sqlMetadataProvider.GetDefaultSchemaName() + "\",\"TableName\":\"books\",\"ColumnName\":\"id\"}]") + @""",
                  ""hasNextPage"": true
                }
              }
            }";

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());

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

            string after = "[{\"Value\":1,\"Direction\":0,\"TableSchema\":\""
                    + _sqlMetadataProvider.GetDefaultSchemaName() + "\",\"TableName\":\"reviews\",\"ColumnName\":\"book_id\"}," +
                          "{\"Value\":568,\"Direction\":0,\"TableSchema\":\""
                    + _sqlMetadataProvider.GetDefaultSchemaName() + "\",\"TableName\":\"reviews\",\"ColumnName\":\"id\"}]";
            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
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
              ""endCursor"": """ +
              SqlPaginationUtil.Base64Encode("[{\"Value\":3,\"Direction\":0,\"TableSchema\":\""
                + _sqlMetadataProvider.GetDefaultSchemaName() + "\",\"TableName\":\"books\",\"ColumnName\":\"id\"}]") + @"""
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
              ""endCursor"": """
                + SqlPaginationUtil.Base64Encode("[{\"Value\":3,\"Direction\":0,\"TableSchema\":\""
                + _sqlMetadataProvider.GetDefaultSchemaName() + "\",\"TableName\":\"books\",\"ColumnName\":\"id\"}]") + @"""
            }
          }
        ]
      }
    }
  ],
  ""hasNextPage"": true,
  ""endCursor"": """ + SqlPaginationUtil.Base64Encode("[{\"Value\":2,\"Direction\":0,\"TableSchema\":\""
        + _sqlMetadataProvider.GetDefaultSchemaName() + "\",\"TableName\":\"books\",\"ColumnName\":\"id\"}]") + @"""
}";

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Do pagination on a table with a primary key with multiple columns
        /// </summary>
        [TestMethod]
        public async Task PaginateCompositePkTable()
        {
            string graphQLQueryName = "reviews";
            string after = SqlPaginationUtil.Base64Encode("[{\"Value\":1,\"Direction\":0,\"TableSchema\":\""
                + _sqlMetadataProvider.GetDefaultSchemaName() + "\",\"TableName\":\"reviews\",\"ColumnName\":\"book_id\"}," +
                "{\"Value\":567,\"Direction\":0,\"TableSchema\":\"" + _sqlMetadataProvider.GetDefaultSchemaName() + "\",\"TableName\":\"reviews\",\"ColumnName\":\"id\"}]");
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

            after = SqlPaginationUtil.Base64Encode("[{\"Value\":1,\"Direction\":0,\"TableSchema\":\""
                    + _sqlMetadataProvider.GetDefaultSchemaName() + "\",\"TableName\":\"reviews\",\"ColumnName\":\"book_id\"}," +
                    "{\"Value\":569,\"Direction\":0,\"TableSchema\":\"" + _sqlMetadataProvider.GetDefaultSchemaName()
                    + "\",\"TableName\":\"reviews\",\"ColumnName\":\"id\"}]");
            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
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

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Restrict the pagination result using the filter argument
        /// </summary>
        [TestMethod]
        public async Task PaginationWithFilterArgument()
        {
            string graphQLQueryName = "books";
            string after = SqlPaginationUtil.Base64Encode("[{\"Value\":1,\"Direction\":0,\"TableSchema\":\"" +
                 _sqlMetadataProvider.GetDefaultSchemaName() + "\",\"TableName\":\"books\",\"ColumnName\":\"id\"}]");
            string graphQLQuery = @"{
                books(first: 2, after: """ + after + @""", " + QueryBuilder.FILTER_FIELD_NAME + @" : {publisher_id: {eq: 2345}}) {
                    items {
                        id
                        publisher_id
                    }
                    endCursor
                    hasNextPage
                }
            }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
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
              ""endCursor"": """ +
                SqlPaginationUtil.Base64Encode("[{\"Value\":4,\"Direction\":0,\"TableSchema\":\""
                + _sqlMetadataProvider.GetDefaultSchemaName() + "\",\"TableName\":\"books\",\"ColumnName\":\"id\"}]") + @""",
              ""hasNextPage"": false
            }";

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Test paginating while ordering by a subset of columns of a composite pk
        /// </summary>
        [TestMethod]
        public async Task TestPaginationWithOrderByWithPartialPk()
        {
            string graphQLQueryName = "stocks";
            string graphQLQuery = @"{
                stocks(first: 2 orderBy: {pieceid: DESC}) {
                    items {
                        pieceid
                        categoryid
                    }
                    endCursor
                    hasNextPage
                }
            }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
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
                  "[{\"Value\":1,\"Direction\":1,\"TableSchema\":\"" + _sqlMetadataProvider.GetDefaultSchemaName() + "\",\"TableName\":\"stocks\",\"ColumnName\":\"pieceid\"}," +
                  "{\"Value\":0,\"Direction\":0,\"TableSchema\":\"" + _sqlMetadataProvider.GetDefaultSchemaName() + " \",\"TableName\":\"stocks\",\"ColumnName\":\"categoryid\"}]") + @""",
              ""hasNextPage"": true
            }";

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Paginate first two entries then paginate again with the returned after token.
        /// Verify both pagination query results
        /// </summary>
        [TestMethod]
        public async Task TestCallingPaginationTwiceWithOrderBy()
        {
            string graphQLQueryName = "books";
            string graphQLQuery1 = @"{
                books(first: 2 orderBy: {title: DESC publisher_id: ASC id: DESC}) {
                    items {
                        id
                        title
                        publisher_id
                    }
                    endCursor
                    hasNextPage
                }
            }";

            JsonElement actual1 = await ExecuteGraphQLRequestAsync(graphQLQuery1, graphQLQueryName, isAuthenticated: false);

            string expectedAfter1 = SqlPaginationUtil.Base64Encode(
                  "[{\"Value\":\"Time to Eat 2\",\"Direction\":1,\"TableSchema\":\"" + _sqlMetadataProvider.GetDefaultSchemaName() + "\",\"TableName\":\"books\",\"ColumnName\":\"title\"}," +
                  "{\"Value\":1941,\"Direction\":0,\"TableSchema\":\"" + _sqlMetadataProvider.GetDefaultSchemaName() + "\",\"TableName\":\"books\",\"ColumnName\":\"publisher_id\"}," +
                  "{\"Value\":12,\"Direction\":1,\"TableSchema\":\"" + _sqlMetadataProvider.GetDefaultSchemaName() + "\",\"TableName\":\"books\",\"ColumnName\":\"id\"}]");

            string expected1 = @"{
              ""items"": [
                {
                  ""id"": 4,
                  ""title"": ""US history in a nutshell"",
                  ""publisher_id"": 2345
                },
                {
                  ""id"": 12,
                  ""title"": ""Time to Eat 2"",
                  ""publisher_id"": 1941
                }
              ],
              ""endCursor"": """ + expectedAfter1 + @""",
              ""hasNextPage"": true
            }";

            SqlTestHelper.PerformTestEqualJsonStrings(expected1, actual1.ToString());

            string graphQLQuery2 = @"{
                books(first: 2, after: """ + expectedAfter1 + @""" orderBy: {title: DESC publisher_id: ASC id: DESC}) {
                    items {
                        id
                        title
                        publisher_id
                    }
                    endCursor
                    hasNextPage
                }
            }";

            JsonElement actual2 = await ExecuteGraphQLRequestAsync(graphQLQuery2, graphQLQueryName, isAuthenticated: false);

            string expectedAfter2 = SqlPaginationUtil.Base64Encode(
                  "[{\"Value\":\"The Palace Door\",\"Direction\":1,\"TableSchema\":\"" + _sqlMetadataProvider.GetDefaultSchemaName() + "\",\"TableName\":\"books\",\"ColumnName\":\"title\"}," +
                  "{\"Value\":2324,\"Direction\":0,\"TableSchema\":\"" + _sqlMetadataProvider.GetDefaultSchemaName() + "\",\"TableName\":\"books\",\"ColumnName\":\"publisher_id\"}," +
                  "{\"Value\":6,\"Direction\":1,\"TableSchema\":\"" + _sqlMetadataProvider.GetDefaultSchemaName() + "\",\"TableName\":\"books\",\"ColumnName\":\"id\"}]");

            string expected2 = @"{
              ""items"": [
                {
                  ""id"": 8,
                  ""title"": ""Time to Eat"",
                  ""publisher_id"": 2324
                },
                {
                  ""id"": 6,
                  ""title"": ""The Palace Door"",
                  ""publisher_id"": 2324
                }
              ],
              ""endCursor"": """ + expectedAfter2 + @""",
              ""hasNextPage"": true
            }";

            SqlTestHelper.PerformTestEqualJsonStrings(expected2, actual2.ToString());
        }

        /// <summary>
        /// Paginate ordering with a column for which multiple entries
        /// have the same value, and check that the column tie break is resolved properly
        /// </summary>
        [TestMethod]
        public async Task TestColumnTieBreak()
        {
            string graphQLQueryName = "books";
            string graphQLQuery1 = @"{
                books(first: 4 orderBy: {publisher_id: DESC}) {
                    items {
                        id
                        publisher_id
                    }
                    endCursor
                    hasNextPage
                }
            }";

            JsonElement actual1 = await ExecuteGraphQLRequestAsync(graphQLQuery1, graphQLQueryName, isAuthenticated: false);

            string expectedAfter1 = SqlPaginationUtil.Base64Encode(
                  "[{\"Value\":2324,\"Direction\":1,\"TableSchema\":\"" + _sqlMetadataProvider.GetDefaultSchemaName() + "\",\"TableName\":\"books\",\"ColumnName\":\"publisher_id\"}," +
                  "{\"Value\":7,\"Direction\":0,\"TableSchema\":\"" + _sqlMetadataProvider.GetDefaultSchemaName() + "\",\"TableName\":\"books\",\"ColumnName\":\"id\"}]");

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

            SqlTestHelper.PerformTestEqualJsonStrings(expected1, actual1.ToString());

            string graphQLQuery2 = @"{
                books(first: 2, after: """ + expectedAfter1 + @""" orderBy: {publisher_id: DESC}) {
                    items {
                        id
                        publisher_id
                    }
                    endCursor
                    hasNextPage
                }
            }";

            JsonElement actual2 = await ExecuteGraphQLRequestAsync(graphQLQuery2, graphQLQueryName, isAuthenticated: false);

            string expectedAfter2 = SqlPaginationUtil.Base64Encode(
                  "[{\"Value\":2323,\"Direction\":1,\"TableSchema\":\"" + _sqlMetadataProvider.GetDefaultSchemaName() + "\",\"TableName\":\"books\",\"ColumnName\":\"publisher_id\"}," +
                  "{\"Value\":5,\"Direction\":0,\"TableSchema\":\"" + _sqlMetadataProvider.GetDefaultSchemaName() + "\",\"TableName\":\"books\",\"ColumnName\":\"id\"}]");

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

            SqlTestHelper.PerformTestEqualJsonStrings(expected2, actual2.ToString());
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

            JsonElement result = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            SqlTestHelper.TestForErrorInGraphQLResponse(result.ToString(), statusCode: $"{DataApiBuilderException.SubStatusCodes.BadRequest}");
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

            JsonElement result = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            SqlTestHelper.TestForErrorInGraphQLResponse(result.ToString(), statusCode: $"{DataApiBuilderException.SubStatusCodes.BadRequest}");
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

            JsonElement result = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            SqlTestHelper.TestForErrorInGraphQLResponse(result.ToString(), statusCode: $"{DataApiBuilderException.SubStatusCodes.BadRequest}");
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

            JsonElement result = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            SqlTestHelper.TestForErrorInGraphQLResponse(result.ToString(), statusCode: $"{DataApiBuilderException.SubStatusCodes.BadRequest}");
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

            JsonElement result = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            SqlTestHelper.TestForErrorInGraphQLResponse(result.ToString(), statusCode: $"{DataApiBuilderException.SubStatusCodes.BadRequest}");
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

            JsonElement result = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            SqlTestHelper.TestForErrorInGraphQLResponse(result.ToString(), statusCode: $"{DataApiBuilderException.SubStatusCodes.BadRequest}");
        }

        /// <summary>
        /// Test with after which does not include all orderBy columns
        /// </summary>
        [TestMethod]
        public async Task RequestInvalidAfterWithUnmatchingOrderByColumns1()
        {
            string graphQLQueryName = "books";
            string after = SqlPaginationUtil.Base64Encode("[{\"Value\":2,\"Direction\":0,\"ColumnName\":\"id\"}]");
            string graphQLQuery = @"{
                 books(" + $"after: \"{after}\"" + @" orderBy: {id: ASC title: DESC}) {
                    items {
                        id
                        title
                    }
                }
            }";

            JsonElement result = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            SqlTestHelper.TestForErrorInGraphQLResponse(result.ToString(), statusCode: $"{DataApiBuilderException.SubStatusCodes.BadRequest}");
        }

        /// <summary>
        /// Test with after which has unnecessary columns
        /// </summary>
        [TestMethod]
        public async Task RequestInvalidAfterWithUnmatchingOrderByColumns2()
        {
            string graphQLQueryName = "books";
            string after = SqlPaginationUtil.Base64Encode(
                "[{\"Value\":2,\"Direction\":0,\"ColumnName\":\"id\"}," +
                "{\"Value\":1234,\"Direction\":1,\"ColumnName\":\"publisher_id\"}]");
            string graphQLQuery = @"{
                 books(" + $"after: \"{after}\"" + @" orderBy: {id: ASC title: DESC}) {
                    items {
                        id
                        title
                    }
                }
            }";

            JsonElement result = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            SqlTestHelper.TestForErrorInGraphQLResponse(result.ToString(), statusCode: $"{DataApiBuilderException.SubStatusCodes.BadRequest}");
        }

        /// <summary>
        /// Test with after which has columns which don't match the direction of
        /// orderby columns
        /// </summary>
        [TestMethod]
        public async Task RequestInvalidAfterWithUnmatchingOrderByColumns3()
        {
            string graphQLQueryName = "books";
            string after = SqlPaginationUtil.Base64Encode("[{\"Value\":2,\"Direction\":0,\"ColumnName\":\"id\"}]");
            string graphQLQuery = @"{
                 books(" + $"after: \"{after}\"" + @" orderBy: {id: DESC}) {
                    items {
                        id
                        title
                    }
                }
            }";

            JsonElement result = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            SqlTestHelper.TestForErrorInGraphQLResponse(result.ToString(), statusCode: $"{DataApiBuilderException.SubStatusCodes.BadRequest}");
        }

        #endregion
    }
}
