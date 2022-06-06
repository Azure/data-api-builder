using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Services;
using HotChocolate.Language;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests
{
    /// <summary>
    /// Test GraphQL Queries validating proper resolver/engine operation.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class MsSqlGraphQLQueryTests : GraphQLQueryTestBase
    {
        #region Test Fixture Setup
        /// <summary>
        /// Sets up test fixture for class, only to be run once per test run, as defined by
        /// MSTest decorator.
        /// </summary>
        /// <param name="context"></param>
        [ClassInitialize]
        public static async Task InitializeTestFixture(TestContext context)
        {
            await InitializeTestFixture(context, TestCategory.MSSQL);

            // Setup GraphQL Components
            //
            _graphQLService = new GraphQLService(
                _runtimeConfigPath,
                _queryEngine,
                _mutationEngine,
                new DocumentCache(),
                new Sha256DocumentHashProvider(),
                _sqlMetadataProvider);
            _graphQLController = new GraphQLController(_graphQLService);
        }

        #endregion

        #region Tests
        /// <summary>
        /// Gets array of results for querying more than one item.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task MultipleResultQuery()
        {
            string msSqlQuery = $"SELECT id, title FROM books ORDER BY id FOR JSON PATH, INCLUDE_NULL_VALUES";
            await MultipleResultQuery(msSqlQuery);
        }

        [TestMethod]
        public async Task MultipleResultQueryWithVariables()
        {
            string msSqlQuery = $"SELECT id, title FROM books ORDER BY id FOR JSON PATH, INCLUDE_NULL_VALUES";
            await MultipleResultQueryWithVariables(msSqlQuery);
        }

        /// <summary>
        /// Gets array of results for querying more than one item.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public override async Task MultipleResultJoinQuery()
        {
            await base.MultipleResultJoinQuery();
        }

        /// <summary>
        /// Test One-To-One relationship both directions
        /// (book -> website placement, website placememnt -> book)
        /// <summary>
        [TestMethod]
        public async Task OneToOneJoinQuery()
        {
            string msSqlQuery = @"
                SELECT
                  TOP 1 [table0].[id] AS [id],
                  JSON_QUERY ([table1_subq].[data]) AS [websiteplacement]
                FROM
                  [books] AS [table0]
                  OUTER APPLY (
                    SELECT
                      TOP 1 [table1].[id] AS [id],
                      [table1].[price] AS [price],
                      JSON_QUERY ([table2_subq].[data]) AS [books]
                    FROM
                      [book_website_placements] AS [table1]
                      OUTER APPLY (
                        SELECT
                          TOP 1 [table2].[id] AS [id]
                        FROM
                          [books] AS [table2]
                        WHERE
                          [table1].[book_id] = [table2].[id]
                        ORDER BY
                          [table2].[id] Asc FOR JSON PATH,
                          INCLUDE_NULL_VALUES,
                          WITHOUT_ARRAY_WRAPPER
                      ) AS [table2_subq]([data])
                    WHERE
                      [table1].[book_id] = [table0].[id]
                    ORDER BY
                      [table1].[id] Asc FOR JSON PATH,
                      INCLUDE_NULL_VALUES,
                      WITHOUT_ARRAY_WRAPPER
                  ) AS [table1_subq]([data])
                WHERE
                  [table0].[id] = 1
                ORDER BY
                  [table0].[id] Asc FOR JSON PATH,
                  INCLUDE_NULL_VALUES,
                  WITHOUT_ARRAY_WRAPPER";

            await OneToOneJoinQuery(msSqlQuery);
        }

        /// <summary>
        /// This deeply nests a many-to-one/one-to-many join multiple times to
        /// show that it still results in a valid query.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public override async Task DeeplyNestedManyToOneJoinQuery()
        {
            await base.DeeplyNestedManyToManyJoinQuery();
        }

        /// <summary>
        /// This deeply nests a many-to-many join multiple times to show that
        /// it still results in a valid query.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public override async Task DeeplyNestedManyToManyJoinQuery()
        {
            await base.DeeplyNestedManyToManyJoinQuery();
        }

        /// <summary>
        /// This deeply nests a many-to-many join multiple times to show that
        /// it still results in a valid query.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public override async Task DeeplyNestedManyToManyJoinQueryWithVariables()
        {
            await base.DeeplyNestedManyToManyJoinQueryWithVariables();
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
        public async Task QueryWithMultileColumnPrimaryKey()
        {
            string msSqlQuery = @"
                SELECT TOP 1 content FROM reviews
                WHERE id = 568 AND book_id = 1 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER
            ";

            await QueryWithMultileColumnPrimaryKey(msSqlQuery);
        }

        [TestMethod]
        public override async Task QueryWithNullResult()
        {
            await base.QueryWithNullResult();
        }

        /// <sumary>
        /// Test if first param successfully limits list quries
        /// </summary>
        [TestMethod]
        public override async Task TestFirstParamForListQueries()
        {
            await base.TestFirstParamForListQueries();
        }

        /// <sumary>
        /// Test if filter param successfully filters the query results
        /// </summary>
        [TestMethod]
        public override async Task TestFilterParamForListQueries()
        {
            await base.TestFilterParamForListQueries();
        }

        /// <summary>
        /// Get all instances of a type with nullable interger fields
        /// </summary>
        [TestMethod]
        public async Task TestQueryingTypeWithNullableIntFields()
        {
            string msSqlQuery = $"SELECT TOP 100 id, title, issue_number FROM [foo].[magazines] ORDER BY id FOR JSON PATH, INCLUDE_NULL_VALUES";
            await TestQueryingTypeWithNullableIntFields(msSqlQuery);
        }

        /// <summary>
        /// Get all instances of a type with nullable string fields
        /// </summary>
        [TestMethod]
        public async Task TestQueryingTypeWithNullableStringFields()
        {
            string msSqlQuery = $"SELECT TOP 100 id, username FROM website_users ORDER BY id FOR JSON PATH, INCLUDE_NULL_VALUES";
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
            string msSqlQuery = $"SELECT TOP 2 id AS book_id, title AS book_title FROM books ORDER by id FOR JSON PATH, INCLUDE_NULL_VALUES";
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
            string msSqlQuery = $"SELECT TOP 2 id AS book_id, title AS title FROM books ORDER by id FOR JSON PATH, INCLUDE_NULL_VALUES";
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

        #endregion

        #region Negative Tests

        [TestMethod]
        public override async Task TestInvalidFirstParamQuery()
        {
            await base.TestInvalidFirstParamQuery();
        }

        [TestMethod]
        public override async Task TestInvalidFilterParamQuery()
        {
            await base.TestInvalidFilterParamQuery();
        }

        #endregion
    }
}
