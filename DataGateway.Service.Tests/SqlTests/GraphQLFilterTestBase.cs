using System.Collections.Generic;
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
            string graphQLQueryName = "getBooks";
            string gqlQuery = @"{
                getBooks(_filter: {title: {eq: ""Awesome book""}})
                {
                    title
                }
            }";

            string dbQuery = MakeQueryOnBooks(
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
            string graphQLQueryName = "getBooks";
            string gqlQuery = @"{
                getBooks(_filter: {title: {neq: ""Awesome book""}})
                {
                    title
                }
            }";

            string dbQuery = MakeQueryOnBooks(
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
            string graphQLQueryName = "getBooks";
            string gqlQuery = @"{
                getBooks(_filter: {title: {startsWith: ""Awe""}})
                {
                    title
                }
            }";

            string dbQuery = MakeQueryOnBooks(
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
            string graphQLQueryName = "getBooks";
            string gqlQuery = @"{
                getBooks(_filter: {title: {endsWith: ""book""}})
                {
                    title
                }
            }";

            string dbQuery = MakeQueryOnBooks(
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
            string graphQLQueryName = "getBooks";
            string gqlQuery = @"{
                getBooks(_filter: {title: {contains: ""some""}})
                {
                    title
                }
            }";

            string dbQuery = MakeQueryOnBooks(
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
            string graphQLQueryName = "getBooks";
            string gqlQuery = @"{
                getBooks(_filter: {title: {notContains: ""book""}})
                {
                    title
                }
            }";

            string dbQuery = MakeQueryOnBooks(
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
            string graphQLQueryName = "getBooks";
            string gqlQuery = @"{
                getBooks(_filter: {title: {contains: ""%""}})
                {
                    title
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
            string graphQLQueryName = "getBooks";
            string gqlQuery = @"{
                getBooks(_filter: {id: {eq: 2}})
                {
                    id
                }
            }";

            string dbQuery = MakeQueryOnBooks(
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
            string graphQLQueryName = "getBooks";
            string gqlQuery = @"{
                getBooks(_filter: {id: {neq: 2}})
                {
                    id
                }
            }";

            string dbQuery = MakeQueryOnBooks(
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
            string graphQLQueryName = "getBooks";
            string gqlQuery = @"{
                getBooks(_filter: {id: {gt: 2 lt: 4}})
                {
                    id
                }
            }";

            string dbQuery = MakeQueryOnBooks(
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
            string graphQLQueryName = "getBooks";
            string gqlQuery = @"{
                getBooks(_filter: {id: {gte: 2 lte: 4}})
                {
                    id
                }
            }";

            string dbQuery = MakeQueryOnBooks(
                new List<string> { "id" },
                @"(id >= 2 AND id <= 4)");

            string actual = await GetGraphQLResultAsync(gqlQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Tests that when and appears in front of or, the final predicate
        /// maintains this order
        /// </summary>
        [TestMethod]
        public async Task TestAndOrFilters()
        {
            string graphQLQueryName = "getBooks";
            string gqlQuery = @"{
                getBooks(_filter: {
                                    id: {gt: 2}
                                    and: [
                                        {id: {lt: 4}}
                                    ]
                                    or: [
                                        {id: {eq: 4}}
                                    ]
                                })
                {
                    id
                }
            }";

            string dbQuery = MakeQueryOnBooks(
                new List<string> { "id" },
                @"(id > 2 AND id < 4 OR id = 4)");

            string actual = await GetGraphQLResultAsync(gqlQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Tests that when or appears in front of and, the final predicate
        /// maintains this order
        /// </summary>
        [TestMethod]
        public async Task TestOrAndFilters()
        {
            string graphQLQueryName = "getBooks";
            string gqlQuery = @"{
                getBooks(_filter: {
                                    id: {gt: 2}
                                    or: [
                                        {id: {eq: 4}}
                                    ]
                                    and: [
                                        {id: {lt: 4}}
                                    ]
                                })
                {
                    id
                }
            }";

            string dbQuery = MakeQueryOnBooks(
                new List<string> { "id" },
                @"(id > 2 OR id = 4 AND id < 4)");

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
            string graphQLQueryName = "getBooks";
            string gqlQuery = @"{
                getBooks(_filter: {
                                    id:{gt: 2 lt: 4}
                                    or: [
                                        {id: {gte: 4}},
                                        {title: {contains: ""book""}}
                                    ]
                                })
                {
                    id
                    title
                }
            }";

            string dbQuery = MakeQueryOnBooks(
                new List<string> { "id", "title" },
                @"((id > 2 AND id < 4) OR (id >= 4 OR title LIKE '%book%'))");

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
            string graphQLQueryName = "getBooks";
            string gqlQuery = @"{
                getBooks(_filter: {
                                    or: [
                                        {id: {gt: 2} and: [{id: {lt: 4}}]},
                                        {id: {gte: 4} title: {contains: ""book""}}
                                    ]
                                })
                {
                    id
                    title
                }
            }";

            string dbQuery = MakeQueryOnBooks(
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
            string graphQLQueryName = "getBooks";
            string gqlQuery = @"{
                getBooks(_filter: {
                                    id: {gte: 2}
                                    publisher_id: {gt: 2000}
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
                                        {publisher_id: {lt: 1500}}
                                    ]
                                })
                {
                    id
                    title
                    publisher_id
                }
            }";

            string dbQuery = MakeQueryOnBooks(
                new List<string> { "id", "title", "publisher_id" },
                @"((id >= 2 AND publisher_id > 2000) AND (id < 1000 AND title LIKE 'US%') OR publisher_id < 1500)");

            string actual = await GetGraphQLResultAsync(gqlQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Test that an empty and evaluates to ... and False
        /// </summary>
        [TestMethod]
        public async Task TestEmptyAnd()
        {
            string graphQLQueryName = "getBooks";
            string gqlQuery = @"{
                getBooks(_filter: {id: {gt: -1} and: []})
                {
                    id
                }
            }";

            string actual = await GetGraphQLResultAsync(gqlQuery, graphQLQueryName, _graphQLController);
            SqlTestHelper.PerformTestEqualJsonStrings("[]", actual);
        }

        /// <summary>
        /// Test that an empty or evaluates to ... or False
        /// </summary>
        [TestMethod]
        public async Task TestEmptyOr()
        {
            string graphQLQueryName = "getBooks";
            string gqlQuery = @"{
                getBooks(_filter: {id: {gt: -1} or: []})
                {
                    id
                }
            }";

            string dbQuery = MakeQueryOnBooks(
                new List<string> { "id" },
                "id > -1");

            string actual = await GetGraphQLResultAsync(gqlQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Test that only an empty and returns nothing
        /// </summary>
        [TestMethod]
        public async Task TestOnlyEmptyAnd()
        {
            string graphQLQueryName = "getBooks";
            string gqlQuery = @"{
                getBooks(_filter: {and: []})
                {
                    id
                }
            }";

            string actual = await GetGraphQLResultAsync(gqlQuery, graphQLQueryName, _graphQLController);
            SqlTestHelper.PerformTestEqualJsonStrings("[]", actual);
        }

        /// <summary>
        /// Test that only an empty or returns nothing
        /// </summary>
        [TestMethod]
        public async Task TestOnlyEmptyOr()
        {
            string graphQLQueryName = "getBooks";
            string gqlQuery = @"{
                getBooks(_filter: {or: []})
                {
                    id
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
            string graphQLQueryName = "getBooks";
            string gqlQuery = @"{
                getBooks(_filter: {id: {gte: 2}}, _filterOData: ""id lt 4"")
                {
                    id
                }
            }";

            string dbQuery = MakeQueryOnBooks(
                new List<string> { "id" },
                "id >= 2 AND id < 4");

            string actual = await GetGraphQLResultAsync(gqlQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        #endregion

        /// <remarks>
        /// This function does not escape special characters from column names so those might lead to errors
        /// </remarks>
        protected abstract string MakeQueryOnBooks(List<string> queriedColumns, string predicate);
    }
}
