using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests
{

    [TestClass]
    public abstract class GraphQLFilterTestBase : SqlTestBase
    {

        #region Test Fixture Setup
        protected static GraphQLService _graphQLService;
        protected static GraphQLController _graphQLController;
        protected static readonly string _integrationTableName = "books";

        #endregion

        #region Tests

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

        #endregion

        /// <remarks>
        /// This function does not escape special characters from column names so those might lead to errors
        /// </remarks>
        protected abstract string MakeQueryOnBooks(List<string> queriedColumns, string predicate);
    }
}
