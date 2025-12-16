// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLQueryTests
{
    /// <summary>
    /// Test GraphQL Queries validating proper resolver/engine operation.
    /// </summary>
    [TestClass, TestCategory(TestCategory.DWSQL)]
    public class DwSqlGraphQLQueryTests : GraphQLQueryTestBase
    {
        /// <summary>
        /// Set the database engine for the tests
        /// </summary>
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.DWSQL;
            await InitializeTestFixture();
        }

        #region Tests
        /// <summary>
        /// Gets array of results for querying more than one item.
        /// </summary>
        [TestMethod]
        public async Task MultipleResultQuery()
        {
            string msSqlQueryToValidateDWResultAgainst = $"SELECT id, title FROM books ORDER BY id asc FOR JSON PATH, INCLUDE_NULL_VALUES";
            await MultipleResultQuery(msSqlQueryToValidateDWResultAgainst);
        }

        /// <summary>
        /// Gets array of results for querying more than one item using query variables
        /// <checks>Runs an mssql query and then validates that the result from the dwsql query graphql call matches the mssql query result.</checks>
        /// </summary>
        [TestMethod]
        public async Task MultipleResultQueryWithVariables()
        {
            string msSqlQuery = $"SELECT id, title FROM books ORDER BY id asc FOR JSON PATH, INCLUDE_NULL_VALUES";
            await MultipleResultQueryWithVariables(msSqlQuery);
        }

        /// <summary>
        /// Tests In operator using query variables
        /// <checks>Runs an mssql query and then validates that the result from the dwsql query graphql call matches the mssql query result.</checks>
        /// </summary>
        [TestMethod]
        public async Task InQueryWithVariables()
        {
            string msSqlQuery = $"SELECT id, title FROM books  where id IN (1, 2) ORDER BY id asc FOR JSON PATH, INCLUDE_NULL_VALUES";
            await InQueryWithVariables(msSqlQuery);
        }

        /// <summary>
        /// Tests In operator with null's and empty values
        /// <checks>Runs an mssql query and then validates that the result from the dwsql query graphql call matches the mssql query result.</checks>
        /// </summary>
        [TestMethod]
        public async Task InQueryWithNullAndEmptyvalues()
        {
            string msSqlQuery = $"SELECT string_types FROM type_table where string_types IN ('lksa;jdflasdf;alsdflksdfkldj', ' ', NULL) FOR JSON PATH, INCLUDE_NULL_VALUES";
            await InQueryWithNullAndEmptyvalues(msSqlQuery);
        }

        /// <summary>
        /// Gets array of results for querying more than one item using query mappings.
        /// </summary>
        [TestMethod]
        public async Task MultipleResultQueryWithMappings()
        {
            string msSqlQuery = @"
                SELECT [__column1] AS [column1], [__column2] AS [column2]
                FROM GQLmappings
                ORDER BY [__column1] asc
                FOR JSON PATH, INCLUDE_NULL_VALUES";

            await MultipleResultQueryWithMappings(msSqlQuery);
        }

        /// <summary>
        /// Tests IN operator with aggregations
        /// </summary>
        [TestMethod]
        public async Task INOperatorWithAggregations()
        {
            string dbQuery = @"
                SELECT COALESCE(
                    '[' + STRING_AGG(
                        '{' +
                        N'""publisher_id"":' + ISNULL(STRING_ESCAPE(CONVERT(NVARCHAR(MAX), [publisher_id]), 'json'), 'null') + ', ' +
                        N'""publisherCount"":' + ISNULL(STRING_ESCAPE(CONVERT(NVARCHAR(MAX), [publisherCount]), 'json'), 'null') +
                        '}', ', '
                    ) + ']', '[]'
                )
                FROM (
                    SELECT TOP 100
                        [table0].[publisher_id] AS [publisher_id],
                        COUNT([table0].[id]) AS [publisherCount]
                    FROM [dbo].[books] AS [table0]
                    WHERE 1 = 1
                    GROUP BY [table0].[publisher_id]
                    HAVING COUNT([table0].[id]) IN (1, 2)
                ) AS [table0];";

            string graphQLQueryName = "books";
            string graphQLQuery = @"query {
                      books {
                        groupBy(fields: [publisher_id]) {
                          fields{
                            publisher_id
                          }
                          aggregations{
                            publisherCount: count(field: id, having:  {
                               in: [1, 2]
                            })
                          }
                        }
                      }
                    }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStringsForAggreagtionQueries(expected, actual.ToString());
        }

        /// <summary>
        /// Test IN Operator in a relationship, for example, in a One -> One relationship
        /// (book -> website placement, website placememnt -> book)
        /// <summary>
        [TestMethod]
        public async Task InFilterOneToOneJoinQuery()
        {
            string dwSqlQuery = @"
                SELECT COALESCE(
                    '[' + STRING_AGG(
                        '{' +
                            N'""id"":' + ISNULL(STRING_ESCAPE(CONVERT(NVARCHAR(MAX), [id]), 'json'), 'null') + ',' +
                            N'""title"":' + ISNULL('""' + STRING_ESCAPE([title], 'json') + '""', 'null') + ',' +
                            N'""websiteplacement"":' + ISNULL([websiteplacement], 'null') +
                        '}',
                        ', '
                    ) + ']',
                    '[]'
                )
                FROM (
                    SELECT TOP 100
                        [table0].[id] AS [id],
                        [table0].[title] AS [title],
                        ([table1_subq].[data]) AS [websiteplacement]
                    FROM [dbo].[books] AS [table0]
    
                    OUTER APPLY (
                        SELECT STRING_AGG(
                            '{' +
                                N'""price"":' + ISNULL(STRING_ESCAPE(CONVERT(NVARCHAR(MAX), [price]), 'json'), 'null') + ',' +
                                N'""book_id"":' + ISNULL(STRING_ESCAPE(CONVERT(NVARCHAR(MAX), [book_id]), 'json'), 'null') +
                            '}',
                            ', '
                        )
                        FROM (
                            SELECT TOP 1
                                [table1].[price] AS [price],
                                [table1].[book_id] AS [book_id]
                            FROM [dbo].[book_website_placements] AS [table1]
                            WHERE [table0].[id] = [table1].[book_id]
                              AND [table1].[book_id] = [table0].[id]
                            ORDER BY [table1].[id] ASC
                        ) AS [table1]
                    ) AS [table1_subq]([data])
    
                    WHERE (
                        [table0].[title] IN ('Awesome book', 'Also Awesome book')
                        AND EXISTS (
                            SELECT 1
                            FROM [dbo].[book_website_placements] AS [table6]
                            WHERE [table6].[book_id] IN (1, 2)
                              AND [table6].[book_id] = [table0].[id]
                              AND [table0].[id] = [table6].[book_id]
                        )
                    )
                    ORDER BY [table0].[id] DESC
                ) AS [table0];";

            await InFilterOneToOneJoinQuery(dwSqlQuery);
        }

        /// <summary>
        /// Test One-To-One relationship both directions
        /// (book -> website placement, website placememnt -> book)
        /// <summary>
        [TestMethod]
        public async Task OneToOneJoinQuery()
        {
            string dwSqlQuery = @"
                SELECT COALESCE('[' + STRING_AGG('{' + N'""id"":' + ISNULL(STRING_ESCAPE(CAST([id] AS NVARCHAR(MAX)), 'json'), 
                                'null') + ',' + N'""title"":' + ISNULL('""' + STRING_ESCAPE([title], 'json') + '""', 'null') + ',' + 
                            N'""websiteplacement"":' + ISNULL([websiteplacement], 'null') + 
                            '}', ', ') + ']', '[]')
                FROM (
                    SELECT TOP 100 [table0].[id] AS [id],
                        [table0].[title] AS [title],
                        ([table1_subq].[data]) AS [websiteplacement]
                    FROM [dbo].[books] AS [table0]
                    OUTER APPLY (
                        SELECT STRING_AGG('{' + N'""price"":' + ISNULL(STRING_ESCAPE(CAST([price] AS NVARCHAR(MAX)), 'json'), 
                                    'null') + '}', ', ')
                        FROM (
                            SELECT TOP 1 [table1].[price] AS [price]
                            FROM [dbo].[book_website_placements] AS [table1]
                            WHERE [table0].[id] = [table1].[book_id]
                                AND [table1].[book_id] = [table0].[id]
                            ORDER BY [table1].[id] ASC
                            ) AS [table1]
                        ) AS [table1_subq]([data])
                    WHERE 1 = 1
                    ORDER BY [table0].[id] ASC
                    ) AS [table0]";

            await OneToOneJoinQuery(dwSqlQuery);
        }

        /// <summary>
        /// DwNTo1JoinOpt is enabled by default for testing
        /// Below test case will ensure the results are identical with using STRING_AGG
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async override Task DeeplyNestedManyToOneJoinQuery()
        {
            string dwSqlQuery = @"
            SELECT COALESCE(
                '[' + STRING_AGG(
                    '{' + N'""title"":' + ISNULL('""' + STRING_ESCAPE([title], 'json') + '""', 'null') + ',' +
                    N'""publishers"":' + ISNULL('""' + STRING_ESCAPE([publishers], 'json') + '""', 'null') + '}', 
                    ', '
                ) + ']', 
                '[]'
            )
            FROM (
                SELECT TOP 5 
                    [table0].[title] AS [title], 
                    ([table1_subq].[data]) AS [publishers]
                FROM [dbo].[books] AS [table0]
                OUTER APPLY (
                    SELECT STRING_AGG(
                        '{' + N'""name"":' + ISNULL('""' + STRING_ESCAPE([name], 'json') + '""', 'null') + ',' +
                        N'""books"":' + ISNULL('""' + STRING_ESCAPE([books], 'json') + '""', 'null') + '}', 
                        ', '
                    )
                    FROM (
                        SELECT TOP 1 
                            [table1].[name] AS [name], 
                            (COALESCE([table2_subq].[data], '[]')) AS [books]
                        FROM [dbo].[publishers] AS [table1]
                        OUTER APPLY (
                            SELECT COALESCE(
                                '[' + STRING_AGG(
                                    '{' + N'""title"":' + ISNULL('""' + STRING_ESCAPE([title], 'json') + '""', 'null') + ',' +
                                    N'""publishers"":' + ISNULL('""' + STRING_ESCAPE([publishers], 'json') + '""', 'null') + '}', 
                                    ', '
                                ) + ']', 
                                '[]'
                            )
                            FROM (
                                SELECT TOP 4 
                                    [table2].[title] AS [title], 
                                    ([table3_subq].[data]) AS [publishers]
                                FROM [dbo].[books] AS [table2]
                                OUTER APPLY (
                                    SELECT STRING_AGG(
                                        '{' + N'""name"":' + ISNULL('""' + STRING_ESCAPE([name], 'json') + '""', 'null') + ',' +
                                        N'""books"":' + ISNULL('""' + STRING_ESCAPE([books], 'json') + '""', 'null') + '}', 
                                        ', '
                                    )
                                    FROM (
                                        SELECT TOP 1 
                                            [table3].[name] AS [name], 
                                            (COALESCE([table4_subq].[data], '[]')) AS [books]
                                        FROM [dbo].[publishers] AS [table3]
                                        OUTER APPLY (
                                            SELECT COALESCE(
                                                '[' + STRING_AGG(
                                                    '{' + N'""title"":' + ISNULL('""' + STRING_ESCAPE([title], 'json') + '""', 'null') + ',' +
                                                    N'""publishers"":' + ISNULL('""' + STRING_ESCAPE([publishers], 'json') + '""', 'null') + '}', 
                                                    ', '
                                                ) + ']', 
                                                '[]'
                                            )
                                            FROM (
                                                SELECT TOP 3 
                                                    [table4].[title] AS [title], 
                                                    ([table5_subq].[data]) AS [publishers]
                                                FROM [dbo].[books] AS [table4]
                                                OUTER APPLY (
                                                    SELECT STRING_AGG(
                                                        '{' + N'""name"":' + ISNULL('""' + STRING_ESCAPE([name], 'json') + '""', 'null') + '}', 
                                                        ', '
                                                    )
                                                    FROM (
                                                        SELECT TOP 1 
                                                            [table5].[name] AS [name]
                                                        FROM [dbo].[publishers] AS [table5]
                                                        WHERE [table4].[publisher_id] = [table5].[id] 
                                                          AND [table5].[id] = [table4].[publisher_id]
                                                        ORDER BY [table5].[id] ASC
                                                    ) AS [table5]
                                                ) AS [table5_subq]([data])
                                                WHERE [table4].[publisher_id] = [table3].[id]
                                                ORDER BY [table4].[id] ASC
                                            ) AS [table4]
                                        ) AS [table4_subq]([data])
                                        WHERE [table2].[publisher_id] = [table3].[id] 
                                          AND [table3].[id] = [table2].[publisher_id]
                                        ORDER BY [table3].[id] ASC
                                    ) AS [table3]
                                ) AS [table3_subq]([data])
                                WHERE [table2].[publisher_id] = [table1].[id]
                                ORDER BY [table2].[id] ASC
                            ) AS [table2]
                        ) AS [table2_subq]([data])
                        WHERE [table0].[publisher_id] = [table1].[id] 
                          AND [table1].[id] = [table0].[publisher_id]
                        ORDER BY [table1].[id] ASC
                    ) AS [table1]
                ) AS [table1_subq]([data])
                WHERE 1 = 1
                ORDER BY [table0].[id] ASC
            ) AS [table0]
            ";

            string graphQLQueryName = "books";
            string graphQLQuery = @"{
              books(first: 5) {
                items {
                  title
                  publishers {
                    name
                    books(first: 4) {
                      items {
                        title
                        publishers {
                          name
                          books(first: 3) {
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

            JsonElement actualGraphQLResults = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            string cleanedActualGraphQLResults = SqlTestHelper.RemoveItemsKeyFromJson(actualGraphQLResults.GetProperty("items")).ToString();

            string expected = await GetDatabaseResultAsync(dwSqlQuery);

            SqlTestHelper.PerformTestEqualJsonStringsForNestedQueries(expected, cleanedActualGraphQLResults);
        }

        /// <summary>
        /// DwNTo1JoinOpt is enabled by default for testing
        /// Below test case will ensure for To-N joins, the query builder will fallback to use STRING_AGG
        /// And the results are consistent
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task OneToManyJoinQuery()
        {
            string dwSqlQuery = @"
                SELECT 
                    COALESCE(
                        '[' + STRING_AGG(
                            '{' + 
                            N'""id"":' + ISNULL(STRING_ESCAPE(CONVERT(NVARCHAR(MAX), [id]), 'json'), 'null') + ',' +
                            N'""reviews"":' + ISNULL('""' + STRING_ESCAPE([reviews], 'json') + '""', 'null') + 
                            '}', 
                            ', '
                        ) + ']', 
                        '[]'
                    ) 
                FROM 
                    (
                        SELECT TOP 2 
                            [table0].[id] AS [id], 
                            COALESCE([table1_subq].[data], '[]') AS [reviews]
                        FROM 
                            [dbo].[books] AS [table0]
                        OUTER APPLY 
                            (
                                SELECT 
                                    COALESCE(
                                        '[' + STRING_AGG(
                                            '{' + 
                                            N'""content"":' + ISNULL('""' + STRING_ESCAPE([content], 'json') + '""', 'null') + 
                                            '}', 
                                            ', '
                                        ) + ']', 
                                        '[]'
                                    ) 
                                FROM 
                                    (
                                        SELECT TOP 100 
                                            [table1].[content] AS [content]
                                        FROM 
                                            [dbo].[reviews] AS [table1]
                                        WHERE 
                                            [table1].[book_id] = [table0].[id]
                                        ORDER BY 
                                            [table1].[book_id] ASC, 
                                            [table1].[id] ASC
                                    ) AS [table1]
                            ) AS [table1_subq]([data])
                        WHERE 
                            1 = 1
                        ORDER BY 
                            [table0].[id] ASC
                    ) AS [table0]";

            string graphQLQueryName = "books";
            string graphQLQuery = @"
               query {
                  books (first: 2) {
                    items {
                      id,
                      reviews {
                        items {
                          content
                        }
                      }
                    }
                  }
                }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            JsonNode cleanedActual = SqlTestHelper.RemoveItemsKeyFromJson(actual.GetProperty("items"));

            string expected = await GetDatabaseResultAsync(dwSqlQuery);

            SqlTestHelper.PerformTestEqualJsonStringsForNestedQueries(expected, cleanedActual.ToString());
        }

        /// <summary>
        /// Added more complicated cases when queries are deeply nested and compare the results. 
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async override Task DeeplyNestedManyToManyJoinQuery()
        {
            string dwSqlQuery = @"
        SELECT COALESCE(
            '[' + STRING_AGG(
                '{' + N'""title"":' + ISNULL('""' + STRING_ESCAPE([title], 'json') + '""', 'null') + ',' +
                N'""authors"":' + ISNULL('""' + STRING_ESCAPE([authors], 'json') + '""', 'null') + '}', ', '
            ) + ']', '[]'
        )
        FROM (
            SELECT TOP 5 
                [table0].[title] AS [title], 
                COALESCE([table1_subq].[data], '[]') AS [authors]
            FROM [dbo].[books] AS [table0]
            OUTER APPLY (
                SELECT COALESCE(
                    '[' + STRING_AGG(
                        '{' + N'""name"":' + ISNULL('""' + STRING_ESCAPE([name], 'json') + '""', 'null') + ',' +
                        N'""books"":' + ISNULL('""' + STRING_ESCAPE([books], 'json') + '""', 'null') + '}', ', '
                    ) + ']', '[]'
                )
                FROM (
                    SELECT TOP 4 
                        [table1].[name] AS [name], 
                        COALESCE([table2_subq].[data], '[]') AS [books]
                    FROM [dbo].[authors] AS [table1]
                    INNER JOIN [dbo].[book_author_link] AS [table11] 
                        ON [table11].[author_id] = [table1].[id]
                    OUTER APPLY (
                        SELECT COALESCE(
                            '[' + STRING_AGG(
                                '{' + N'""title"":' + ISNULL('""' + STRING_ESCAPE([title], 'json') + '""', 'null') + ',' +
                                N'""authors"":' + ISNULL('""' + STRING_ESCAPE([authors], 'json') + '""', 'null') + '}', ', '
                            ) + ']', '[]'
                        )
                        FROM (
                            SELECT TOP 3 
                                [table2].[title] AS [title], 
                                COALESCE([table3_subq].[data], '[]') AS [authors]
                            FROM [dbo].[books] AS [table2]
                            INNER JOIN [dbo].[book_author_link] AS [table8] 
                                ON [table8].[book_id] = [table2].[id]
                            OUTER APPLY (
                                SELECT COALESCE(
                                    '[' + STRING_AGG(
                                        '{' + N'""name"":' + ISNULL('""' + STRING_ESCAPE([name], 'json') + '""', 'null') + '}', ', '
                                    ) + ']', '[]'
                                )
                                FROM (
                                    SELECT TOP 2 
                                        [table3].[name] AS [name]
                                    FROM [dbo].[authors] AS [table3]
                                    INNER JOIN [dbo].[book_author_link] AS [table5] 
                                        ON [table5].[author_id] = [table3].[id]
                                    WHERE [table5].[book_id] = [table2].[id]
                                    ORDER BY [table3].[id] ASC
                                ) AS [table3]
                            ) AS [table3_subq]([data])
                            WHERE [table8].[author_id] = [table1].[id]
                            ORDER BY [table2].[id] ASC
                        ) AS [table2]
                    ) AS [table2_subq]([data])
                    WHERE [table11].[book_id] = [table0].[id]
                    ORDER BY [table1].[id] ASC
                ) AS [table1]
            ) AS [table1_subq]([data])
            WHERE 1 = 1
            ORDER BY [table0].[id] ASC
        ) AS [table0]";

            string graphQLQueryName = "books";
            string graphQLQuery = @"
            {
                books(first: 5) {
                    items {
                        title
                        authors(first: 4) {
                            items {
                                name
                                books(first: 3) {
                                    items {
                                        title
                                        authors(first: 2) {
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

            JsonElement actualGraphQLResults = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            string cleanedActualGraphQLResults = SqlTestHelper.RemoveItemsKeyFromJson(actualGraphQLResults.GetProperty("items")).ToString();

            string expected = await GetDatabaseResultAsync(dwSqlQuery);

            SqlTestHelper.PerformTestEqualJsonStringsForNestedQueries(expected, cleanedActualGraphQLResults);
        }

        /// <summary>
        /// Test query on One-To-One relationship when the fields defining
        /// the relationship in the entity include fields that are mapped in
        /// that same entity.
        /// </summary>
        /// <exception cref="NotImplementedException"></exception>
        [TestMethod]
        public async Task OneToOneJoinQueryWithMappedFieldNamesInRelationship()
        {
            string dwSqlQuery = @"
                SELECT COALESCE('['+STRING_AGG('{'+N'""fancyName"":' + ISNULL('""' + STRING_ESCAPE([fancyName],'json') + '""','null')+','+N'""fungus"":' + ISNULL([fungus],'null')+'}',', ')+']','[]')
                FROM (
                    SELECT TOP 100 [table0].[species] AS [fancyName], 
                        (SELECT TOP 1 '{""habitat"":""' + STRING_ESCAPE([table1].[habitat], 'json') + '""}'
                         FROM [dbo].[fungi] AS [table1]
                         WHERE [table0].[species] = [table1].[habitat] AND [table1].[habitat] = [table0].[species]
                         ORDER BY [table1].[speciesid] ASC) AS [fungus]
                    FROM [dbo].[trees] AS [table0]
                    WHERE 1 = 1
                    ORDER BY [table0].[treeId] ASC
                ) AS [table0]";

            await OneToOneJoinQueryWithMappedFieldNamesInRelationship(dwSqlQuery);
        }

        /// <summary>
        /// Test getting a single item by use of primary key
        /// <summary>
        [TestMethod]
        public async Task QueryWithSingleColumnPrimaryKey()
        {
            string msSqlQuery = @"
                SELECT title FROM books
                WHERE id = 2 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER
            ";

            await QueryWithSingleColumnPrimaryKey(msSqlQuery);
        }

        /// <summary>
        /// Test getting a single item by use of primary key and mappings.
        /// <summary>
        [TestMethod]
        public async Task QueryWithSingleColumnPrimaryKeyAndMappings()
        {
            string msSqlQuery = @"
                SELECT [__column1] AS [column1] FROM GQLMappings
                WHERE [__column1] = 1 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER
            ";

            await QueryWithSingleColumnPrimaryKeyAndMappings(msSqlQuery);
        }

        /// <summary>
        /// Test getting a single item by use of primary key and other columns.
        /// <summary>
        [TestMethod]
        public async Task QueryWithMultipleColumnPrimaryKey()
        {
            string msSqlQuery = @"
                SELECT TOP 1 content FROM reviews
                WHERE id = 568 AND book_id = 1 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER
            ";

            await QueryWithMultipleColumnPrimaryKey(msSqlQuery);
        }

        /// <summary>
        /// Test with a nullable foreign key
        /// <summary>
        [TestMethod]
        public async Task QueryWithNullableForeignKey()
        {
            string msSqlQuery = @"
                SELECT
                  TOP 1 [table0].[title] AS [title],
                  JSON_QUERY ([table1_subq].[data]) AS [myseries]
                FROM
                  [dbo].[comics] AS [table0] OUTER APPLY (
                    SELECT
                      TOP 1 [table1].[name] AS [name]
                    FROM
                      [dbo].[series] AS [table1]
                    WHERE
                      [table0].[series_id] = [table1].[id]
                    ORDER BY
                      [table1].[id] ASC FOR JSON PATH,
                      INCLUDE_NULL_VALUES,
                      WITHOUT_ARRAY_WRAPPER
                  ) AS [table1_subq]([data])
                WHERE
                  [table0].[id] = 1
                ORDER BY
                  [table0].[id] ASC FOR JSON PATH,
                  INCLUDE_NULL_VALUES,
                  WITHOUT_ARRAY_WRAPPER";

            await QueryWithNullableForeignKey(msSqlQuery);
        }

        /// <summary>
        /// Get all instances of a type with nullable integer fields
        /// </summary>
        [TestMethod]
        public async Task TestQueryingTypeWithNullableIntFields()
        {
            string msSqlQuery = $"SELECT TOP 100 id, title, issue_number FROM [foo].[magazines] ORDER BY id asc FOR JSON PATH, INCLUDE_NULL_VALUES";
            await TestQueryingTypeWithNullableIntFields(msSqlQuery);
        }

        /// <summary>
        /// Test where data in the db has a nullable datetime field. The query should successfully return the date in the published_date field if present, else return null.
        /// </summary>
        [TestMethod]
        public async Task TestQueryingTypeWithNullableDateTimeFields()
        {
            string msSqlQuery = $"SELECT datetime_types FROM type_table ORDER BY id asc FOR JSON PATH, INCLUDE_NULL_VALUES";
            await TestQueryingTypeWithNullableDateTimeFields(msSqlQuery);
        }

        /// <summary>
        /// Get all instances of a type with nullable string fields
        /// </summary>
        [TestMethod]
        public async Task TestQueryingTypeWithNullableStringFields()
        {
            string msSqlQuery = $"SELECT TOP 100 id, username FROM website_users ORDER BY id asc FOR JSON PATH, INCLUDE_NULL_VALUES";
            await TestQueryingTypeWithNullableStringFields(msSqlQuery);
        }

        /// <summary>
        /// Test to check graphQL support for aliases(arbitrarily set by user while making request).
        /// book_id and book_title are aliases used for corresponding query fields.
        /// The response for the query will contain the alias instead of raw db column.
        /// </summary>
        [TestMethod]
        public async Task TestAliasSupportForGraphQLQueryFields()
        {
            string msSqlQuery = $"SELECT TOP 2 id AS book_id, title AS book_title FROM books ORDER by id asc FOR JSON PATH, INCLUDE_NULL_VALUES";
            await TestAliasSupportForGraphQLQueryFields(msSqlQuery);
        }

        /// <summary>
        /// Test to check graphQL support for aliases(arbitrarily set by user while making request).
        /// book_id is an alias, while title is the raw db field.
        /// The response for the query will use the alias where it is provided in the query.
        /// </summary>
        [TestMethod]
        public async Task TestSupportForMixOfRawDbFieldFieldAndAlias()
        {
            string msSqlQuery = $"SELECT TOP 2 id AS book_id, title AS title FROM books ORDER by id asc FOR JSON PATH, INCLUDE_NULL_VALUES";
            await TestSupportForMixOfRawDbFieldFieldAndAlias(msSqlQuery);
        }

        /// <summary>
        /// Tests orderBy on a list query
        /// </summary>
        [TestMethod]
        public async Task TestOrderByInListQuery()
        {
            string msSqlQuery = $"SELECT TOP 100 id, title FROM books ORDER BY title DESC, id ASC FOR JSON PATH, INCLUDE_NULL_VALUES";
            await TestOrderByInListQuery(msSqlQuery);
        }

        /// <summary>
        /// Use multiple order options and order an entity with a composite pk
        /// </summary>
        [TestMethod]
        public async Task TestOrderByInListQueryOnCompPkType()
        {
            string msSqlQuery = $"SELECT TOP 100 id, content FROM reviews ORDER BY content ASC, id DESC, book_id ASC FOR JSON PATH, INCLUDE_NULL_VALUES";
            await TestOrderByInListQueryOnCompPkType(msSqlQuery);
        }

        /// <summary>
        /// Tests null fields in orderBy are ignored
        /// meaning that null pk columns are included in the ORDER BY clause
        /// as ASC by default while null non-pk columns are completely ignored
        /// </summary>
        [TestMethod]
        public async Task TestNullFieldsInOrderByAreIgnored()
        {
            string msSqlQuery = $"SELECT TOP 100 id, title FROM books ORDER BY title DESC, id ASC FOR JSON PATH, INCLUDE_NULL_VALUES";
            await TestNullFieldsInOrderByAreIgnored(msSqlQuery);
        }

        /// <summary>
        /// Tests that an orderBy with only null fields results in default pk sorting
        /// </summary>
        [TestMethod]
        public async Task TestOrderByWithOnlyNullFieldsDefaultsToPkSorting()
        {
            string msSqlQuery = $"SELECT TOP 100 id, title FROM books ORDER BY id ASC FOR JSON PATH, INCLUDE_NULL_VALUES";
            await TestOrderByWithOnlyNullFieldsDefaultsToPkSorting(msSqlQuery);
        }

        /// <summary>
        /// Tests that orderBy works using Variable.
        /// </summary>
        [TestMethod]
        public async Task TestSettingOrderByOrderUsingVariable()
        {
            string msSqlQuery = $"SELECT TOP 4 id, title FROM books ORDER BY id DESC FOR JSON PATH, INCLUDE_NULL_VALUES";
            await TestSettingOrderByOrderUsingVariable(msSqlQuery);
        }

        /// <summary>
        /// Tests complex arguments using variables
        /// </summary>
        [TestMethod]
        public async Task TestSettingComplexArgumentUsingVariables()
        {
            string msSqlQuery = $"SELECT TOP 100 id, title FROM books ORDER BY id ASC FOR JSON PATH, INCLUDE_NULL_VALUES";
            await base.TestSettingComplexArgumentUsingVariables(msSqlQuery);
        }

        /// <summary>
        /// Tests query with null arguments in gql call.
        /// </summary>
        [TestMethod]
        public async Task TestQueryWithExplicitlyNullArguments()
        {
            string msSqlQuery = $"SELECT id, title FROM books ORDER BY id asc FOR JSON PATH, INCLUDE_NULL_VALUES";
            await TestQueryWithExplicitlyNullArguments(msSqlQuery);
        }

        /// <summary>
        /// Tests query on view.
        /// </summary>
        [TestMethod]
        public async Task TestQueryOnBasicView()
        {
            string msSqlQuery = $"SELECT TOP 5 id, title FROM books_view_all ORDER BY id FOR JSON PATH, INCLUDE_NULL_VALUES";
            await base.TestQueryOnBasicView(msSqlQuery);
        }

        [TestMethod]
        public async Task TestQueryOnCompositeView()
        {
            string msSqlQuery = $"SELECT TOP 5 id, name FROM books_publishers_view_composite ORDER BY id FOR JSON PATH, INCLUDE_NULL_VALUES";
            await base.TestQueryOnCompositeView(msSqlQuery);
        }

        /// <summary>
        /// Datawarehouse does not support explicit foreign keys. ignoring this test.
        /// </summary>
        [TestMethod]
        [Ignore]
        public override Task TestConfigTakesPrecedenceForRelationshipFieldsOverDB(
            string[] sourceFields,
            string[] targetFields,
            int club_id,
            string club_name,
            DatabaseType dbType,
            string testEnvironment)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Test to execute stored-procedure in graphQL that returns a single row
        /// </summary>
        [TestMethod]
        public async Task TestStoredProcedureQueryForGettingSingleRow()
        {
            string dwSqlQuery = $"EXEC dbo.get_publisher_by_id @id=1234";
            await TestStoredProcedureQueryForGettingSingleRow(dwSqlQuery);
        }

        /// <summary>
        /// Test to execute stored-procedure in graphQL that returns a list(multiple rows)
        /// </summary>
        [TestMethod]
        public async Task TestStoredProcedureQueryForGettingMultipleRows()
        {
            string dwSqlQuery = $"EXEC dbo.get_books";
            await TestStoredProcedureQueryForGettingMultipleRows(dwSqlQuery);
        }

        /// <summary>
        /// Test to execute stored-procedure in graphQL that counts the total number of rows
        /// </summary>
        [TestMethod]
        public async Task TestStoredProcedureQueryForGettingTotalNumberOfRows()
        {
            string dwSqlQuery = $"EXEC dbo.count_books";
            await TestStoredProcedureQueryForGettingTotalNumberOfRows(dwSqlQuery);
        }

        /// <summary>
        /// Test to execute stored-procedure in graphQL that contains null in the result set.
        /// </summary>
        [TestMethod]
        public async Task TestStoredProcedureQueryWithResultsContainingNull()
        {
            string dwSqlQuery = $"EXEC dbo.get_authors_history_by_first_name @firstName='Aaron'";
            await TestStoredProcedureQueryWithResultsContainingNull(dwSqlQuery);
        }

        /// <summary>
        /// Checks failure on providing arguments with no default in runtimeconfig.
        /// In this test, there is no default value for the argument 'id' in runtimeconfig, nor is it specified in the query.
        /// Stored procedure expects id argument to be provided.
        /// This test validates the "Development Mode" error message which denotes the
        /// specific missing parameter and stored procedure name.
        /// </summary>
        [TestMethod]
        public async Task TestStoredProcedureQueryWithNoDefaultInConfig()
        {
            string graphQLQueryName = "executeGetPublisher";
            string graphQLQuery = @"{
                executeGetPublisher {
                    name
                }
            }";

            JsonElement result = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            SqlTestHelper.TestForErrorInGraphQLResponse(
                response: result.ToString(),
                message: "Procedure or function 'get_publisher_by_id' expects parameter '@id', which was not supplied.");
        }

        /// <summary>
        /// Test to check GraphQL support for aggregations with aliases.
        /// This test verifies that the SQL query results are correctly mapped to the expected GraphQL format.
        /// </summary>
        [TestMethod]
        public async Task TestSupportForAggregationsWithAliases()
        {
            string msSqlQuery = @"
SELECT COALESCE(
    '[' + STRING_AGG(
        '{' + N'""max"":' + ISNULL(STRING_ESCAPE(CONVERT(NVARCHAR(MAX), [max]), 'json'), 'null') + ',' +
        N'""max_price"":' + ISNULL(STRING_ESCAPE(CONVERT(NVARCHAR(MAX), [max_price]), 'json'), 'null') + ',' +
        N'""min_price"":' + ISNULL(STRING_ESCAPE(CONVERT(NVARCHAR(MAX), [min_price]), 'json'), 'null') + ',' +
        N'""avg_price"":' + ISNULL(STRING_ESCAPE(CONVERT(NVARCHAR(MAX), [avg_price]), 'json'), 'null') + ',' +
        N'""sum_price"":' + ISNULL(STRING_ESCAPE(CONVERT(NVARCHAR(MAX), [sum_price]), 'json'), 'null') + '}', ', '
    ) + ']', '[]'
) 
FROM (
    SELECT TOP 100  
        max([table0].[categoryid]) AS [max], 
        max([table0].[price]) AS [max_price], 
        min([table0].[price]) AS [min_price], 
        avg([table0].[price]) AS [avg_price], 
        sum([table0].[price]) AS [sum_price] 
    FROM [dbo].[stocks_price] AS [table0] 
    WHERE 1 = 1
) AS [table0];";

            // Execute the test for the SQL query
            await TestSupportForAggregationsWithAliases(msSqlQuery);
        }

        /// <summary>
        /// Test to check GraphQL support for aggregations with aliases and groupby.
        /// This test verifies that the SQL query results are correctly mapped to the expected GraphQL format.
        /// </summary>
        [TestMethod]
        public async Task TestSupportForGroupByAggregationsWithAliases()
        {
            string msSqlQuery = @"
SELECT COALESCE(
    '[' + STRING_AGG(
        '{' + N'""max"":' + ISNULL(STRING_ESCAPE(CONVERT(NVARCHAR(MAX), [max]), 'json'), 'null') + ',' +
        N'""max_price"":' + ISNULL(STRING_ESCAPE(CONVERT(NVARCHAR(MAX), [max_price]), 'json'), 'null') + ',' +
        N'""min_price"":' + ISNULL(STRING_ESCAPE(CONVERT(NVARCHAR(MAX), [min_price]), 'json'), 'null') + ',' +
        N'""avg_price"":' + ISNULL(STRING_ESCAPE(CONVERT(NVARCHAR(MAX), [avg_price]), 'json'), 'null') + ',' +
        N'""sum_price"":' + ISNULL(STRING_ESCAPE(CONVERT(NVARCHAR(MAX), [sum_price]), 'json'), 'null') + ',' +
        N'""count"":' + ISNULL(STRING_ESCAPE(CONVERT(NVARCHAR(MAX), [count]), 'json'), 'null') + '}', ', '
    ) + ']', '[]'
) 
FROM (
    SELECT TOP 100
        max([table0].[categoryid]) AS [max], 
        max([table0].[price]) AS [max_price], 
        min([table0].[price]) AS [min_price], 
        avg([table0].[price]) AS [avg_price], 
        sum([table0].[price]) AS [sum_price], 
        count([table0].[categoryid]) AS [count] 
    FROM [dbo].[stocks_price] AS [table0] 
    WHERE 1 = 1 
    GROUP BY [table0].[categoryid]
) AS [table0];";

            // Execute the test for the SQL query
            await TestSupportForGroupByAggregationsWithAliases(msSqlQuery);
        }

        /// <summary>
        /// Test to check GraphQL support for min aggregations.
        /// This test verifies that the SQL query results are correctly mapped to the expected GraphQL format.
        /// </summary>
        [TestMethod]
        public async Task TestSupportForMinAggregation()
        {
            string msSqlQuery = @"
SELECT COALESCE(
    '[' + STRING_AGG(
        '{' + N'""min_price"":' + ISNULL(STRING_ESCAPE(CONVERT(NVARCHAR(MAX), [min_price]), 'json'), 'null') + '}', ', '
    ) + ']', '[]'
) 
FROM (
    SELECT TOP 100 min([table0].[price]) AS [min_price] 
    FROM [dbo].[stocks_price] AS [table0] 
    WHERE 1 = 1
) AS [table0];";

            await TestSupportForMinAggregation(msSqlQuery);
        }

        /// <summary>
        /// Test to check GraphQL support for Max aggregations.
        /// This test verifies that the SQL query results are correctly mapped to the expected GraphQL format.
        /// </summary>
        [TestMethod]
        public async Task TestSupportForMaxAggregation()
        {
            string msSqlQuery = @"
SELECT COALESCE('['+STRING_AGG('{'+N'""max_price"":' + ISNULL(STRING_ESCAPE(CONVERT(NVARCHAR(MAX), [max_price]),'json'),'null')+'}',', ')+']','[]') 
FROM (
    SELECT TOP 100 max([table0].[price]) AS [max_price] 
    FROM [dbo].[stocks_price] AS [table0] 
    WHERE 1 = 1
) AS [table0];";

            // Execute the test for the SQL query
            await TestSupportForMaxAggregation(msSqlQuery);
        }

        /// <summary>
        /// Test to check GraphQL support for avg aggregations.
        /// This test verifies that the SQL query results are correctly mapped to the expected GraphQL format.
        /// </summary>
        [TestMethod]
        public async Task TestSupportForAvgAggregation()
        {
            string msSqlQuery = @"
SELECT COALESCE('['+STRING_AGG('{'+N'""avg_price"":' + ISNULL(STRING_ESCAPE(CONVERT(NVARCHAR(MAX), [avg_price]),'json'),'null')+'}',', ')+']','[]') 
FROM (
    SELECT TOP 100 avg([table0].[price]) AS [avg_price] 
    FROM [dbo].[stocks_price] AS [table0] 
    WHERE 1 = 1
) AS [table0];";

            // Execute the test for the SQL query
            await TestSupportForAvgAggregation(msSqlQuery);
        }

        /// <summary>
        /// Test to check GraphQL support for sum aggregations.
        /// This test verifies that the SQL query results are correctly mapped to the expected GraphQL format.
        /// </summary>
        [TestMethod]
        public async Task TestSupportForSumAggregation()
        {
            string msSqlQuery = @"
SELECT COALESCE('['+STRING_AGG('{'+N'""sum_price"":' + ISNULL(STRING_ESCAPE(CONVERT(NVARCHAR(MAX), [sum_price]),'json'),'null')+'}',', ')+']','[]') 
FROM (
    SELECT TOP 100 sum([table0].[price]) AS [sum_price] 
    FROM [dbo].[stocks_price] AS [table0] 
    WHERE 1 = 1
) AS [table0];";

            // Execute the test for the SQL query
            await TestSupportForSumAggregation(msSqlQuery);
        }

        /// <summary>
        /// Test to check GraphQL support for count aggregations.
        /// This test verifies that the SQL query results are correctly mapped to the expected GraphQL format.
        /// </summary>
        [TestMethod]
        public async Task TestSupportForCountAggregation()
        {
            string msSqlQuery = @"
SELECT COALESCE('[' + STRING_AGG('{' + N'""count_categoryid"":' + ISNULL(STRING_ESCAPE(CONVERT(NVARCHAR(MAX), [count_categoryid]), 'json'), 'null') + '}', ', ') + ']', '[]')
FROM (
    SELECT TOP 100 count([table0].[categoryid]) AS [count_categoryid]
    FROM [dbo].[stocks_price] AS [table0]
    WHERE 1 = 1
) AS [table0];";

            // Execute the test for the SQL query
            await TestSupportForCountAggregation(msSqlQuery);
        }

        /// <summary>
        /// Test to check GraphQL support for having filter.
        /// This test verifies that the SQL query results are correctly mapped to the expected GraphQL format.
        /// </summary>
        [TestMethod]
        public async Task TestSupportForHavingAggregation()
        {
            string msSqlQuery = @"
SELECT COALESCE('[' + STRING_AGG('{' + N'""max"":' + ISNULL(STRING_ESCAPE(CONVERT(NVARCHAR(MAX), [max]), 'json'), 'null') + '}', ', ') + ']', '[]') 
FROM (
    SELECT TOP 100 max([table0].[id]) AS [max] 
    FROM [dbo].[publishers] AS [table0] 
    WHERE 1 = 1 
    HAVING max([table0].[id]) > 2346
) AS [table0];";

            // Execute the test for the SQL query
            await TestSupportForHavingAggregation(msSqlQuery);
        }

        /// <summary>
        /// Test to check GraphQL support for count aggregations.
        /// This test verifies that the SQL query results are correctly mapped to the expected GraphQL format.
        /// </summary>
        [TestMethod]
        public async Task TestSupportForGroupByHavingAggregation()
        {
            string msSqlQuery = @"
SELECT COALESCE(
    '[' + STRING_AGG(
        '{' + N'""sum_price"":' + ISNULL(STRING_ESCAPE(CONVERT(NVARCHAR(MAX), [sum_price]), 'json'), 'null') + '}', ', '
    ) + ']', '[]'
) 
FROM (
    SELECT TOP 100 
        SUM([table0].[price]) AS [sum_price] 
    FROM [dbo].[stocks_price] AS [table0] 
    WHERE 1 = 1 
    GROUP BY [table0].[categoryid], [table0].[pieceid] 
    HAVING SUM([table0].[price]) > 50
) AS [table0];";

            // Execute the test for the SQL query
            await TestSupportForGroupByHavingAggregation(msSqlQuery);
        }

        /// <summary>
        /// Test to check GraphQL support for count aggregations.
        /// This test verifies that the SQL query results are correctly mapped to the expected GraphQL format.
        /// </summary>
        [TestMethod]
        public async Task TestSupportForGroupByHavingFieldsAggregation()
        {
            string msSqlQuery = @"
SELECT COALESCE(
    '[' + STRING_AGG(
        '{' + N'""categoryid"":' + ISNULL(STRING_ESCAPE(CONVERT(NVARCHAR(MAX), [categoryid]), 'json'), 'null') + ',' +
        N'""pieceid"":' + ISNULL(STRING_ESCAPE(CONVERT(NVARCHAR(MAX), [pieceid]), 'json'), 'null') + ',' +
        N'""sum_price"":' + ISNULL(STRING_ESCAPE(CONVERT(NVARCHAR(MAX), [sum_price]), 'json'), 'null') + ',' +
        N'""count_piece"":' + ISNULL(STRING_ESCAPE(CONVERT(NVARCHAR(MAX), [count_piece]), 'json'), 'null') + '}', ', '
    ) + ']', '[]'
) 
FROM (
    SELECT TOP 100 
        [table0].[categoryid] AS [categoryid], 
        [table0].[pieceid] AS [pieceid], 
        SUM([table0].[price]) AS [sum_price], 
        COUNT([table0].[pieceid]) AS [count_piece] 
    FROM [dbo].[stocks_price] AS [table0] 
    WHERE 1 = 1 
    GROUP BY [table0].[categoryid], [table0].[pieceid] 
    HAVING SUM([table0].[price]) > 50 AND COUNT([table0].[pieceid]) <= 100
) AS [table0];";

            // Execute the test for the SQL query
            await TestSupportForGroupByHavingFieldsAggregation(msSqlQuery);
        }

        /// <summary>
        /// Test to check GraphQL support for count aggregations.
        /// This test verifies that the SQL query results are correctly mapped to the expected GraphQL format.
        /// </summary>
        [TestMethod]
        public async Task TestSupportForGroupByNoAggregation()
        {
            string msSqlQuery = @"
SELECT COALESCE(
    '[' + STRING_AGG(
        '{' + N'""categoryid"":' + ISNULL(STRING_ESCAPE(CONVERT(NVARCHAR(MAX), [categoryid]), 'json'), 'null') + ',' +
        N'""pieceid"":' + ISNULL(STRING_ESCAPE(CONVERT(NVARCHAR(MAX), [pieceid]), 'json'), 'null') + '}', ', '
    ) + ']', '[]'
) 
FROM (
    SELECT TOP 100  
        [table0].[categoryid] AS [categoryid], 
        [table0].[pieceid] AS [pieceid]  
    FROM [dbo].[stocks_price] AS [table0] 
    WHERE 1 = 1 
    GROUP BY [table0].[categoryid], [table0].[pieceid]
) AS [table0]";

            // Execute the test for the SQL query
            await TestSupportForGroupByNoAggregation(msSqlQuery);
        }

        /// <summary>
        /// Test to check that an exception is thrown when both items and groupBy are present in the same query.
        /// </summary>
        [TestMethod]
        public async Task TestInvalidQueryWithItemsAndGroupBy()
        {
            string graphQLQueryName = "stocks_prices";
            string graphQLQuery = @"
    {
        stocks_prices {
            items {
                price
            }
            groupBy {
                aggregations {
                    sum_price: sum(field: price)
                }
            }
        }
    }";

            JsonElement result = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            if (result[0].TryGetProperty("message", out JsonElement message))
            {
                Assert.IsTrue(message.ToString() == "Cannot have both groupBy and items in the same query", "Requesting groupby and items in same query should fail.");
            }
        }

        /// <summary>
        /// Test groupby selection fields not matching arguments.
        /// </summary>
        [TestMethod]
        public async Task TestGroupBySelectionsNotPresentInArguments()
        {
            string graphQLQueryName = "stocks_prices";
            string graphQLQuery = @"
    {
        stocks_prices {
            groupBy(fields: [categoryid, pieceid]) {
                fields
                {
                    categoryid
                    pieceid,
                    price
                }
                aggregations {
                    sum_price: sum(field: price, having:{ gt: 50 })
                    count_piece: count(field: pieceid, having: { lte : 100 })
                }
            }
        }
    }";

            JsonElement result = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            if (result[0].TryGetProperty("message", out JsonElement message))
            {
                Assert.IsTrue(message.ToString() == "Groupby fields in selection must match the fields in the groupby argument.");
            }
        }

        [TestMethod]
        public override async Task TestNoAggregationOptionsForTableWithoutNumericFields()
        {
            await base.TestNoAggregationOptionsForTableWithoutNumericFields();
        }

        /// <summary>
        /// When the feature flag object is passed to build GraphQL runtime objects
        /// Runtime config can correctly get the value
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public void TestEnableDwNto1JoinQueryFeatureFlagLoadedFromRuntime()
        {
            RuntimeConfig mockConfig = new(
               Schema: "",
               DataSource: new(DatabaseType.MySQL, string.Empty, new()),
               Runtime: new(
                   Rest: new(),
                   GraphQL: new(FeatureFlags: new()
                   {
                       EnableDwNto1JoinQueryOptimization = true
                   }),
                   Mcp: new(),
                   Host: new(null, null)
               ),
               Entities: new(new Dictionary<string, Entity>())
            );

            Assert.IsTrue(mockConfig.EnableDwNto1JoinOpt);
        }

        /// <summary>
        /// When the feature flag object is NOT passed to build GraphQL runtime objects
        /// Runtime config can correctly get the default value instead
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public void TestEnableDwNto1JoinQueryFeatureFlagDefaultValueLoaded()
        {
            RuntimeConfig mockConfig = new(
               Schema: "",
               DataSource: new(DatabaseType.MySQL, string.Empty, new()),
               Runtime: new(
                   Rest: new(),
                   GraphQL: new(),
                   Mcp: new(),
                   Host: new(null, null)
               ),
               Entities: new(new Dictionary<string, Entity>())
            );

            FeatureFlags expect = new();

            Assert.AreEqual(expect.EnableDwNto1JoinQueryOptimization, mockConfig.EnableDwNto1JoinOpt);
        }
        #endregion
    }
}
