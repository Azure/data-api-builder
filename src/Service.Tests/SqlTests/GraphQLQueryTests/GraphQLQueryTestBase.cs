using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Queries;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLQueryTests
{
    /// <summary>
    /// Base class for GraphQL Query tests targetting Sql databases.
    /// </summary>
    [TestClass]
    public abstract class GraphQLQueryTestBase : SqlTestBase
    {
        #region Tests
        /// <summary>
        /// Gets array of results for querying more than one item.
        /// </summary>
        /// <returns></returns>
        public async Task MultipleResultQuery(string dbQuery)
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

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.GetProperty("items").ToString());
        }

        [TestMethod]
        public async Task MultipleResultQueryWithVariables(string dbQuery)
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

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false, new() { { "first", 100 } });
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.GetProperty("items").ToString());
        }

        /// <summary>
        /// Gets array of results for querying a table containing computed columns.
        /// </summary>
        /// <returns>rows from sales table</returns>
        public async Task MultipleResultQueryContainingComputedColumns(string dbQuery)
        {
            string graphQLQueryName = "sales";
            string graphQLQuery = @"{
                sales(first: 10) {
                    items {
                        id
                        item_name
                        subtotal
                        tax
                        total
                    }
                }
            }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.GetProperty("items").ToString());
        }

        /// <summary>
        /// Gets array of results for querying more than one item.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public virtual async Task MultipleResultJoinQuery()
        {
            string graphQLQueryName = "books";
            string graphQLQuery = @"{
                books(first: 12) {
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
        ""items"": [
              {
                ""id"": 126,
                ""name"": ""Aaron""
              }
        ]
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
},
  {
    ""id"": 9,
    ""title"": ""Policy-Test-01"",
    ""publisher_id"": 1940,
    ""publishers"": {
        ""id"": 1940,
      ""name"": ""Policy Publisher 01""
    },
    ""reviews"": {
        ""items"": []
    },
    ""authors"": {
        ""items"": []
    }
},
  {
    ""id"": 10,
    ""title"": ""Policy-Test-02"",
    ""publisher_id"": 1940,
    ""publishers"": {
        ""id"": 1940,
      ""name"": ""Policy Publisher 01""
    },
    ""reviews"": {
        ""items"": []
    },
    ""authors"": {
        ""items"": []
    }
},
  {
    ""id"": 11,
    ""title"": ""Policy-Test-04"",
    ""publisher_id"": 1941,
    ""publishers"": {
        ""id"": 1941,
      ""name"": ""Policy Publisher 02""
    },
    ""reviews"": {
        ""items"": []
    },
    ""authors"": {
        ""items"": []
    }
},
{
    ""id"": 12,
    ""title"": ""Time to Eat 2"",
    ""publisher_id"": 1941,
    ""publishers"": {
        ""id"": 1941,
      ""name"": ""Policy Publisher 02""
    },
    ""reviews"": {
        ""items"": []
    },
    ""authors"": {
        ""items"": []
    }
}
]";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.GetProperty("items").ToString());
        }

        /// <summary>
        /// Test One-To-One relationship both directions
        /// (book -> website placement, website placememnt -> book)
        /// <summary>
        [TestMethod]
        public async Task OneToOneJoinQuery(string dbQuery)
        {
            string graphQLQueryName = "book_by_pk";
            string graphQLQuery = @"query {
                book_by_pk(id: 1) {
                  id
                  websiteplacement {
                    id
                    price
                    books {
                      id
                    }
                  }
                }
            }";

            JsonElement actual = await base.ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// This deeply nests a many-to-one/one-to-many join multiple times to
        /// show that it still results in a valid query.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public virtual async Task DeeplyNestedManyToOneJoinQuery()
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
            await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
        }

        /// <summary>
        /// This deeply nests a many-to-many join multiple times to show that
        /// it still results in a valid query.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public virtual async Task DeeplyNestedManyToManyJoinQuery()
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

            await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
        }

        /// <summary>
        /// This deeply nests a many-to-many join multiple times to show that
        /// it still results in a valid query.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public virtual async Task DeeplyNestedManyToManyJoinQueryWithVariables()
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

            await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false, new() { { "first", 100 } });
        }

        [TestMethod]
        public async Task QueryWithSingleColumnPrimaryKey(string dbQuery)
        {
            string graphQLQueryName = "book_by_pk";
            string graphQLQuery = @"{
                book_by_pk(id: 2) {
                    title
                }
            }";

            JsonElement actual = await base.ExecuteGraphQLRequestAsync(
                graphQLQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        [TestMethod]
        public async Task QueryWithMultipleColumnPrimaryKey(string dbQuery)
        {
            string graphQLQueryName = "review_by_pk";
            string graphQLQuery = @"{
                review_by_pk(id: 568, book_id: 1) {
                    content
                }
            }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        [TestMethod]
        public virtual async Task QueryWithNullResult()
        {
            string graphQLQueryName = "book_by_pk";
            string graphQLQuery = @"{
                book_by_pk(id: -9999) {
                    title
                }
            }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);

            Assert.IsNull(actual.GetString());
        }

        [TestMethod]
        public async Task QueryWithNullableForeignKey(string dbQuery)
        {
            string graphQLQueryName = "comic_by_pk";
            string graphQLQuery = @"{
                comic_by_pk(id: 1) {
                    title
                    myseries {
                        name
                    }
                }
            }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <sumary>
        /// Test if first param successfully limits list quries
        /// </summary>
        [TestMethod]
        public virtual async Task TestFirstParamForListQueries()
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
          },
          {
                        ""title"": ""Before Sunrise""
          }
        ]
      }
            }
        }
]";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.GetProperty("items").ToString());
        }

        /// <sumary>
        /// Test if filter param successfully filters the query results
        /// </summary>
        [TestMethod]
        public virtual async Task TestFilterParamForListQueries()
        {
            string graphQLQueryName = "books";
            string graphQLQuery = @"{
                books( " + QueryBuilder.FILTER_FIELD_NAME + @": {id: {gte: 1} and: [{id: {lte: 4}}]}) {
                    items {
                        id
                        publishers {
                            books(first: 3, " + QueryBuilder.FILTER_FIELD_NAME + @": {id: {neq: 2}}) {
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
                      },
                      {
                        ""id"": 13
                      },
                      {
                        ""id"": 14
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
          },
          {
            ""id"": 13
          },
          {
            ""id"": 14
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

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.GetProperty("items").ToString());
        }

        /// <summary>
        /// Get all instances of a type with nullable interger fields
        /// </summary>
        [TestMethod]
        public async Task TestQueryingTypeWithNullableIntFields(string dbQuery)
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

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.GetProperty("items").ToString());
        }

        /// <summary>
        /// Get all instances of a type with nullable string fields
        /// </summary>
        [TestMethod]
        public async Task TestQueryingTypeWithNullableStringFields(string dbQuery)
        {
            string graphQLQueryName = "websiteUsers";
            string graphQLQuery = @"{
                websiteUsers {
                    items {
                        id
                        username
                    }
                }
            }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.GetProperty("items").ToString());
        }

        /// <summary>
        /// Test to check graphQL support for aliases(arbitrarily set by user while making request).
        /// book_id and book_title are aliases used for corresponding query fields.
        /// The response for the query will contain the alias instead of raw db column.
        /// </summary>
        [TestMethod]
        public async Task TestAliasSupportForGraphQLQueryFields(string dbQuery)
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

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.GetProperty("items").ToString());
        }

        /// <summary>
        /// Test to check graphQL support for aliases(arbitrarily set by user while making request).
        /// book_id is an alias, while title is the raw db field.
        /// The response for the query will use the alias where it is provided in the query.
        /// </summary>
        [TestMethod]
        public async Task TestSupportForMixOfRawDbFieldFieldAndAlias(string dbQuery)
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

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.GetProperty("items").ToString());
        }

        /// <summary>
        /// Tests orderBy on a list query
        /// </summary>
        [TestMethod]
        public async Task TestOrderByInListQuery(string dbQuery)
        {
            string graphQLQueryName = "books";
            string graphQLQuery = @"{
                books(first: 100 orderBy: {title: DESC}) {
                    items {
                        id
                        title
                    }
                }
            }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.GetProperty("items").ToString());
        }

        /// <summary>
        /// Use multiple order options and order an entity with a composite pk
        /// </summary>
        [TestMethod]
        public async Task TestOrderByInListQueryOnCompPkType(string dbQuery)
        {
            string graphQLQueryName = "reviews";
            string graphQLQuery = @"{
                reviews(orderBy: {content: ASC id: DESC}) {
                    items {
                        id
                        content
                    }
                }
            }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.GetProperty("items").ToString());
        }

        /// <summary>
        /// Tests null fields in orderBy are ignored
        /// meaning that null pk columns are included in the ORDER BY clause
        /// as ASC by default while null non-pk columns are completely ignored
        /// </summary>
        [TestMethod]
        public async Task TestNullFieldsInOrderByAreIgnored(string dbQuery)
        {
            string graphQLQueryName = "books";
            string graphQLQuery = @"{
                books(first: 100 orderBy: {title: DESC id: null publisher_id: null}) {
                    items {
                        id
                        title
                    }
                }
            }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.GetProperty("items").ToString());
        }

        /// <summary>
        /// Tests that an orderBy with only null fields results in default pk sorting
        /// </summary>
        [TestMethod]
        public async Task TestOrderByWithOnlyNullFieldsDefaultsToPkSorting(string dbQuery)
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

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.GetProperty("items").ToString());
        }

        /// <summary>
        /// Tests that orderBy order can be set using variable
        /// </summary>
        [TestMethod]
        public async Task TestSettingOrderByOrderUsingVariable(string dbQuery)
        {
            string graphQLQueryName = "books";
            string graphQLQuery = @"query($order: OrderBy)
            {
                books(first: 4 orderBy: {id: $order}) {
                    items {
                        id
                        title
                    }
                }
            }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false, new() { { "order", "DESC" } });
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.GetProperty("items").ToString());
        }

        /// <summary>
        /// Tests setting complex types using variable shows an appropriate error
        /// </summary>
        [TestMethod]
        public virtual async Task TestSettingComplexArgumentUsingVariables(string dbQuery)
        {
            string graphQLQueryName = "books";
            string graphQLQuery = @"query($orderBy: bookOrderByInput)
            {
                books(first: 100 orderBy: $orderBy) {
                    items {
                        id
                        title
                    }
                }
            }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false, new() { { "orderBy", new { id = "ASC" } } });
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.GetProperty("items").ToString());
        }

        /// <summary>
        /// Set query arguments to null explicitly and test t
        /// </summary>
        [TestMethod]
        public virtual async Task TestQueryWithExplicitlyNullArguments(string dbQuery)
        {
            string graphQLQueryName = "books";
            string graphQLQuery = @"{
                books(first: null, after: null, orderBy: null, " + QueryBuilder.FILTER_FIELD_NAME + @": null) {
                    items {
                        id
                        title
                    }
                }
            }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.GetProperty("items").ToString());
        }

        /// <summary>
        /// Query a simple view (contains columns from one table)
        /// </summary>
        [TestMethod]
        public virtual async Task TestQueryOnBasicView(string dbQuery)
        {
            string graphQLQueryName = "books_view_alls";
            string graphQLQuery = @"{
                books_view_alls(first: 5) {
                    items {
                        id
                        title
                    }
                }
            }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.GetProperty("items").ToString());
        }

        /// <summary>
        /// Simple Stored Procedure to check SELECT query returning single row
        /// </summary>
        public async Task TestStoredProcedureQueryForGettingSingleRow(string dbQuery)
        {
            string graphQLQueryName = "GetPublisher";
            string graphQLQuery = @"{
                GetPublisher(id: 1234) {
                    id
                    name
                }
            }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery, expectJson: false);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Simple Stored Procedure to check SELECT query returning multiple rows
        /// </summary>
        public async Task TestStoredProcedureQueryForGettingMultipleRows(string dbQuery)
        {
            string graphQLQueryName = "GetBooks";
            string graphQLQuery = @"{
                GetBooks {
                    id
                    title
                    publisher_id
                }
            }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery, expectJson: false);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Simple Stored Procedure to check COUNT operation
        /// </summary>
        public async Task TestStoredProcedureQueryForGettingTotalNumberOfRows(string dbQuery)
        {
            string graphQLQueryName = "CountBooks";
            string graphQLQuery = @"{
                CountBooks {
                    total_books
                }
            }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery, expectJson: false);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Test to verify Stored Procedure can handle nullable result columns
        /// The result will contain a row which has columns containing null value.
        /// Columns:[first_publish_year and total_books_published] in the result set are nullable.
        /// </summary>
        public async Task TestStoredProcedureQueryWithResultsContainingNull(string dbQuery)
        {
            string graphQLQueryName = "GetAuthorsHistoryByFirstName";
            string graphQLQuery = @"{
                GetAuthorsHistoryByFirstName(firstName: ""Aaron"") {
                    author_name
                    first_publish_year
                    total_books_published
                }
            }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery, expectJson: false);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Query a composite view (contains columns from multiple tables)
        /// </summary>
        [TestMethod]
        public virtual async Task TestQueryOnCompositeView(string dbQuery)
        {
            string graphQLQueryName = "books_publishers_view_composites";
            string graphQLQuery = @"{
                books_publishers_view_composites(first: 5) {
                    items {
                        id
                        name
                    }
                }
            }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.GetProperty("items").ToString());
        }

        #endregion

        #region Negative Tests

        [TestMethod]
        public virtual async Task TestInvalidFirstParamQuery()
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

            JsonElement result = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            SqlTestHelper.TestForErrorInGraphQLResponse(result.ToString(), statusCode: $"{DataApiBuilderException.SubStatusCodes.BadRequest}");
        }

        /// <summary>
        /// Checks failure on providing invalid arguments in graphQL Query
        /// </summary>
        public async Task TestStoredProcedureQueryWithInvalidArgumentType()
        {
            string graphQLQueryName = "GetBook";
            string graphQLQuery = @"{
                GetBook(id: ""3"") {
                    id
                    title
                    publisher_id
                }
            }";

            JsonElement result = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            SqlTestHelper.TestForErrorInGraphQLResponse(result.ToString(), message: "The specified argument value does not match the argument type.");
        }

        [TestMethod]
        public virtual async Task TestInvalidFilterParamQuery()
        {
            string graphQLQueryName = "books";
            string graphQLQuery = @"{
                books( " + QueryBuilder.FILTER_FIELD_NAME + @": ""INVALID"") {
                    items {
                        id
                        title
                    }
                }
            }";

            JsonElement result = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            SqlTestHelper.TestForErrorInGraphQLResponse(result.ToString());
        }

        #endregion
    }
}

