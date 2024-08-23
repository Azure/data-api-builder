// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLQueryTests
{
    /// <summary>
    /// Test GraphQL Queries validating proper resolver/engine operation.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class MsSqlGraphQLQueryTests : GraphQLQueryTestBase
    {
        /// <summary>
        /// Set the database engine for the tests
        /// </summary>
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.MSSQL;
            await InitializeTestFixture();
        }

        #region Tests
        /// <summary>
        /// Gets array of results for querying more than one item.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task MultipleResultQuery()
        {
            string msSqlQuery = $"SELECT id, title FROM books ORDER BY id asc FOR JSON PATH, INCLUDE_NULL_VALUES";
            await MultipleResultQuery(msSqlQuery);
        }

        /// <summary>
        /// Gets array of results for querying a table containing computed columns.
        /// </summary>
        /// <check>rows from sales table</check>
        [TestMethod]
        public async Task MultipleResultQueryContainingComputedColumns()
        {
            string msSqlQuery = @"
                SELECT
                    id,
                    item_name,
                    ROUND(subtotal,2) AS subtotal,
                    ROUND(tax,2) AS tax,
                    ROUND(total,2) AS total
                FROM
                    sales
                ORDER BY id asc FOR JSON PATH, INCLUDE_NULL_VALUES";
            await MultipleResultQueryContainingComputedColumns(msSqlQuery);
        }

        [TestMethod]
        public async Task MultipleResultQueryWithVariables()
        {
            string msSqlQuery = $"SELECT id, title FROM books ORDER BY id asc FOR JSON PATH, INCLUDE_NULL_VALUES";
            await MultipleResultQueryWithVariables(msSqlQuery);
        }

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
            string msSqlQuery = @"
                SELECT TOP 100 [table0].[id] AS [id]
                    ,[table0].[title] AS [title]
                    ,JSON_QUERY([table1_subq].[data]) AS [websiteplacement]
                FROM [dbo].[books] AS [table0]
                OUTER APPLY (
                    SELECT TOP 1 [table1].[price] AS [price]
                    FROM [dbo].[book_website_placements] AS [table1]
                    WHERE [table1].[book_id] = [table0].[id]
                    ORDER BY [table1].[id] ASC
                    FOR JSON PATH
                        ,INCLUDE_NULL_VALUES
                        ,WITHOUT_ARRAY_WRAPPER
                    ) AS [table1_subq]([data])
                WHERE 1 = 1
                ORDER BY [table0].[id] ASC
                FOR JSON PATH
                    ,INCLUDE_NULL_VALUES";

            await OneToOneJoinQuery(msSqlQuery);
        }

        /// <summary>
        /// Test query on One-To-One relationship when the fields defining
        /// the relationship in the entity include fields that are mapped in
        /// that same entity.
        /// <summary>
        [TestMethod]
        public async Task OneToOneJoinQueryWithMappedFieldNamesInRelationship()
        {
            string msSqlQuery = @"
                SELECT TOP 100 [table0].[species] AS [fancyName]
                    ,JSON_QUERY([table1_subq].[data]) AS [fungus]
                FROM [dbo].[trees] AS [table0]
                OUTER APPLY (
                    SELECT TOP 1 [table1].[habitat] AS [habitat]
                    FROM [dbo].[fungi] AS [table1]
                    WHERE [table1].[habitat] = [table0].[species]
                    ORDER BY [table1].[habitat] ASC
                    FOR JSON PATH
                        ,INCLUDE_NULL_VALUES
                        ,WITHOUT_ARRAY_WRAPPER
                    ) AS [table1_subq]([data])
                WHERE 1 = 1
                FOR JSON PATH
                    ,INCLUDE_NULL_VALUES";

            await OneToOneJoinQueryWithMappedFieldNamesInRelationship(msSqlQuery);
        }

        [TestMethod]
        public async Task QueryWithSingleColumnPrimaryKey()
        {
            string msSqlQuery = @"
                SELECT title FROM books
                WHERE id = 2 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER
            ";

            await QueryWithSingleColumnPrimaryKey(msSqlQuery);
        }

        [TestMethod]
        public async Task QueryWithSingleColumnPrimaryKeyAndMappings()
        {
            string msSqlQuery = @"
                SELECT [__column1] AS [column1] FROM GQLMappings
                WHERE [__column1] = 1 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER
            ";

            await QueryWithSingleColumnPrimaryKeyAndMappings(msSqlQuery);
        }

        [TestMethod]
        public async Task QueryWithMultipleColumnPrimaryKey()
        {
            string msSqlQuery = @"
                SELECT TOP 1 content FROM reviews
                WHERE id = 568 AND book_id = 1 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER
            ";

            await QueryWithMultipleColumnPrimaryKey(msSqlQuery);
        }

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
        /// Get all instances of a type with nullable interger fields
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

        [TestMethod]
        public async Task TestSettingOrderByOrderUsingVariable()
        {
            string msSqlQuery = $"SELECT TOP 4 id, title FROM books ORDER BY id DESC FOR JSON PATH, INCLUDE_NULL_VALUES";
            await TestSettingOrderByOrderUsingVariable(msSqlQuery);
        }

        [TestMethod]
        public async Task TestSettingComplexArgumentUsingVariables()
        {
            string msSqlQuery = $"SELECT TOP 100 id, title FROM books ORDER BY id ASC FOR JSON PATH, INCLUDE_NULL_VALUES";
            await base.TestSettingComplexArgumentUsingVariables(msSqlQuery);
        }

        [TestMethod]
        public async Task TestQueryWithExplicitlyNullArguments()
        {
            string msSqlQuery = $"SELECT id, title FROM books ORDER BY id asc FOR JSON PATH, INCLUDE_NULL_VALUES";
            await TestQueryWithExplicitlyNullArguments(msSqlQuery);
        }

        [TestMethod]
        public async Task TestQueryOnBasicView()
        {
            string msSqlQuery = $"SELECT TOP 5 id, title FROM books_view_all ORDER BY id FOR JSON PATH, INCLUDE_NULL_VALUES";
            await base.TestQueryOnBasicView(msSqlQuery);
        }

        /// <summary>
        /// Test to execute stored-procedure in graphQL that returns a single row
        /// </summary>
        [TestMethod]
        public async Task TestStoredProcedureQueryForGettingSingleRow()
        {
            string msSqlQuery = $"EXEC dbo.get_publisher_by_id @id=1234";
            await TestStoredProcedureQueryForGettingSingleRow(msSqlQuery);
        }

        /// <summary>
        /// Test to execute stored-procedure in graphQL that returns a list(multiple rows)
        /// </summary>
        [TestMethod]
        public async Task TestStoredProcedureQueryForGettingMultipleRows()
        {
            string msSqlQuery = $"EXEC dbo.get_books";
            await TestStoredProcedureQueryForGettingMultipleRows(msSqlQuery);
        }

        /// <summary>
        /// Test to execute stored-procedure in graphQL that counts the total number of rows
        /// </summary>
        [TestMethod]
        public async Task TestStoredProcedureQueryForGettingTotalNumberOfRows()
        {
            string msSqlQuery = $"EXEC dbo.count_books";
            await TestStoredProcedureQueryForGettingTotalNumberOfRows(msSqlQuery);
        }

        /// <summary>
        /// Test to execute stored-procedure in graphQL that contains null in the result set.
        /// </summary>
        [TestMethod]
        public async Task TestStoredProcedureQueryWithResultsContainingNull()
        {
            string msSqlQuery = $"EXEC dbo.get_authors_history_by_first_name @firstName='Aaron'";
            await TestStoredProcedureQueryWithResultsContainingNull(msSqlQuery);
        }

        [TestMethod]
        public async Task TestQueryOnCompositeView()
        {
            string msSqlQuery = $"SELECT TOP 5 id, name FROM books_publishers_view_composite ORDER BY id FOR JSON PATH, INCLUDE_NULL_VALUES";
            await base.TestQueryOnCompositeView(msSqlQuery);
        }

        /// <inheritdoc />
        [DataTestMethod]
        [DataRow(null, null, 1113, "Real Madrid", DisplayName = "No Overriding of existing relationship fields in DB.")]
        [DataRow(new string[] { "new_club_id" }, new string[] { "id" }, 1111, "Manchester United", DisplayName = "Overriding existing relationship fields in DB.")]
        public async Task TestConfigTakesPrecedenceForRelationshipFieldsOverDB(
            string[] sourceFields,
            string[] targetFields,
            int club_id,
            string club_name)
        {
            await TestConfigTakesPrecedenceForRelationshipFieldsOverDB(
                sourceFields,
                targetFields,
                club_id,
                club_name,
                DatabaseType.MSSQL,
                TestCategory.MSSQL);
        }

        /// <inheritdoc/>>
        [TestMethod]
        public async Task QueryAgainstSPWithOnlyTypenameInSelectionSet()
        {
            string dbQuery = "select count(*) as count from books";
            await QueryAgainstSPWithOnlyTypenameInSelectionSet(dbQuery);
        }

        /// <summary>
        /// Checks failure on providing arguments with no default in runtimeconfig.
        /// In this test, there is no default value for the argument 'id' in runtimeconfig, nor is it specified in the query.
        /// Stored procedure expects id argument to be provided.
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
            SqlTestHelper.TestForErrorInGraphQLResponse(result.ToString(), message: "Did not provide all procedure params");
        }
        #endregion
    }
}
