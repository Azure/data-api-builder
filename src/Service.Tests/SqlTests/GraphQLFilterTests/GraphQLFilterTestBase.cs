// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Queries;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLFilterTests
{

    [TestClass]
    public abstract class GraphQLFilterTestBase : SqlTestBase
    {
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
        /// Tests eq of StringFilterInput when mappings are configured for GraphQL entity.
        /// </summary>
        [TestMethod]
        public async Task TestStringFiltersEqWithMappings(string dbQuery)
        {
            string graphQLQueryName = "gQLmappings";
            string gqlQuery = @"{
                gQLmappings( " + QueryBuilder.FILTER_FIELD_NAME + @" : {column2: {eq: ""Filtered Record""}})
                {
                    items {
                        column1
                        column2
                    }
                }
            }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Tests IN of StringFilterInput when mappings are configured for GraphQL entity.
        /// </summary>
        [TestMethod]
        public async Task TestStringFiltersINWithMappings(string dbQuery)
        {
            string graphQLQueryName = "gQLmappings";
            string gqlQuery = @"{
                gQLmappings( " + QueryBuilder.FILTER_FIELD_NAME + @" : {column2: {in: [""Filtered Record""]}})
                {
                    items {
                        column1
                        column2
                    }
                }
            }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Tests correct rows are returned with filters containing 2 varchar columns one with null and one with non-null values.
        /// </summary>
        [TestMethod]
        public async Task TestFilterForVarcharColumnWithNullAndNonNullValues()
        {
            string graphQLQueryName = "journals";
            string gqlQuery = @"{
                journals( " + QueryBuilder.FILTER_FIELD_NAME + @" : { and: [ {color: {isNull: true}} {ownername: {eq: ""Abhishek""}}]})
                {
                    items {
                        id
                        journalname
                        color
                        ownername
                    }
                }
            }";

            string dbQuery = MakeQueryOn(
                "journals",
                new List<string> { "id", "journalname", "color", "ownername" },
                "color IS NULL AND ownername = 'Abhishek'",
                GetDefaultSchema());

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: true, clientRoleHeader: "AuthorizationHandlerTester");
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Tests filter operation on column habitat of type varchar(6)
        /// giving correct result.
        /// To verify filter works not only with varchar(max)
        /// </summary>
        [TestMethod]
        public async Task TestFilterForVarcharColumnWithNotMaximumSize()
        {
            string graphQLQueryName = "fungi";
            string gqlQuery = @"{
                fungi( " + QueryBuilder.FILTER_FIELD_NAME + @" :  {habitat: {eq: ""sand""}})
                {
                    items {
                        speciesid
                        region
                        habitat
                    }
                }
            }";

            string dbQuery = MakeQueryOn(
                "fungi",
                new List<string> { "speciesid", "region", "habitat" },
                "habitat = 'sand'",
                GetDefaultSchema(),
                new List<string> { "speciesid" });

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Test that filter values are not truncated to fit the column size.
        /// Here, the habitat column is of size 6 and the filter value is "forestland" which is of size 10.
        /// So, "forestland" should not be truncated to "forest" before matching values in the table.
        /// </summary>
        [TestMethod]
        public async Task TestFilterForVarcharColumnWithNotMaximumSizeAndNoTruncation()
        {
            string graphQLQueryName = "fungi";
            string gqlQuery = @"{
                fungi( " + QueryBuilder.FILTER_FIELD_NAME + @" :  {habitat: {eq: ""forestland""}})
                {
                    items {
                        speciesid
                        region
                        habitat
                    }
                }
            }";

            string dbQuery = MakeQueryOn(
                "fungi",
                new List<string> { "speciesid", "region", "habitat" },
                "habitat = 'forestland'",
                GetDefaultSchema(),
                new List<string> { "speciesid" });

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
        /// Tests various StringFilterInput cases that uses LIKE clause with special characters such as \,_,[,]..
        /// </summary>
        [TestMethod]
        public async Task TestStringFiltersWithSpecialCharacters(string filterParams, string dbQuery)
        {
            // Arrange
            string graphQLQueryName = "books";

            // Construct the GraphQL query by injecting the dynamic filter
            string gqlQuery = @$"{{
                books(filter: {filterParams}, orderBy: {{title: ASC}}) {{
                    items {{
                        title
                    }}
                }}
            }}";

            // Act
            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);

            // // Assert
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

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: true);
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

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: true);
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

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: true);
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

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: true);
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

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: true);
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

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: true);
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

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: true);
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

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: true);
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
                ["id", "title"],
                "id >= 2",
                GetDefaultSchema());

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: true);
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
            string gqlQuery = @"query($and: [bookFilterInput!])
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

        /// <summary>
        /// Test Nested Filter for Many-One relationship.
        /// </summary>
        [TestMethod]
        public async Task TestNestedFilterManyOne(string existsPredicate, string roleName, bool expectsError = false, string errorMsgFragment = "")
        {
            string graphQLQueryName = "comics";
            // Gets all the comics that have their series name = 'Foundation'
            string gqlQuery = @"{
                comics (" + QueryBuilder.FILTER_FIELD_NAME + ": {" +
                    @"myseries: { name: { eq: ""Foundation"" }}})
                    {
                      items {
                        id
                        title
                      }
                    }
                }";

            string dbQuery = MakeQueryOn(
                table: "comics",
                queriedColumns: new List<string> { "id", "title" },
                existsPredicate,
                GetDefaultSchema());

            JsonElement actual = await ExecuteGraphQLRequestAsync(
                gqlQuery,
                graphQLQueryName,
                isAuthenticated: true,
                clientRoleHeader: roleName,
                expectsError: expectsError);

            if (expectsError)
            {
                SqlTestHelper.TestForErrorInGraphQLResponse(actual.ToString(), message: errorMsgFragment);
            }
            else
            {
                string expected = await GetDatabaseResultAsync(dbQuery);
                SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
            }
        }

        /// <summary>
        /// Test Nested Filter for One-Many relationship
        /// </summary>
        [TestMethod]
        public async Task TestNestedFilterOneMany(string existsPredicate, string roleName, bool expectsError = false, string errorMsgFragment = "")
        {
            string graphQLQueryName = "series";
            // Gets the series that have comics with categoryName containing Tales
            string gqlQuery = @"{
                series (" + QueryBuilder.FILTER_FIELD_NAME +
                    @": { comics: { categoryName: { contains: ""Tales"" }}} )
                    {
                      items {
                        id
                        name
                      }
                    }
                }";

            string dbQuery = MakeQueryOn(
                table: "series",
                queriedColumns: new List<string> { "id", "name" },
                existsPredicate,
                GetDefaultSchema());

            JsonElement actual = await ExecuteGraphQLRequestAsync(
                gqlQuery,
                graphQLQueryName,
                isAuthenticated: true,
                clientRoleHeader: roleName,
                expectsError: expectsError);

            if (expectsError)
            {
                SqlTestHelper.TestForErrorInGraphQLResponse(actual.ToString(), message: errorMsgFragment);
            }
            else
            {
                string expected = await GetDatabaseResultAsync(dbQuery);
                SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
            }
        }

        /// <summary>
        /// Test Nested Filter for Many-Many relationship
        /// </summary>
        [TestMethod]
        public async Task TestNestedFilterManyMany(string existsPredicate)
        {
            string graphQLQueryName = "books";
            // Gets the books that have been written by Aaron as author
            string gqlQuery = @"{
                books (" + QueryBuilder.FILTER_FIELD_NAME +
                    @": { authors : { name: { eq: ""Aaron""}}} )
                    {
                      items {
                        title
                      }
                    }
                }";

            string dbQuery = MakeQueryOn(
                table: "books",
                queriedColumns: new List<string> { "title" },
                existsPredicate,
                GetDefaultSchema());

            JsonElement actual = await ExecuteGraphQLRequestAsync(
                gqlQuery,
                graphQLQueryName,
                isAuthenticated: true);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Test a field of the nested filter is null.
        /// </summary>
        [TestMethod]
        public async Task TestNestedFilterFieldIsNull(string existsPredicate, string roleName, bool expectsError = false, string errorMsgFragment = "")
        {
            string graphQLQueryName = "stocks";
            // Gets stocks which have a null price.
            string gqlQuery = @"{
                stocks (" + QueryBuilder.FILTER_FIELD_NAME +
                    @": { stocks_price: { price: { isNull: true }}} )
                    {
                      items {
                        categoryName
                      }
                    }
                }";

            string dbQuery = MakeQueryOn(
                table: "stocks",
                queriedColumns: new List<string> { "categoryName" },
                existsPredicate,
                GetDefaultSchema(),
                pkColumns: new List<string> { "categoryid", "pieceid" });

            JsonElement actual = await ExecuteGraphQLRequestAsync(
                gqlQuery,
                graphQLQueryName,
                isAuthenticated: true,
                clientRoleHeader: roleName,
                expectsError: expectsError);

            if (expectsError)
            {
                SqlTestHelper.TestForErrorInGraphQLResponse(actual.ToString(), message: errorMsgFragment);
            }
            else
            {
                string expected = await GetDatabaseResultAsync(dbQuery);
                SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
            }
        }

        /// <summary>
        /// Tests nested filter having another nested filter.
        /// </summary>
        [TestMethod]
        public async Task TestNestedFilterWithinNestedFilter(string existsPredicate, string roleName, bool expectsError = false, string errorMsgFragment = "")
        {
            string graphQLQueryName = "booksNF";

            // Gets all the books written by Aaron
            // only if the title of one of his books contains 'Awesome'.
            string gqlQuery = @"{
                booksNF (" + QueryBuilder.FILTER_FIELD_NAME +
                    @": { authors: {
                             books: { title: { contains: ""Awesome"" }}
                             name: { eq: ""Aaron"" }
                        }} )
                    {
                      items {
                        title
                      }
                    }
                }";

            string dbQuery = MakeQueryOn(
                table: "books",
                queriedColumns: new List<string> { "title" },
                existsPredicate,
                GetDefaultSchema());

            JsonElement actual = await ExecuteGraphQLRequestAsync(
                gqlQuery,
                graphQLQueryName,
                isAuthenticated: true,
                clientRoleHeader: roleName,
                expectsError: expectsError);

            if (expectsError)
            {
                SqlTestHelper.TestForErrorInGraphQLResponse(actual.ToString(), message: errorMsgFragment);
            }
            else
            {
                string expected = await GetDatabaseResultAsync(dbQuery);
                SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
            }
        }

        /// <summary>
        /// Tests nested filter and an AND clause.
        /// </summary>
        [TestMethod]
        public async Task TestNestedFilterWithAnd(string existsPredicate, string roleName, bool expectsError = false, string errorMsgFragment = "")
        {
            string graphQLQueryName = "booksNF";

            // Gets all the books written by Aniruddh and the publisher is 'Small Town Publisher'.
            string gqlQuery = @"{
                booksNF (" + QueryBuilder.FILTER_FIELD_NAME +
                    @": { publishers: { name: { eq: ""Small Town Publisher"" } }
                      and: { authors: {name: { eq: ""Aniruddh"" } }
                    }})
                    {
                      items {
                        title
                      }
                    }
                }";

            string dbQuery = MakeQueryOn(
                table: "books",
                queriedColumns: new List<string> { "title" },
                existsPredicate,
                GetDefaultSchema());

            JsonElement actual = await ExecuteGraphQLRequestAsync(
                gqlQuery,
                graphQLQueryName,
                isAuthenticated: true,
                clientRoleHeader: roleName,
                expectsError: expectsError);

            if (expectsError)
            {
                SqlTestHelper.TestForErrorInGraphQLResponse(actual.ToString(), message: errorMsgFragment);
            }
            else
            {
                string expected = await GetDatabaseResultAsync(dbQuery);
                SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
            }
        }

        /// <summary>
        /// Tests nested filter alongwith an OR clause.
        /// </summary>
        [TestMethod]
        public async Task TestNestedFilterWithOr(string existsPredicate, string roleName, bool expectsError = false, string errorMsgFragment = "")
        {
            string graphQLQueryName = "booksNF";

            // Gets all the books written by Aniruddh OR if their publisher is 'TBD Publishing One'.
            string gqlQuery = @"{
                booksNF (" + QueryBuilder.FILTER_FIELD_NAME +
                    @": { or: [{
                        publishers: { name: { eq: ""TBD Publishing One"" } } }
                        { authors : {
                          name: { eq: ""Aniruddh""}}}
                      ]
                    })
                    {
                      items {
                        title
                      }
                    }
                }";

            string dbQuery = MakeQueryOn(
                table: "books",
                queriedColumns: new List<string> { "title" },
                existsPredicate,
                GetDefaultSchema());

            JsonElement actual = await ExecuteGraphQLRequestAsync(
                gqlQuery,
                graphQLQueryName,
                isAuthenticated: true,
                clientRoleHeader: roleName,
                expectsError: expectsError);

            if (expectsError)
            {
                SqlTestHelper.TestForErrorInGraphQLResponse(actual.ToString(), message: errorMsgFragment);
            }
            else
            {
                string expected = await GetDatabaseResultAsync(dbQuery);
                SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
            }
        }

        /// <summary>
        /// Tests nested filter with IN and OR clause.
        /// </summary>
        [TestMethod]
        public async Task TestNestedFilterWithOrAndIN(string existsPredicate, string roleName, bool expectsError = false, string errorMsgFragment = "")
        {
            string graphQLQueryName = "booksNF";

            // Gets all the books written by Aniruddh OR if their publisher is 'TBD Publishing One'.
            string gqlQuery = @"{
                booksNF (" + QueryBuilder.FILTER_FIELD_NAME +
                    @": { or: [{
                        publishers: { name: { in: [""TBD Publishing One""] } } }
                        { authors : {
                          name: { in: [""Aniruddh""] }}}
                      ]
                    })
                    {
                      items {
                        title
                      }
                    }
                }";

            string dbQuery = MakeQueryOn(
                table: "books",
                queriedColumns: new List<string> { "title" },
                existsPredicate,
                GetDefaultSchema());

            JsonElement actual = await ExecuteGraphQLRequestAsync(
                gqlQuery,
                graphQLQueryName,
                isAuthenticated: true,
                clientRoleHeader: roleName,
                expectsError: expectsError);

            if (expectsError)
            {
                SqlTestHelper.TestForErrorInGraphQLResponse(actual.ToString(), message: errorMsgFragment);
            }
            else
            {
                string expected = await GetDatabaseResultAsync(dbQuery);
                SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
            }
        }

        #endregion

        protected abstract string GetDefaultSchema();

        /// <summary>
        /// Formats the default schema so that it can be
        /// placed right before the identity that it is qualifying
        /// </summary>
        protected string GetPreIndentDefaultSchema()
        {
            string defaultSchema = GetDefaultSchema();
            return string.IsNullOrEmpty(defaultSchema) ? string.Empty : defaultSchema + ".";
        }

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
        protected override async Task<JsonElement> ExecuteGraphQLRequestAsync(
            string graphQLQuery,
            string graphQLQueryName,
            bool isAuthenticated,
            Dictionary<string, object> variables = null,
            string clientRoleHeader = null,
            bool expectsError = false)
        {
            JsonElement dataResult = await base.ExecuteGraphQLRequestAsync(
                graphQLQuery,
                graphQLQueryName,
                isAuthenticated,
                variables,
                clientRoleHeader);

            if (expectsError)
            {
                // ExecuteGraphQLRequestAsync returns the error property when an error is encountered.
                // Do not further filter the returned property.
                return dataResult;
            }

            return dataResult.GetProperty("items");
        }
    }
}
