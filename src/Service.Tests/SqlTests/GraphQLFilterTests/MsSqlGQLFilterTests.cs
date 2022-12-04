using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Queries;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLFilterTests
{

    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class MsSqlGQLFilterTests : GraphQLFilterTestBase
    {
        protected static string DEFAULT_SCHEMA = "dbo";

        /// <summary>
        /// Set the database engine for the tests
        /// </summary>
        [ClassInitialize]
        public static async Task Setup(TestContext context)
        {
            DatabaseEngine = TestCategory.MSSQL;
            await InitializeTestFixture(context);
        }

        /// <summary>
        /// Test Nested Filter for Many-One relationship.
        /// </summary>
        [TestMethod]
        public async Task TestNestedFilterManyOne()
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

            string defaultSchema = GetDefaultSchema();
            if (!string.IsNullOrEmpty(defaultSchema))
            {
                defaultSchema += ".";
            }

            string existsPredicate = $@"
                EXISTS( SELECT 1
                        FROM {defaultSchema}[series] AS [table1]
                        WHERE [table1].[name] = 'Foundation'
                        AND [table0].[series_id] = [table1].[id] )";
            string dbQuery = MakeQueryOn(
                table: "comics",
                queriedColumns: new List<string> { "id", "title" },
                existsPredicate,
                GetDefaultSchema());

            JsonElement actual = await ExecuteGraphQLRequestAsync(
                gqlQuery,
                graphQLQueryName,
                isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Test Nested Filter for One-Many relationship
        /// </summary>
        [TestMethod]
        public async Task TestNestedFilterOneMany()
        {
            string graphQLQueryName = "series";
            // Gets the series that have comics with categoryName containing Fairy
            string gqlQuery = @"{
                series (" + QueryBuilder.FILTER_FIELD_NAME +
                    @": { comics: { categoryName: { contains: ""Fairy"" }}} )
                    {
                      items {
                        id
                        name
                      }
                    }
                }";

            string defaultSchema = GetDefaultSchema();
            if (!string.IsNullOrEmpty(defaultSchema))
            {
                defaultSchema += ".";
            }

            string existsPredicate = $@"
                EXISTS( SELECT 1
                        FROM {defaultSchema}[comics] AS [table1]
                        WHERE [table1].[title] = 'Cinderella'
                        AND [table1].[series_id] = [table0].[id] )";
            string dbQuery = MakeQueryOn(
                table: "series",
                queriedColumns: new List<string> { "id", "name" },
                existsPredicate,
                GetDefaultSchema());

            JsonElement actual = await ExecuteGraphQLRequestAsync(
                gqlQuery,
                graphQLQueryName,
                isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Test Nested Filter for Many-Many relationship
        /// </summary>
        [TestMethod]
        public async Task TestNestedFilterManyMany()
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

            string defaultSchema = GetDefaultSchema();
            if (!string.IsNullOrEmpty(defaultSchema))
            {
                defaultSchema += ".";
            }

            string existsPredicate = $@"
                EXISTS( SELECT 1
                        FROM {defaultSchema}[authors] AS [table1]
                        INNER JOIN [dbo].[book_author_link] AS [table3]
                        ON [table3].[book_id] = [table0].[id]
                        WHERE [table1].[name] = 'Aaron'
                        AND [table3].[author_id] = [table1].[id])";
            string dbQuery = MakeQueryOn(
                table: "books",
                queriedColumns: new List<string> { "title" },
                existsPredicate,
                GetDefaultSchema());

            JsonElement actual = await ExecuteGraphQLRequestAsync(
                gqlQuery,
                graphQLQueryName,
                isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Test a field of the nested filter is null.
        /// </summary>
        [TestMethod]
        public async Task TestNestedFilterFieldIsNull()
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

            string defaultSchema = GetDefaultSchema();
            if (!string.IsNullOrEmpty(defaultSchema))
            {
                defaultSchema += ".";
            }

            string existsPredicate = $@"
                EXISTS( SELECT 1
                        FROM {defaultSchema}[stocks_price] AS [table1]
                        WHERE [table1].[price] IS NULL
                        AND [table1].[categoryid] = [table0].[categoryid]
                        AND [table1].[pieceid] = [table0].[pieceid])";
            string dbQuery = MakeQueryOn(
                table: "stocks",
                queriedColumns: new List<string> { "categoryName" },
                existsPredicate,
                GetDefaultSchema(),
                pkColumns: new List<string> { "categoryId", "pieceId" });

            JsonElement actual = await ExecuteGraphQLRequestAsync(
                gqlQuery,
                graphQLQueryName,
                isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Tests nested filter having another nested filter.
        /// </summary>
        [TestMethod]
        public async Task TestNestedFilterWithinNestedFilter()
        {
            string graphQLQueryName = "books";

            // Gets all the books written by Aaron
            // only if the title of one of his books contains 'Awesome'.
            string gqlQuery = @"{
                books (" + QueryBuilder.FILTER_FIELD_NAME +
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

            string defaultSchema = GetDefaultSchema();
            if (!string.IsNullOrEmpty(defaultSchema))
            {
                defaultSchema += ".";
            }

            string existsPredicate = $@"
                EXISTS (SELECT 1 FROM {defaultSchema}[authors] AS [table1]
                        INNER JOIN {defaultSchema}[book_author_link] AS [table6]
                        ON [table6].[book_id] = [table0].[id]
                        WHERE (EXISTS (SELECT 1 FROM {defaultSchema}[books] AS [table2]
                                       INNER JOIN {defaultSchema}[book_author_link] AS [table4]
                                       ON [table4].[author_id] = [table1].[id]
                                       WHERE [table2].[title] LIKE 'Awesome'
                                       AND [table4].[book_id] = [table2].[id])
                                       AND [table1].[name] = 'Aaron') AND [table6].[author_id] = [table1].[id])";
            string dbQuery = MakeQueryOn(
                table: "books",
                queriedColumns: new List<string> { "title" },
                existsPredicate,
                GetDefaultSchema());

            JsonElement actual = await ExecuteGraphQLRequestAsync(
                gqlQuery,
                graphQLQueryName,
                isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Tests nested filter and an AND clause.
        /// </summary>
        [TestMethod]
        public async Task TestNestedFilterWithAnd()
        {
            string graphQLQueryName = "books";

            // Gets all the books written by Aniruddh and the publisher is 'Small Town Publisher'.
            string gqlQuery = @"{
                books (" + QueryBuilder.FILTER_FIELD_NAME +
                    @": { authors:  {
                          name: { eq: ""Aniruddh""}
                          }
                      and: {
                       publishers: { name: { eq: ""Small Town Publisher"" } }
                       }
                    })
                    {
                      items {
                        title
                      }
                    }
                }";

            string defaultSchema = GetDefaultSchema();
            if (!string.IsNullOrEmpty(defaultSchema))
            {
                defaultSchema += ".";
            }

            string existsPredicate = $@"
                EXISTS (SELECT 1 FROM {defaultSchema}[authors] AS [table1]
                        INNER JOIN {defaultSchema}[book_author_link] AS [table3]
                        ON [table3].[book_id] = [table0].[id]
                        WHERE [table1].[name] = 'Aniruddh'
                        AND [table3].[author_id] = [table1].[id])
                        AND EXISTS (SELECT 1 FROM {defaultSchema}[publishers] AS [table4]
                                    WHERE [table4].[name] = 'Small Town Publisher'
                                    AND [table0].[publisher_id] = [table4].[id])";
            string dbQuery = MakeQueryOn(
                table: "books",
                queriedColumns: new List<string> { "title" },
                existsPredicate,
                GetDefaultSchema());

            JsonElement actual = await ExecuteGraphQLRequestAsync(
                gqlQuery,
                graphQLQueryName,
                isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Tests nested filter alongwith an OR clause.
        /// </summary>
        [TestMethod]
        public async Task TestNestedFilterWithOr()
        {
            string graphQLQueryName = "books";

            // Gets all the books written by Aniruddh OR if their publisher is 'TBD Publishing One'.
            string gqlQuery = @"{
                books (" + QueryBuilder.FILTER_FIELD_NAME +
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

            string defaultSchema = GetDefaultSchema();
            if (!string.IsNullOrEmpty(defaultSchema))
            {
                defaultSchema += ".";
            }

            string existsPredicate = $@"
                EXISTS( SELECT 1 FROM {defaultSchema}[publishers] AS [table1]
                    WHERE [table1].[name] = 'TBD Publishing One'
                    AND [table0].[publisher_id] = [table1].[id])
                OR EXISTS( SELECT 1 FROM {defaultSchema}[authors] AS [table3]
                           INNER JOIN {defaultSchema}[book_author_link] AS [table5]
                           ON [table5].[book_id] = [table0].[id]
                           WHERE [table3].[name] = 'Aniruddh'
                           AND [table5].[author_id] = [table3].[id])";
            string dbQuery = MakeQueryOn(
                table: "books",
                queriedColumns: new List<string> { "title" },
                existsPredicate,
                GetDefaultSchema());

            JsonElement actual = await ExecuteGraphQLRequestAsync(
                gqlQuery,
                graphQLQueryName,
                isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Gets the default schema for
        /// MsSql.
        /// </summary>
        /// <returns></returns>
        protected override string GetDefaultSchema()
        {
            return DEFAULT_SCHEMA;
        }

        protected override string MakeQueryOn(
            string table,
            List<string> queriedColumns,
            string predicate,
            string schema,
            List<string> pkColumns = null)
        {
            if (pkColumns == null)
            {
                pkColumns = new() { "id" };
            }

            string schemaAndTable = $"[{schema}].[{table}]";
            string orderBy = string.Join(", ", pkColumns.Select(c => $"[table0].[{c}]"));

            return @"
                SELECT TOP 100 " + string.Join(", ", queriedColumns) + @"
                FROM " + schemaAndTable + @" AS [table0]
                WHERE " + predicate + @"
                ORDER BY " + orderBy + @" asc
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES
            ";
        }
    }
}
