// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Queries;
using Azure.DataApiBuilder.Service.Tests.Configuration;
using Microsoft.AspNetCore.TestHost;
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

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: true);
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
        /// Tests that the following "Find Many" query is properly handled by the engine given that it references
        /// mapped column names "column1" and "column2" and NOT "__column1" nor "__column2"
        /// and that the two mapped names are present in the result set.
        /// </summary>
        /// <param name="dbQuery"></param>
        [TestMethod]
        public async Task MultipleResultQueryWithMappings(string dbQuery)
        {
            string graphQLQueryName = "gQLmappings";

            // "4" references the number of records in the GQLmappings table.
            string graphQLQuery = @"{
                gQLmappings(first: 4) {
                    items {
                        column1
                        column2
                    }
                }
            }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: true);
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
        /// Validates that a list query with only __typename in the selection set
        /// returns the right types
        /// </summary>
        [TestMethod]
        public async Task ListQueryWithoutItemSelectionButWithTypename()
        {
            string graphQLQueryName = "books";
            string graphQLQuery = @"{
                books{
                    __typename
                }
            }";

            string expected = @"
                {
                  ""__typename"": ""bookConnection""
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Validates that a list query against a table with composite Pk
        /// with only __typename in the selection set returns the right types
        /// </summary>
        [TestMethod]
        public async Task ListQueryWithoutItemSelectionButOnlyTypenameAgainstTableWithCompositePK()
        {
            string graphQLQueryName = "stocks";
            string graphQLQuery = @"{
                stocks{
                    __typename
                }
            }";

            string expected = @"
                {
                  ""__typename"": ""StockConnection""
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: true);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Validates that a point query with only __typename field in the selection set
        /// returns the right type
        /// </summary>
        [TestMethod]
        public async Task PointQueryWithTypenameInSelectionSet()
        {
            string graphQLQueryName = "book_by_pk";
            string graphQLQuery = @"{
                book_by_pk(id: 3) {
                    __typename
                }
            }";

            string expected = @"
                {
                    ""__typename"": ""book""
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Validates that a list query with only __typename field in the items section returns the right types
        /// </summary>
        [TestMethod]
        public async Task ListQueryWithOnlyTypenameInSelectionSet()
        {
            string graphQLQueryName = "books";
            string graphQLQuery = @"{
                books(first: 3) {
                    items {
                        __typename
                    }
                }
            }";

            string typename = @"
                {
                    ""__typename"": ""book""
                }
            ";

            // Since the first 3 elements are fetched, we expect the response to contain 3 items
            // with just the __typename field.
            string expected = SqlTestHelper.ConstructGQLTypenameResponseNTimes(typename: typename, times: 3);

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.GetProperty("items").ToString());
        }

        /// <summary>
        /// Validates that a nested point query with only __typename field in each selection set
        /// returns the right types
        /// </summary>
        [TestMethod]
        public async Task NestedPointQueryWithOnlyTypenameInEachSelectionSet()
        {
            string graphQLQueryName = "book_by_pk";
            string graphQLQuery = @"{
                book_by_pk(id: 3) {
                    __typename
                    publishers {
                      __typename
                      books {
                        __typename
                      }
                    }
                }     
            }";

            string expected = @"
                {
                  ""__typename"": ""book"",
                  ""publishers"": {
                    ""__typename"": ""Publisher"",
                    ""books"": {
                      ""__typename"": ""bookConnection""
                    }
                  }
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Validates that querying a SP with only __typename field in the selection set
        /// returns the right type(s)
        /// </summary>
        public async Task QueryAgainstSPWithOnlyTypenameInSelectionSet(string dbQuery)
        {
            string graphQLQueryName = "executeGetBooks";
            string graphQLQuery = @"{
                executeGetBooks{
                    __typename
                }     
            }";

            string typename = @"
                {
                    ""__typename"": ""GetBooks""
                }";

            string bookCountFromDB = await GetDatabaseResultAsync(dbQuery, expectJson: false);
            int expectedCount = JsonSerializer.Deserialize<List<Dictionary<string, int>>>(bookCountFromDB)[0]["count"];
            string expected = SqlTestHelper.ConstructGQLTypenameResponseNTimes(typename: typename, times: expectedCount);
            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Test One-To-One relationship both directions
        /// (book -> website placement, website placememnt -> book)
        /// <summary>
        [TestMethod]
        public async Task OneToOneJoinQuery(string dbQuery)
        {
            string graphQLQueryName = "books";
            string graphQLQuery = @"query {
                books {
                  items {
                    id
                    title
                    websiteplacement {
                    price
                  }
                }
              }
            }";

            JsonElement actual = await base.ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.GetProperty("items").ToString());
        }

        /// <summary>
        /// Test One-To-One relationship when the fields defining
        /// the relationship in the entity include fields that are mapped in
        /// that same entity.
        /// <summary>
        [TestMethod]
        public async Task OneToOneJoinQueryWithMappedFieldNamesInRelationship(string dbQuery)
        {
            string graphQLQueryName = "shrubs";
            string graphQLQuery = @"query {
                shrubs {
                  items {
                    fancyName
                    fungus {
                      habitat
                  }
                }
              }
            }";

            JsonElement actual = await base.ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.GetProperty("items").ToString());
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
        public async Task QueryWithSingleColumnPrimaryKeyAndMappings(string dbQuery)
        {
            string graphQLQueryName = "gQLmappings_by_pk";
            string graphQLQuery = @"{
                gQLmappings_by_pk(column1: 1) {
                    column1
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

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: true);
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
        /// Test where data in the db has a nullable datetime field. The query should successfully return the date in the published_date field if present, else return null.
        /// </summary>
        [TestMethod]
        public async Task TestQueryingTypeWithNullableDateTimeFields(string dbQuery)
        {
            string graphQLQueryName = "supportedTypes";
            string graphQLQuery = @"{
                supportedTypes(first: 100) {
                    items {
                        datetime_types
                    }
                }
            }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: true);
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
        /// This test ensures if a value is specified in the query, it is used
        /// </summary>
        public async Task TestStoredProcedureQueryForGettingSingleRow(string dbQuery)
        {
            string graphQLQueryName = "executeGetPublisher";
            string graphQLQuery = @"query {
                executeGetPublisher(id: 1234) {
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
            string graphQLQueryName = "executeGetBooks";
            string graphQLQuery = @"{
                executeGetBooks {
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
            string graphQLQueryName = "executeCountBooks";
            string graphQLQuery = @"mutation {
                executeCountBooks {
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
            string graphQLQueryName = "executeSearchAuthorByFirstName";
            string graphQLQuery = @"mutation {
                executeSearchAuthorByFirstName(firstName: ""Aaron"") {
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

        /// <summary>
        /// Validates the presence of a resolved __typename in a GraphQL query result payload.
        /// __typename should not be resolved as a database field and should not result in a database error.
        /// </summary>
        [TestMethod]
        public async Task SingleItemQueryWithIntrospectionFields()
        {

            string graphQLQueryName = "book_by_pk";
            string graphQLQuery = @"query {
                book_by_pk(id: 1) {
                  id,
                  __typename
                }
            }";

            string expected = @"
            {
                ""id"": 1,
                ""__typename"": ""book""
            }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Validates the presence of a resolved __typename in a GraphQL query result payload where there
        /// are nested fields. This query returns the __typename of publishers (PublisherConnection) and the
        /// __typename of the items collection (Publisher).
        /// Including the __typename field for items should not result in a database error because __typename
        /// should not be resolved as a field for the Publisher table.
        /// </summary>
        [TestMethod]
        public async Task ListQueryWithIntrospectionFields()
        {

            string graphQLQueryName = "publishers";
            string graphQLQuery = @"query {
                publishers {
                  __typename,
                  items {
                    __typename,
                    name
                  }
                }
            }";

            string expected = @"
            {
              ""__typename"": ""PublisherConnection"",
              ""items"": [
                {
                  ""__typename"": ""Publisher"",
                  ""name"": ""The First Publisher""
                },
                {
                  ""__typename"": ""Publisher"",
                  ""name"": ""Big Company""
                },
                {
                  ""__typename"": ""Publisher"",
                  ""name"": ""Policy Publisher 01""
                },
                {
                  ""__typename"": ""Publisher"",
                  ""name"": ""Policy Publisher 02""
                },
                {
                  ""__typename"": ""Publisher"",
                  ""name"": ""TBD Publishing One""
                },
                {
                  ""__typename"": ""Publisher"",
                  ""name"": ""TBD Publishing Two Ltd""
                },
                {
                  ""__typename"": ""Publisher"",
                  ""name"": ""Small Town Publisher""
                }
              ]
            }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        [TestMethod]
        public async Task CollectionQueryWithInlineFragmentOverlappingFields()
        {
            string query = @"
query {
    books(first: 10) {
        __typename
        items {
            id
            title
            ... on book { id }
        }
    }
}
            ";

            JsonElement response = await ExecuteGraphQLRequestAsync(query, "books", false);

            Assert.AreEqual(10, response.GetProperty("items").GetArrayLength());
        }

        [TestMethod]
        public async Task CollectionQueryWithInlineFragmentNonOverlappingFields()
        {
            string query = @"
query {
    books(first: 10) {
        __typename
        items {
            title
            ... on book { id }
        }
    }
}
            ";

            JsonElement response = await ExecuteGraphQLRequestAsync(query, "books", false);

            Assert.AreEqual(10, response.GetProperty("items").GetArrayLength());
        }

        [TestMethod]
        public async Task CollectionQueryWithFragmentOverlappingFields()
        {
            string query = @"
query {
    books(first: 10) {
        __typename
        items {
            id
            title
            ... b
        }
    }
}

fragment b on book { id }
            ";

            JsonElement response = await ExecuteGraphQLRequestAsync(query, "books", false);

            Assert.AreEqual(10, response.GetProperty("items").GetArrayLength());
        }

        [TestMethod]
        public async Task CollectionQueryWithFragmentNonOverlappingFields()
        {
            string query = @"
query {
    books(first: 10) {
        __typename
        items {
            title
            ... b
        }
    }
}

fragment b on book { id }
            ";

            JsonElement response = await ExecuteGraphQLRequestAsync(query, "books", false);

            Assert.AreEqual(10, response.GetProperty("items").GetArrayLength());
        }

        [TestMethod]
        public async Task QueryWithInlineFragmentOverlappingFields()
        {
            string query = @"
query {
    book_by_pk(id: 1) {
        __typename
        id
        title
        ... on book { id }
    }
}
            ";

            JsonElement response = await ExecuteGraphQLRequestAsync(query, "book_by_pk", false);

            Assert.AreEqual(1, response.GetProperty("id").GetInt32());
        }

        [TestMethod]
        public async Task QueryWithInlineFragmentNonOverlappingFields()
        {
            string query = @"
query {
    book_by_pk(id: 1) {
        __typename
        title
        ... on book { id }
    }
}
            ";

            JsonElement response = await ExecuteGraphQLRequestAsync(query, "book_by_pk", false);

            Assert.AreEqual(1, response.GetProperty("id").GetInt32());
        }

        [TestMethod]
        public async Task QueryWithFragmentOverlappingFields()
        {
            string query = @"
query {
    book_by_pk(id: 1) {
        __typename
        id
        title
        ... p
    }
}

fragment p on book { id }
            ";
            JsonElement response = await ExecuteGraphQLRequestAsync(query, "book_by_pk", false);

            Assert.AreEqual(1, response.GetProperty("id").GetInt32());
        }

        [TestMethod]
        public async Task QueryWithFragmentNonOverlappingFields()
        {
            string query = @"
query {
    book_by_pk(id: 1) {
        __typename
        title
        ... p
    }
}

fragment p on book { id }
            ";

            JsonElement response = await ExecuteGraphQLRequestAsync(query, "book_by_pk", false);

            Assert.AreEqual(1, response.GetProperty("id").GetInt32());
        }

        [TestMethod]
        public async Task GraphQLQueryWithMultipleOfTheSameFieldReturnsFieldOnce()
        {
            string query = @"
query {
    books(first: 10) {
        items {
            id
            id
        }
    }
}
            ";

            JsonElement response = await ExecuteGraphQLRequestAsync(query, "books", false);

            Assert.AreEqual(10, response.GetProperty("items").GetArrayLength());
            Assert.AreEqual(1, response.GetProperty("items").EnumerateArray().First().GetProperty("id").GetInt32());
        }

        /// <summary>
        /// Validates that DAB evaluates the self-joining relationship "child_accounts" for the entity dbo_DimAccounts.
        /// The database schema defines a foreign key relationship between the dbo_DimAccount table and itself:
        /// Referencing field: ParentAccountKey | Referenced field: AccountKey
        /// The field child_accounts represents the one-to-many relationship entry:
        /// - source entity: dbo_DimAccount | source.fields: AccountKey
        /// - target entity: dbo_DimAccount | target.fields: ParentAccountKey
        /// In plain language: one (parent) account whose referenced field AccountKey maps
        /// to many child records' referencing field ParentAccountKey.
        /// </summary>
        [TestMethod]
        public async Task QueryById_SelfReferencingRelationship_ReturnsExpectedChildren()
        {
            string query = @"
            query queryAccountAndParent{
                dbo_DimAccount_by_pk(AccountKey: 2) {
                    AccountKey
                    ParentAccountKey
                    child_accounts {
                        items {
                            AccountKey
                        }
                    }   
                }
            }";

            JsonElement response = await ExecuteGraphQLRequestAsync(query, "dbo_DimAccount_by_pk", false);

            // Expected Response
            /* 
             * {
  "data": {
    "dbo_DimAccount_by_pk": {
      "AccountKey": 2,
      "ParentAccountKey": 1,
      "child_accounts": {
        "items": [
          {
            "AccountKey": 3
          },
          {
            "AccountKey": 4
          }
        ]
      }
    }
  }
}
             */

            Assert.AreEqual(expected: 2, actual: response.GetProperty("AccountKey").GetInt32());
            Assert.AreEqual(expected: 1, actual: response.GetProperty("ParentAccountKey").GetInt32());
            Assert.AreEqual(expected: 2, actual: response.GetProperty("child_accounts").GetProperty("items").GetArrayLength());
            List<JsonElement> childAccounts = response.GetProperty("child_accounts").GetProperty("items").EnumerateArray().ToList();
            Assert.IsTrue(childAccounts[0].GetProperty("AccountKey").GetInt32() == 3);
            Assert.IsTrue(childAccounts[1].GetProperty("AccountKey").GetInt32() == 4);
        }

        /// <summary>
        /// Validates that DAB evaluates the self-joining relationship "parent_account" for the entity dbo_DimAccounts.
        /// The database schema defines a foreign key relationship between the dbo_DimAccount table and itself:
        /// Referencing field: ParentAccountKey | Referenced field: AccountKey
        /// The field parent_account represents the many-to-one relationship entry:
        /// - source entity: dbo_DimAccount | source.fields: ParentAccountKey
        /// - target entity: dbo_DimAccount | target.fields: AccountKey
        /// In plain language: many (child) accounts whose records' referencing field ParentAccountKey map
        /// to a single parent record's referenced field AccountKey.
        /// </summary>
        [TestMethod]
        public async Task QueryMany_SelfReferencingRelationship()
        {
            string query = @"
            query queryAccountAndParent{
                dbo_DimAccounts(first: 1, filter: {ParentAccountKey: { isNull: false}}, orderBy: { AccountKey: ASC}) {
                    items {
                        AccountKey
                        ParentAccountKey
                        parent_account {
                            AccountKey
                        }
                    }
                }
            }";

            JsonElement response = await ExecuteGraphQLRequestAsync(query, "dbo_DimAccounts", false);

            /* Expected Response
{
  "data": {
    "dbo_DimAccounts": {
      "items": [
        {
          "AccountKey": 2,
          "ParentAccountKey": 1,
          "parent_account": {
            "AccountKey": 1
          }
        }
      ]
    }
  }
}
             */
            Assert.AreEqual(expected: 1, actual: response.GetProperty("items").GetArrayLength());
            List<JsonElement> results = response.GetProperty("items").EnumerateArray().ToList();
            Assert.AreEqual(expected: 1, actual: results.Count, message: "More results than expected");
            JsonElement account = results[0];
            Assert.AreEqual(expected: 2, actual: account.GetProperty("AccountKey").GetInt32());
            // help write an assert that checks the parent_account field
            int expectedParentAccountKey = account.GetProperty("ParentAccountKey").GetInt32();
            int relationshipResolvedAccountKey = account.GetProperty("parent_account").GetProperty("AccountKey").GetInt32();
            Assert.AreEqual(expected: expectedParentAccountKey, actual: relationshipResolvedAccountKey);
        }

        #endregion

        #region Negative Tests

        /// <summary>
        /// This test checks the failure on providing invalid first parameter in graphQL Query.
        /// We only allow -1 or positive integers for first parameter.-1 means max page size.
        /// </summary>
        [TestMethod]
        public virtual async Task TestInvalidFirstParamQuery()
        {
            string graphQLQueryName = "books";
            string graphQLQuery = @"{
                books(first: -2) {
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

        /// <summary>
        /// Test to check that sourceFields and targetFields for relationship provided in the config
        /// overrides relationship fields defined in DB.
        /// In this Test the result changes when we override the source and target fields in the config.
        /// </summary>
        [TestMethod]
        public virtual async Task TestConfigTakesPrecedenceForRelationshipFieldsOverDB(
            string[] sourceFields,
            string[] targetFields,
            int club_id,
            string club_name,
            DatabaseType dbType,
            string testEnvironment)
        {
            RuntimeConfig configuration = SqlTestHelper.InitBasicRuntimeConfigWithNoEntity(dbType, testEnvironment);

            Entity clubEntity = new(
                Source: new("clubs", EntitySourceType.Table, null, null),
                Rest: new(Enabled: true),
                GraphQL: new("club", "clubs"),
                Permissions: new[] { ConfigurationTests.GetMinimalPermissionConfig(AuthorizationResolver.ROLE_ANONYMOUS) },
                Relationships: null,
                Mappings: null
            );

            Entity playerEntity = new(
                Source: new("players", EntitySourceType.Table, null, null),
                Rest: new(Enabled: true),
                GraphQL: new("player", "players"),
                Permissions: new[] { ConfigurationTests.GetMinimalPermissionConfig(AuthorizationResolver.ROLE_ANONYMOUS) },
                Relationships: new Dictionary<string, EntityRelationship>() { {"clubs", new (
                    Cardinality: Cardinality.One,
                    TargetEntity: "Club",
                    SourceFields: sourceFields,
                    TargetFields: targetFields,
                    LinkingObject: null,
                    LinkingSourceFields: null,
                    LinkingTargetFields: null
                )}},
                Mappings: null
            );

            Dictionary<string, Entity> entities = new(configuration.Entities) {
                { "Club", clubEntity },
                { "Player", playerEntity }
            };

            RuntimeConfig updatedConfig = configuration
                with
            { Entities = new(entities) };

            const string CUSTOM_CONFIG = "custom-config.json";
            File.WriteAllText(CUSTOM_CONFIG, updatedConfig.ToJson());

            string[] args = new[]
            {
                    $"--ConfigFileName={CUSTOM_CONFIG}"
            };

            using (TestServer server = new(Program.CreateWebHostBuilder(args)))
            using (HttpClient client = server.CreateClient())
            {
                string query = @"{
                    player_by_pk(id: 1) {
                        name,
                        clubs {
                            id
                            name
                        }
                    }
                }";

                object payload = new { query };

                HttpRequestMessage graphQLRequest = new(HttpMethod.Post, "/graphql")
                {
                    Content = JsonContent.Create(payload)
                };

                HttpResponseMessage graphQLResponse = await client.SendAsync(graphQLRequest);
                Assert.AreEqual(System.Net.HttpStatusCode.OK, graphQLResponse.StatusCode);

                string body = await graphQLResponse.Content.ReadAsStringAsync();
                JsonElement graphQLResult = JsonSerializer.Deserialize<JsonElement>(body);
                Assert.AreEqual(club_id, graphQLResult.GetProperty("data").GetProperty("player_by_pk").GetProperty("clubs").GetProperty("id").GetDouble());
                Assert.AreEqual(club_name, graphQLResult.GetProperty("data").GetProperty("player_by_pk").GetProperty("clubs").GetProperty("name").ToString());
            }
        }

        #endregion
    }
}

