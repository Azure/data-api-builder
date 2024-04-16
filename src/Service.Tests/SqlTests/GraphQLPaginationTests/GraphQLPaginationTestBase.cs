// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Queries;
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
            string after = SqlPaginationUtil.Base64Encode($"[{{\"EntityName\":\"Book\",\"FieldName\":\"id\",\"FieldValue\":1,\"Direction\":0}}]");
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
              SqlPaginationUtil.Base64Encode($"[{{\"EntityName\":\"Book\",\"FieldName\":\"id\",\"FieldValue\":3,\"Direction\":0}}]") + @""",
              ""hasNextPage"": true
            }";

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Request a full connection object {items, after, hasNextPage}
        /// using a negative one for the first parameter.
        /// This should return max items as we use -1 to allow user to get max allowed page size.
        /// </summary>
        [TestMethod]
        public async Task RequestMaxUsingNegativeOne()
        {
            string graphQLQueryName = "books";
            string graphQLQuery = @"{
                books (first: -1) {
                    items {
                        id
                        title
                    }
                    endCursor
                    hasNextPage
                }
            }";

            // this resultset represents all books in the db.
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
              ""endCursor"": null,
              ""hasNextPage"": false
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
              ""endCursor"": null,
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
            string after = SqlPaginationUtil.Base64Encode($"[{{\"EntityName\":\"Books\",\"FieldName\":\"id\",\"FieldValue\":1,\"Direction\":0}}]");
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
        public async virtual Task RequestAfterTokenOnly(
            string exposedFieldName,
            object afterValue,
            object endCursorValue,
            object afterIdValue,
            object endCursorIdValue,
            bool isLastPage)
        {
            string graphQLQueryName = "supportedTypes";
            string after;
            if ("typeid".Equals(exposedFieldName))
            {
                after = SqlPaginationUtil.Base64Encode($"[{{\"EntityName\":\"SupportedType\",\"FieldName\":\"typeid\",\"FieldValue\":\"{afterValue}\",\"Direction\":0}}]");
            }
            else
            {
                after = SqlPaginationUtil.Base64Encode(
                $"[{{\"EntityName\":\"SupportedType\",\"FieldName\":\"{exposedFieldName}\",\"FieldValue\":{afterValue},\"Direction\":0}}," +
                $"{{\"EntityName\":\"SupportedType\",\"FieldName\":\"typeid\",\"FieldValue\":{afterIdValue},\"Direction\":0}}]");
            }

            string graphQLQuery = @"{
                supportedTypes(first: 2," + $"after: \"{after}\" " +
                 $"orderBy: {{ {exposedFieldName} : ASC }} )" + @"{
                    endCursor
                }
            }";

            JsonElement root = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            string actual = root.GetProperty(QueryBuilder.PAGINATION_TOKEN_FIELD_NAME).GetString();
            // Decode if not null
            actual = string.IsNullOrEmpty(actual) ? "null" : SqlPaginationUtil.Base64Decode(root.GetProperty(QueryBuilder.PAGINATION_TOKEN_FIELD_NAME).GetString());
            string expected;
            if ("typeid".Equals(exposedFieldName))
            {
                expected = $"[{{\"EntityName\":\"SupportedType\",\"FieldName\":\"typeid\",\"FieldValue\":{endCursorValue},\"Direction\":0}}]";
            }
            else
            {
                expected = $"[{{\"EntityName\":\"SupportedType\",\"FieldName\":\"{exposedFieldName}\",\"FieldValue\":{endCursorValue},\"Direction\":0}}," +
                    $"{{\"EntityName\":\"SupportedType\",\"FieldName\":\"typeid\",\"FieldValue\":{endCursorIdValue},\"Direction\":0}}]";
            }

            if (isLastPage)
            {
                expected = "null";
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
            string after = SqlPaginationUtil.Base64Encode($"[{{\"EntityName\":\"Books\",\"FieldName\":\"id\",\"FieldValue\":1,\"Direction\":0}}]");
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
            string after = SqlPaginationUtil.Base64Encode($"[{{\"EntityName\":\"Books\",\"FieldName\":\"id\",\"FieldValue\":1000000,\"Direction\":0}}]");
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
            string after = SqlPaginationUtil.Base64Encode($"[{{\"EntityName\":\"Book\",\"FieldName\":\"id\",\"FieldValue\":1,\"Direction\":0}}]");
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
                      ""endCursor"": """ + SqlPaginationUtil.Base64Encode($"[{{\"EntityName\":\"Book\",\"FieldName\":\"id\",\"FieldValue\":13,\"Direction\":0}}]") + @""",
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
                      ""endCursor"": null,
                      ""hasNextPage"": false
                    }
                  }
                }
              ],
              ""endCursor"": """ + SqlPaginationUtil.Base64Encode($"[{{\"EntityName\":\"Book\",\"FieldName\":\"id\",\"FieldValue\":3,\"Direction\":0}}]") + @""",
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
            string after = SqlPaginationUtil.Base64Encode($"[{{\"EntityName\":\"Book\",\"FieldName\":\"id\",\"FieldValue\":1,\"Direction\":0}}]");
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
                  ""endCursor"": """ + SqlPaginationUtil.Base64Encode($"[{{\"EntityName\":\"Book\",\"FieldName\":\"id\",\"FieldValue\":13,\"Direction\":0}}]") + @""",
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

            string after = $"[{{\"EntityName\":\"Review\",\"FieldName\":\"book_id\",\"FieldValue\":1,\"Direction\":0}}," +
                $"{{\"EntityName\":\"Review\",\"FieldName\":\"id\",\"FieldValue\":568,\"Direction\":0}}]";
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
              SqlPaginationUtil.Base64Encode($"[{{\"EntityName\":\"Book\",\"FieldName\":\"id\",\"FieldValue\":3,\"Direction\":0}}]") + @"""
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
                + SqlPaginationUtil.Base64Encode($"[{{\"EntityName\":\"Book\",\"FieldName\":\"id\",\"FieldValue\":3,\"Direction\":0}}]") + @"""
            }
          }
        ]
      }
    }
  ],
  ""hasNextPage"": true,
  ""endCursor"": """ + SqlPaginationUtil.Base64Encode($"[{{\"EntityName\":\"Book\",\"FieldName\":\"id\",\"FieldValue\":2,\"Direction\":0}}]") + @"""
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
            string after = SqlPaginationUtil.Base64Encode($"[{{\"EntityName\":\"Reviews\",\"FieldName\":\"book_id\",\"FieldValue\":1,\"Direction\":0}}," +
                $"{{\"EntityName\":\"Reviews\",\"FieldName\":\"id\",\"FieldValue\":567,\"Direction\":0}}]");
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
              ""endCursor"": null
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
            string after = SqlPaginationUtil.Base64Encode($"[{{\"EntityName\":\"Books\",\"FieldName\":\"id\",\"FieldValue\":1,\"Direction\":0}}]");
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
              ""endCursor"": null,
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

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: true);
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
                  $"[{{\"EntityName\":\"Stock\",\"FieldName\":\"pieceid\",\"FieldValue\":1,\"Direction\":1}}," +
                  $"{{\"EntityName\":\"Stock\",\"FieldName\":\"categoryid\",\"FieldValue\":0,\"Direction\":0}}]") + @""",
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
                $"[{{\"EntityName\":\"Book\",\"FieldName\":\"title\",\"FieldValue\":\"Time to Eat 2\",\"Direction\":1}}," +
                $"{{\"EntityName\":\"Book\",\"FieldName\":\"publisher_id\",\"FieldValue\":1941,\"Direction\":0}}," +
                $"{{\"EntityName\":\"Book\",\"FieldName\":\"id\",\"FieldValue\":12,\"Direction\":1}}]");

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
                  $"[{{\"EntityName\":\"Book\",\"FieldName\":\"title\",\"FieldValue\":\"The Palace Door\",\"Direction\":1}}," +
                  $"{{\"EntityName\":\"Book\",\"FieldName\":\"publisher_id\",\"FieldValue\":2324,\"Direction\":0}}," +
                  $"{{\"EntityName\":\"Book\",\"FieldName\":\"id\",\"FieldValue\":6,\"Direction\":1}}]");
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
                  $"[{{\"EntityName\":\"Book\",\"FieldName\":\"publisher_id\",\"FieldValue\":2324,\"Direction\":1}}," +
                  $"{{\"EntityName\":\"Book\",\"FieldName\":\"id\",\"FieldValue\":7,\"Direction\":0}}]");
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
                $"[{{\"EntityName\":\"Book\",\"FieldName\":\"publisher_id\",\"FieldValue\":2323,\"Direction\":1}}," +
                $"{{\"EntityName\":\"Book\",\"FieldName\":\"id\",\"FieldValue\":5,\"Direction\":0}}]");
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
                books(first: -2) {
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
        /// Request an invalid number of entries for a pagination page.
        /// Default max page size of config is 100000. Requesting 100001 entries, should lead to an error.
        /// </summary>
        [TestMethod]
        public async Task RequestInvalidMaxSize()
        {
            string graphQLQueryName = "books";
            string graphQLQuery = @"{
                books(first: 100001) {
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
            string after = SqlPaginationUtil.Base64Encode($"[{{\"EntityName\":\"Books\",\"FieldName\":\"title\",\"FieldValue\":\"Great Book\",\"Direction\":0}}]");
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
            string after = SqlPaginationUtil.Base64Encode($"[{{\"EntityName\":\"Books\",\"FieldName\":\"id\",\"FieldValue\":\"two\",\"Direction\":0}}]");
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
            string after = SqlPaginationUtil.Base64Encode($"[{{\"EntityName\":\"Books\",\"FieldName\":\"id\",\"FieldValue\":2,\"Direction\":0}}]");
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
                $"[{{\"EntityName\":\"Books\",\"FieldName\":\"id\",\"FieldValue\":2,\"Direction\":0}}," +
                $"{{\"EntityName\":\"Books\",\"FieldName\":\"publisher_id\",\"FieldValue\":1234,\"Direction\":1}}]");
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
            string after = SqlPaginationUtil.Base64Encode($"[{{\"EntityName\":\"Books\",\"FieldName\":\"id\",\"FieldValue\":2,\"Direction\":0}}]");
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
