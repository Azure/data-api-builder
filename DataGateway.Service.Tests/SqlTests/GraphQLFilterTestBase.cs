using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests
{

    [TestClass]
    public abstract class GraphQLFilterTestBase : SqlTestBase
    {

        #region Test Fixture Setup
        protected static GraphQLService _graphQLService;
        protected static GraphQLController _graphQLController;

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
                books(_filter: {title: {eq: ""Awesome book""}})
                {
                    items {
                        title
                    }
                }
            }";

            string dbQuery = MakeQueryOn(
                "books",
                new List<string> { "title" },
                "title = 'Awesome book'");

            string actual = await GetGraphQLResultAsync(gqlQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Tests neq of StringFilterInput
        /// </summary>
        [TestMethod]
        public async Task TestStringFiltersNeq()
        {
            string graphQLQueryName = "books";
            string gqlQuery = @"{
                books(_filter: {title: {neq: ""Awesome book""}})
                {
                    items {
                        title
                    }
                }
            }";

            string dbQuery = MakeQueryOn(
                "books",
                new List<string> { "title" },
                "title != 'Awesome book'");

            string actual = await GetGraphQLResultAsync(gqlQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Tests startsWith of StringFilterInput
        /// </summary>
        [TestMethod]
        public async Task TestStringFiltersStartsWith()
        {
            string graphQLQueryName = "books";
            string gqlQuery = @"{
                books(_filter: {title: {startsWith: ""Awe""}})
                {
                    items {
                        title
                    }
                }
            }";

            string dbQuery = MakeQueryOn(
                "books",
                new List<string> { "title" },
                "title LIKE 'Awe%'");

            string actual = await GetGraphQLResultAsync(gqlQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Tests endsWith of StringFilterInput
        /// </summary>
        [TestMethod]
        public async Task TestStringFiltersEndsWith()
        {
            string graphQLQueryName = "books";
            string gqlQuery = @"{
                books(_filter: {title: {endsWith: ""book""}})
                {
                    items {
                        title
                    }
                }
            }";

            string dbQuery = MakeQueryOn(
                "books",
                new List<string> { "title" },
                "title LIKE '%book'");

            string actual = await GetGraphQLResultAsync(gqlQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Tests contains of StringFilterInput
        /// </summary>
        [TestMethod]
        public async Task TestStringFiltersContains()
        {
            string graphQLQueryName = "books";
            string gqlQuery = @"{
                books(_filter: {title: {contains: ""some""}})
                {
                    items {
                        title
                    }
                }
            }";

            string dbQuery = MakeQueryOn(
                "books",
                new List<string> { "title" },
                "title LIKE '%some%'");

            string actual = await GetGraphQLResultAsync(gqlQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Tests notContains of StringFilterInput
        /// </summary>
        [TestMethod]
        public async Task TestStringFiltersNotContains()
        {
            string graphQLQueryName = "books";
            string gqlQuery = @"{
                books(_filter: {title: {notContains: ""book""}})
                {
                    items {
                        title
                    }
                }
            }";

            string dbQuery = MakeQueryOn(
                "books",
                new List<string> { "title" },
                "title NOT LIKE '%book%'");

            string actual = await GetGraphQLResultAsync(gqlQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Tests that special characters are escaped in operations involving LIKE
        /// </summary>
        [TestMethod]
        public async Task TestStringFiltersContainsWithSpecialChars()
        {
            string graphQLQueryName = "books";
            string gqlQuery = @"{
                books(_filter: {title: {contains: ""%""}})
                {
                    items {
                        title
                    }
                }
            }";

            string actual = await GetGraphQLResultAsync(gqlQuery, graphQLQueryName, _graphQLController);
            SqlTestHelper.PerformTestEqualJsonStrings("[]", actual);
        }

        /// <summary>
        /// Tests eq of IntFilterInput
        /// </summary>
        [TestMethod]
        public async Task TestIntFiltersEq()
        {
            string graphQLQueryName = "books";
            string gqlQuery = @"{
                books(_filter: {id: {eq: 2}})
                {
                    items {
                        id
                    }
                }
            }";

            string dbQuery = MakeQueryOn(
                "books",
                new List<string> { "id" },
                @"id = 2");

            string actual = await GetGraphQLResultAsync(gqlQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Tests neq of IntFilterInput
        /// </summary>
        [TestMethod]
        public async Task TestIntFiltersNeq()
        {
            string graphQLQueryName = "books";
            string gqlQuery = @"{
                books(_filter: {id: {neq: 2}})
                {
                    items {
                        id
                    }
                }
            }";

            string dbQuery = MakeQueryOn(
                "books",
                new List<string> { "id" },
                @"id != 2");

            string actual = await GetGraphQLResultAsync(gqlQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Tests gt and lt of IntFilterInput
        /// </summary>
        [TestMethod]
        public async Task TestIntFiltersGtLt()
        {
            string graphQLQueryName = "books";
            string gqlQuery = @"{
                books(_filter: {id: {gt: 2 lt: 4}})
                {
                    items {
                        id
                    }
                }
            }";

            string dbQuery = MakeQueryOn(
                "books",
                new List<string> { "id" },
                @"(id > 2 AND id < 4)");

            string actual = await GetGraphQLResultAsync(gqlQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Tests gte and lte of IntFilterInput
        /// </summary>
        [TestMethod]
        public async Task TestIntFiltersGteLte()
        {
            string graphQLQueryName = "books";
            string gqlQuery = @"{
                books(_filter: {id: {gte: 2 lte: 4}})
                {
                    items {
                        id
                    }
                }
            }";

            string dbQuery = MakeQueryOn(
                "books",
                new List<string> { "id" },
                @"(id >= 2 AND id <= 4)");

            string actual = await GetGraphQLResultAsync(gqlQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
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
                books(_filter: {
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
                @"(title LIKE '%book%' AND ((id > 2 AND id < 4) OR id >= 4))");

            string actual = await GetGraphQLResultAsync(gqlQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
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
                books(_filter: {
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
                @"((id > 2 AND id < 4) OR (id >= 4 AND title LIKE '%book%'))");

            string actual = await GetGraphQLResultAsync(gqlQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
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
                books(_filter: {
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
                  (publisher_id < 1500 OR publisher_id > 2000)");

            string actual = await GetGraphQLResultAsync(gqlQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Test that an empty and evaluates to False
        /// </summary>
        [TestMethod]
        public async Task TestOnlyEmptyAnd()
        {
            string graphQLQueryName = "books";
            string gqlQuery = @"{
                books(_filter: {and: []})
                {
                    items {
                        id
                    }
                }
            }";

            string actual = await GetGraphQLResultAsync(gqlQuery, graphQLQueryName, _graphQLController);
            SqlTestHelper.PerformTestEqualJsonStrings("[]", actual);
        }

        /// <summary>
        /// Test that an empty or evaluates to False
        /// </summary>
        [TestMethod]
        public async Task TestOnlyEmptyOr()
        {
            string graphQLQueryName = "books";
            string gqlQuery = @"{
                books(_filter: {or: []})
                {
                    items {
                        id
                    }
                }
            }";

            string actual = await GetGraphQLResultAsync(gqlQuery, graphQLQueryName, _graphQLController);
            SqlTestHelper.PerformTestEqualJsonStrings("[]", actual);
        }

        /// <summary>
        /// Test that filters applied by the typed filter and the OData filter
        /// are AND-ed
        /// </summary>
        [TestMethod]
        public async Task TestFilterAndFilterODataUsedTogether()
        {
            string graphQLQueryName = "books";
            string gqlQuery = @"{
                books(_filter: {id: {gte: 2}}, _filterOData: ""id lt 4"")
                {
                    items {
                        id
                    }
                }
            }";

            string dbQuery = MakeQueryOn(
                "books",
                new List<string> { "id" },
                "id >= 2 AND id < 4");

            string actual = await GetGraphQLResultAsync(gqlQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Test filtering null integer fields
        /// </summary>
        [TestMethod]
        public async Task TestGetNullIntFields()
        {
            string graphQLQueryName = "magazines";
            string gqlQuery = @"{
                magazines(_filter: {issue_number: {isNull: true}}) {
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
                "issue_number IS NULL");

            string actual = await GetGraphQLResultAsync(gqlQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Test filtering non null integer fields
        /// </summary>
        [TestMethod]
        public async Task TestGetNonNullIntFields()
        {
            string graphQLQueryName = "magazines";
            string gqlQuery = @"{
                magazines(_filter: {issue_number: {isNull: false}}) {
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
                "issue_number IS NOT NULL");

            string actual = await GetGraphQLResultAsync(gqlQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Test filtering null string fields
        /// </summary>
        [TestMethod]
        public async Task TestGetNullStringFields()
        {
            string graphQLQueryName = "website_users";
            string gqlQuery = @"{
                website_users(_filter: {username: {isNull: true}}) {
                    items {
                        id
                        username
                    }
                }
            }";

            string dbQuery = MakeQueryOn(
                "website_users",
                new List<string> { "id", "username" },
                "username IS NULL");

            string actual = await GetGraphQLResultAsync(gqlQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Test filtering not null string fields
        /// </summary>
        [TestMethod]
        public async Task TestGetNonNullStringFields()
        {
            string graphQLQueryName = "website_users";
            string gqlQuery = @"{
                website_users(_filter: {username: {isNull: false}}) {
                    items {
                        id
                        username
                    }
                }
            }";

            string dbQuery = MakeQueryOn(
                "website_users",
                new List<string> { "id", "username" },
                "username IS NOT NULL");

            string actual = await GetGraphQLResultAsync(gqlQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Passes null to nullable fields and makes sure they are ignored
        /// </summary>
        public async Task TestExplicitNullFieldsAreIgnored()
        {
            string graphQLQueryName = "getBooks";
            string gqlQuery = @"{
                getBooks(_filter: {
                                    id: {gte: 2 lte: null}
                                    title: null
                                    or: null
                                  })
                {
                    id
                    title
                }
            }";

            string dbQuery = MakeQueryOn(
                "books",
                new List<string> { "id", "title" },
                @"id >= 2");

            string actual = await GetGraphQLResultAsync(gqlQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Passes null to nullable fields and makes sure they are ignored
        /// </summary>
        public async Task TestInputObjectWithOnlyNullFieldsEvaluatesToFalse()
        {
            string graphQLQueryName = "getBooks";
            string gqlQuery = @"{
                getBooks(_filter: {id: {lte: null}})
                {
                    id
                }
            }";

            string dbQuery = MakeQueryOn(
                "books",
                new List<string> { "id" },
                @"1 != 1");

            string actual = await GetGraphQLResultAsync(gqlQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        #endregion

        /// <remarks>
        /// This function does not escape special characters from column names so those might lead to errors
        /// </remarks>
        protected abstract string MakeQueryOn(
            string table,
            List<string> queriedColumns,
            string predicate,
            List<string> pkColumns = null);

        protected override async Task<string> GetGraphQLResultAsync(string graphQLQuery, string graphQLQueryName, GraphQLController graphQLController, Dictionary<string, object> variables = null, bool failOnErrors = true)
        {
            string dataResult = await base.GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, graphQLController, variables, failOnErrors);

            return JsonDocument.Parse(dataResult).RootElement.GetProperty("items").ToString();
        }
    }
}
