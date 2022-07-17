using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests.GraphQLQueryTests
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

            string actual = await this.GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, HttpClient);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
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

            string actual = await this.GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, HttpClient, new() { { "first", 100 } });
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
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

            string actual = await this.GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, HttpClient);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
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

            string actual = await base.GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, HttpClient);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
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
            await this.GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, HttpClient);
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

            await this.GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, HttpClient);
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

            await this.GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, HttpClient,
                new() { { "first", 100 } });

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

            string actual = await base.GetGraphQLResultAsync(
                graphQLQuery, graphQLQueryName, HttpClient);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        [TestMethod]
        public async Task QueryWithMultileColumnPrimaryKey(string dbQuery)
        {
            string graphQLQueryName = "review_by_pk";
            string graphQLQuery = @"{
                review_by_pk(id: 568, book_id: 1) {
                    content
                }
            }";

            string actual = await base.GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, HttpClient);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
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

            string actual = await base.GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, HttpClient);

            SqlTestHelper.PerformTestEqualJsonStrings("null", actual);
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
          }
        ]
      }
            }
        }
]";

            string actual = await this.GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, HttpClient);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <sumary>
        /// Test if filter param successfully filters the query results
        /// </summary>
        [TestMethod]
        public virtual async Task TestFilterParamForListQueries()
        {
            string graphQLQueryName = "books";
            string graphQLQuery = @"{
                books(_filter: {id: {gte: 1} and: [{id: {lte: 4}}]}) {
                    items {
                        id
                        publishers {
                            books(first: 3, _filter: {id: {neq: 2}}) {
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

            string actual = await this.GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, HttpClient);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
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

            string actual = await this.GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, HttpClient);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
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

            string actual = await this.GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, HttpClient);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
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

            string actual = await this.GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, HttpClient);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
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

            string actual = await this.GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, HttpClient);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
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

            string actual = await this.GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, HttpClient);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
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

            string actual = await this.GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, HttpClient);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
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

            string actual = await this.GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, HttpClient);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
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

            string actual = await this.GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, HttpClient);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
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

            JsonElement result = await SqlTestBase.GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, HttpClient);
            SqlTestHelper.TestForErrorInGraphQLResponse(result.ToString(), statusCode: $"{DataGatewayException.SubStatusCodes.BadRequest}");
        }

        [TestMethod]
        public virtual async Task TestInvalidFilterParamQuery()
        {
            string graphQLQueryName = "books";
            string graphQLQuery = @"{
                books(_filter: ""INVALID"") {
                    items {
                        id
                        title
                    }
                }
            }";

            JsonElement result = await SqlTestBase.GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, HttpClient);
            SqlTestHelper.TestForErrorInGraphQLResponse(result.ToString());
        }

        #endregion

        protected override async Task<string> GetGraphQLResultAsync(
            string graphQLQuery, string graphQLQueryName,
            HttpClient httpClient,
            Dictionary<string, object> variables = null,
            bool failOnErrors = true)
        {
            string dataResult = await base.GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, httpClient, variables, failOnErrors);

            return JsonDocument.Parse(dataResult).RootElement.GetProperty("items").ToString();
        }
    }
}

