using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Queries;
using Azure.DataApiBuilder.Service.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLFilterTests
{

    [TestClass]
    public abstract class GraphQLFilterTestBase : SqlTestBase
    {

        #region Test Fixture Setup
        protected static GraphQLSchemaCreator _graphQLService;

        #endregion

        #region Tests

        /// <summary>
        /// Tests eq of StringFilterInput
        /// </summary>
        [TestMethod]
        public async Task TestStringFiltersEq()
        {
            string graphQLQueryName = "books";
            string gqlQuery = @"{
                books( " + QueryBuilder.FILTER_FIELD_NAME + @" : {title: {eq: ""Awesome book""}})
                {
                    items {
                        title
                    }
                }
            }";

            string dbQuery = MakeQueryOn(
                "books",
                new List<string> { "title" },
                "title = 'Awesome book'",
                GetDefaultSchema());

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Tests neq of StringFilterInput
        /// </summary>
        [TestMethod]
        public async Task TestStringFiltersNeq()
        {
            string graphQLQueryName = "books";
            string gqlQuery = @"{
               books( " + QueryBuilder.FILTER_FIELD_NAME + @" : {title: {neq: ""Awesome book""}})
                {
                    items {
                        title
                    }
                }
            }";

            string dbQuery = MakeQueryOn(
                "books",
                new List<string> { "title" },
                "title != 'Awesome book'",
                GetDefaultSchema());

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Tests startsWith of StringFilterInput
        /// </summary>
        [TestMethod]
        public async Task TestStringFiltersStartsWith()
        {
            string graphQLQueryName = "books";
            string gqlQuery = @"{
                books( " + QueryBuilder.FILTER_FIELD_NAME + @" : {title: {startsWith: ""Awe""}})
                {
                    items {
                        title
                    }
                }
            }";

            string dbQuery = MakeQueryOn(
                "books",
                new List<string> { "title" },
                "title LIKE 'Awe%'",
                GetDefaultSchema());

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Tests endsWith of StringFilterInput
        /// </summary>
        [TestMethod]
        public async Task TestStringFiltersEndsWith()
        {
            string graphQLQueryName = "books";
            string gqlQuery = @"{
                books( " + QueryBuilder.FILTER_FIELD_NAME + @" : {title: {endsWith: ""book""}})
                {
                    items {
                        title
                    }
                }
            }";

            string dbQuery = MakeQueryOn(
                "books",
                new List<string> { "title" },
                "title LIKE '%book'",
                GetDefaultSchema());

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Tests contains of StringFilterInput
        /// </summary>
        [TestMethod]
        public async Task TestStringFiltersContains()
        {
            string graphQLQueryName = "books";
            string gqlQuery = @"{
                books( " + QueryBuilder.FILTER_FIELD_NAME + @" : {title: {contains: ""some""}})
                {
                    items {
                        title
                    }
                }
            }";

            string dbQuery = MakeQueryOn(
                "books",
                new List<string> { "title" },
                "title LIKE '%some%'",
                GetDefaultSchema());

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Tests notContains of StringFilterInput
        /// </summary>
        [TestMethod]
        public async Task TestStringFiltersNotContains()
        {
            string graphQLQueryName = "books";
            string gqlQuery = @"{
                books( " + QueryBuilder.FILTER_FIELD_NAME + @" :{title: {notContains: ""book""}})
                {
                    items {
                        title
                    }
                }
            }";

            string dbQuery = MakeQueryOn(
                "books",
                new List<string> { "title" },
                "title NOT LIKE '%book%'",
                GetDefaultSchema());

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Tests that special characters are escaped in operations involving LIKE
        /// </summary>
        [TestMethod]
        public async Task TestStringFiltersContainsWithSpecialChars()
        {
            string graphQLQueryName = "books";
            string gqlQuery = @"{
                books( " + QueryBuilder.FILTER_FIELD_NAME + @" : {title: {contains: ""%""}})
                {
                    items {
                        title
                    }
                }
            }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: false);
            SqlTestHelper.PerformTestEqualJsonStrings("[]", actual.ToString());
        }

        /// <summary>
        /// Tests eq of IntFilterInput
        /// </summary>
        [TestMethod]
        public async Task TestIntFiltersEq()
        {
            string graphQLQueryName = "books";
            string gqlQuery = @"{
                books( " + QueryBuilder.FILTER_FIELD_NAME + @" : {id: {eq: 2}})
                {
                    items {
                        id
                    }
                }
            }";

            string dbQuery = MakeQueryOn(
                "books",
                new List<string> { "id" },
                @"id = 2",
                GetDefaultSchema());

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Tests neq of IntFilterInput
        /// </summary>
        [TestMethod]
        public async Task TestIntFiltersNeq()
        {
            string graphQLQueryName = "books";
            string gqlQuery = @"{
                books( " + QueryBuilder.FILTER_FIELD_NAME + @" : {id: {neq: 2}})
                {
                    items {
                        id
                    }
                }
            }";

            string dbQuery = MakeQueryOn(
                "books",
                new List<string> { "id" },
                @"id != 2",
                GetDefaultSchema());

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Tests gt and lt of IntFilterInput
        /// </summary>
        [TestMethod]
        public async Task TestIntFiltersGtLt()
        {
            string graphQLQueryName = "books";
            string gqlQuery = @"{
                books( " + QueryBuilder.FILTER_FIELD_NAME + @" : {id: {gt: 2 lt: 4}})
                {
                    items {
                        id
                    }
                }
            }";

            string dbQuery = MakeQueryOn(
                "books",
                new List<string> { "id" },
                @"(id > 2 AND id < 4)",
                GetDefaultSchema());

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Tests gte and lte of IntFilterInput
        /// </summary>
        [TestMethod]
        public async Task TestIntFiltersGteLte()
        {
            string graphQLQueryName = "books";
            string gqlQuery = @"{
                books( " + QueryBuilder.FILTER_FIELD_NAME + @" : {id: {gte: 2 lte: 4}})
                {
                    items {
                        id
                    }
                }
            }";

            string dbQuery = MakeQueryOn(
                "books",
                new List<string> { "id" },
                @"(id >= 2 AND id <= 4)",
                GetDefaultSchema());

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Test that:
        /// - the predicate equivalent of *FilterInput input types is put in parenthesis if the
        ///   predicate
        /// - the predicate equivalent of and / or field is put in parenthesis if the predicate
        ///   contains only one operation
        /// </summary>
        /// <remarks>
        /// one operation predicate: id == 2
        /// multiple operation predicate: id == 2 AND publisher_id < 3
        /// </remarks>
        public async Task TestCreatingParenthesis1()
        {
            string graphQLQueryName = "books";
            string gqlQuery = @"{
                books( " + QueryBuilder.FILTER_FIELD_NAME + @" : {
                                    title: {contains: ""book""}
                                    or: [
                                        {id:{gt: 2 lt: 4}},
                                        {id: {gte: 4}},
                                    ]
                                })
                {
                    items {
                        id
                        title
                    }
                }
            }";

            string dbQuery = MakeQueryOn(
                "books",
                new List<string> { "id", "title" },
                @"(title LIKE '%book%' AND ((id > 2 AND id < 4) OR id >= 4))",
                GetDefaultSchema());

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Test that:
        /// - the predicate equivalent of *FilterInput input types is put in parenthesis if the
        ///   predicate
        /// - the predicate equivalent of and / or field is put in parenthesis if the predicate
        ///   contains only one operation
        /// </summary>
        /// <remarks>
        /// one operation predicate: id == 2
        /// multiple operation predicate: id == 2 AND publisher_id < 3
        /// </remarks>
        public async Task TestCreatingParenthesis2()
        {
            string graphQLQueryName = "books";
            string gqlQuery = @"{
                books( " + QueryBuilder.FILTER_FIELD_NAME + @" : {
                                    or: [
                                        {id: {gt: 2} and: [{id: {lt: 4}}]},
                                        {id: {gte: 4} title: {contains: ""book""}}
                                    ]
                                })
                {
                    items {
                        id
                        title
                    }
                }
            }";

            string dbQuery = MakeQueryOn(
                "books",
                new List<string> { "id", "title" },
                @"((id > 2 AND id < 4) OR (id >= 4 AND title LIKE '%book%'))",
                GetDefaultSchema());

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Test that a complicated filter is evaluated as:
        /// - all non and/or fields of each *FilterInput are AND-ed together and put in parenthesis
        /// - each *FilterInput inside an and/or team is AND/OR-ed together and put in parenthesis
        /// - the final predicate is:
        ///   ((<AND-ed non and/or predicates>) AND (<AND-ed predicates in and filed>) OR <OR-ed predicates in or field>)
        /// </summart>
        public async Task TestComplicatedFilter()
        {
            string graphQLQueryName = "books";
            string gqlQuery = @"{
                books( " + QueryBuilder.FILTER_FIELD_NAME + @" : {
                                    id: {gte: 2}
                                    title: {notContains: ""book""}
                                    and: [
                                        {
                                            id: {lt: 1000}
                                            title: {startsWith: ""US""}
                                        },
                                        {
                                            title: {endsWith: ""Diaries""}
                                            id: {neq: 3}
                                        }
                                    ]
                                    or: [
                                        {publisher_id: {gt: 2000}},
                                        {publisher_id: {lt: 1500}},
                                    ]
                                })
                {
                    items {
                        id
                        title
                        publisher_id
                    }
                }
            }";

            string dbQuery = MakeQueryOn(
                "books",
                new List<string> { "id", "title", "publisher_id" },
                @"((id >= 2 AND title NOT LIKE '%book%') AND
                  (id < 1000 AND title LIKE 'US%') AND
                  (publisher_id < 1500 OR publisher_id > 2000)",
                GetDefaultSchema());

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Test that an empty and evaluates to False
        /// </summary>
        [TestMethod]
        public async Task TestOnlyEmptyAnd()
        {
            string graphQLQueryName = "books";
            string gqlQuery = @"{
                books( " + QueryBuilder.FILTER_FIELD_NAME + @" : {and: []})
                {
                    items {
                        id
                    }
                }
            }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: false);
            SqlTestHelper.PerformTestEqualJsonStrings("[]", actual.ToString());
        }

        /// <summary>
        /// Test that an empty or evaluates to False
        /// </summary>
        [TestMethod]
        public async Task TestOnlyEmptyOr()
        {
            string graphQLQueryName = "books";
            string gqlQuery = @"{
                books( " + QueryBuilder.FILTER_FIELD_NAME + @" : {or: []})
                {
                    items {
                        id
                    }
                }
            }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: false);
            SqlTestHelper.PerformTestEqualJsonStrings("[]", actual.ToString());
        }

        /// <summary>
        /// Test filtering null integer fields
        /// </summary>
        [TestMethod]
        public async Task TestGetNullIntFields()
        {
            string graphQLQueryName = "magazines";
            string gqlQuery = @"{
                magazines( " + QueryBuilder.FILTER_FIELD_NAME + @" : { issue_number: {isNull: true}}) {
                    items {
                        id
                        title
                        issue_number
                        }
                    }
                }";

            string dbQuery = MakeQueryOn(
                "magazines",
                new List<string> { "id", "title", "issue_number" },
                "issue_number IS NULL",
                "foo");

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Test filtering non null integer fields
        /// </summary>
        [TestMethod]
        public async Task TestGetNonNullIntFields()
        {
            string graphQLQueryName = "magazines";
            string gqlQuery = @"{
                magazines( " + QueryBuilder.FILTER_FIELD_NAME + @" : { issue_number: {isNull: false}}) {
                    items {
                        id
                        title
                        issue_number
                    }
                }
            }";

            string dbQuery = MakeQueryOn(
                "magazines",
                new List<string> { "id", "title", "issue_number" },
                "issue_number IS NOT NULL",
                "foo");

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Test filtering null string fields
        /// </summary>
        [TestMethod]
        public async Task TestGetNullStringFields()
        {
            string graphQLQueryName = "websiteUsers";
            string gqlQuery = @"{
                websiteUsers( " + QueryBuilder.FILTER_FIELD_NAME + @" : {username: {isNull: true}}) {
                    items {
                        id
                        username
                    }
                }
            }";

            string dbQuery = MakeQueryOn(
                "website_users",
                new List<string> { "id", "username" },
                "username IS NULL",
                GetDefaultSchema());

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Test filtering not null string fields
        /// </summary>
        [TestMethod]
        public async Task TestGetNonNullStringFields()
        {
            string graphQLQueryName = "websiteUsers";
            string gqlQuery = @"{
                websiteUsers( " + QueryBuilder.FILTER_FIELD_NAME + @" : {username: {isNull: false}}) {
                    items {
                        id
                        username
                    }
                }
            }";

            string dbQuery = MakeQueryOn(
                "website_users",
                new List<string> { "id", "username" },
                "username IS NOT NULL",
                GetDefaultSchema());

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Passes null to nullable fields and makes sure they are ignored
        /// </summary>
        [TestMethod]
        public async Task TestExplicitNullFieldsAreIgnored()
        {
            string graphQLQueryName = "books";
            string gqlQuery = @"{
                books( " + QueryBuilder.FILTER_FIELD_NAME + @" : {
                                    id: {gte: 2 lte: null}
                                    title: null
                                    or: null
                                  })
                {
                    items {
                        id
                        title
                    }
                }
            }";

            string dbQuery = MakeQueryOn(
                "books",
                new List<string> { "id", "title" },
                @"id >= 2",
                GetDefaultSchema());

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Passes null to nullable fields and makes sure they are ignored
        /// </summary>
        public async Task TestInputObjectWithOnlyNullFieldsEvaluatesToFalse()
        {
            string graphQLQueryName = "books";
            string gqlQuery = @"{
                getbooks( " + QueryBuilder.FILTER_FIELD_NAME + @" : {id: {lte: null}})
                {
                    items {
                        id
                    }
                }
            }";

            string dbQuery = MakeQueryOn(
                "books",
                new List<string> { "id" },
                @"1 != 1",
                GetDefaultSchema());

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Test passing variable to filter input type fields
        /// </summary>
        [TestMethod]
        public async Task TestPassingVariablesToFilter()
        {
            string graphQLQueryName = "books";
            string gqlQuery = @"query($lteValue: Int!, $gteValue: Int!)
            {
                books(" + QueryBuilder.FILTER_FIELD_NAME + @": {id: {lte: $lteValue} and: [{id: {gte: $gteValue}}]})
                {
                    items {
                        id
                    }
                }
            }";

            string dbQuery = MakeQueryOn(
                "books",
                new List<string> { "id" },
                @"id <= 4 AND id >= 2",
                GetDefaultSchema());

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: false, new() { { "lteValue", 4 }, { "gteValue", 2 } });
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Test passing variable to and field
        /// </summary>
        [TestMethod]
        public async Task TestPassingVariablesToAndField()
        {
            string graphQLQueryName = "books";
            string gqlQuery = @"query($and: [BookFilterInput!])
            {
                books(" + QueryBuilder.FILTER_FIELD_NAME + @": {and: $and})
                {
                    items {
                        id
                    }
                }
            }";

            string dbQuery = MakeQueryOn(
                "books",
                new List<string> { "id" },
                @"id < 3",
                GetDefaultSchema());

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: false, new() { { "and", new[] { new { id = new { lt = 3 } } } } });
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        #endregion

        protected abstract string GetDefaultSchema();

        /// <remarks>
        /// This function does not escape special characters from column names so those might lead to errors
        /// </remarks>
        protected abstract string MakeQueryOn(
            string table,
            List<string> queriedColumns,
            string predicate,
            string schema = "",
            List<string> pkColumns = null);

        /// <summary>
        /// Method used to execute GraphQL requests.
        /// For list results, returns the JsonElement representative of the property 'items'
        /// </summary>
        /// <param name="graphQLQuery"></param>
        /// <param name="graphQLQueryName"></param>
        /// <param name="isAuthenticated"></param>
        /// <param name="variables"></param>
        /// <param name="clientRoleHeader"></param>
        /// <returns></returns>
        protected override async Task<JsonElement> ExecuteGraphQLRequestAsync(
            string graphQLQuery,
            string graphQLQueryName,
            bool isAuthenticated,
            Dictionary<string, object> variables = null,
            string clientRoleHeader = null,
            bool failOnError = true)
        {
            JsonElement dataResult = await base.ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: isAuthenticated, variables);

            return dataResult.GetProperty("items");
        }
    }
}
