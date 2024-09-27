// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text.Json;
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
        #endregion
    }
}
