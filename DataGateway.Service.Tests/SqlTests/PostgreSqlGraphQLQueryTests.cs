using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Services;
using HotChocolate.Language;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests
{

    [TestClass, TestCategory(TestCategory.POSTGRESQL)]
    public class PostgreSqlGraphQLQueryTests : GraphQLQueryTestBase
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
            await InitializeTestFixture(context, TestCategory.POSTGRESQL);

            // Setup GraphQL Components
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
        [TestMethod]
        public async Task MultipleResultQuery()
        {
            string postgresQuery = $"SELECT json_agg(to_jsonb(table0)) FROM (SELECT id, title FROM books ORDER BY id) as table0 LIMIT 100";
            await MultipleResultQuery(postgresQuery);
        }

        [TestMethod]
        public async Task MultipleResultQueryWithVariables()
        {
            string postgresQuery = $"SELECT json_agg(to_jsonb(table0)) FROM (SELECT id, title FROM books ORDER BY id) as table0 LIMIT 100";
            await MultipleResultQueryWithVariables(postgresQuery);
        }

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
            string postgresQuery = @"
SELECT
  to_jsonb(""subq12"") AS ""data""
FROM
  (
    SELECT
      ""table0"".""id"" AS ""id"",
      ""table1_subq"".""data"" AS ""websiteplacement""
    FROM
      ""public"".""books"" AS ""table0""
      LEFT OUTER JOIN LATERAL(
        SELECT
          to_jsonb(""subq11"") AS ""data""
        FROM
          (
            SELECT
              ""table1"".""id"" AS ""id"",
              ""table1"".""price"" AS ""price"",
              ""table2_subq"".""data"" AS ""books""
            FROM
              ""public"".""book_website_placements"" AS ""table1""
              LEFT OUTER JOIN LATERAL(
                SELECT
                  to_jsonb(""subq10"") AS ""data""
                FROM
                  (
                    SELECT
                      ""table2"".""id"" AS ""id""
                    FROM
                      ""public"".""books"" AS ""table2""
                    WHERE
                      ""table1"".""book_id"" = ""table2"".""id""
                    ORDER BY
                      ""table2"".""id"" Asc
                    LIMIT
                      1
                  ) AS ""subq10""
              ) AS ""table2_subq"" ON TRUE
            WHERE
              ""table1"".""book_id"" = ""table0"".""id""
            ORDER BY
              ""table1"".""id"" Asc
            LIMIT
              1
          ) AS ""subq11""
      ) AS ""table1_subq"" ON TRUE
    WHERE
      ""table0"".""id"" = 1
    ORDER BY
      ""table0"".""id"" Asc
    LIMIT
      1
  ) AS ""subq12""
            ";

            await OneToOneJoinQuery(postgresQuery);
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

        [TestMethod]
        public async Task QueryWithSingleColumnPrimaryKey()
        {
            string postgresQuery = @"
                SELECT to_jsonb(subq) AS data
                FROM (
                    SELECT table0.title AS title
                    FROM books AS table0
                    WHERE id = 2
                    ORDER BY id
                    LIMIT 1
                ) AS subq
            ";

            await QueryWithSingleColumnPrimaryKey(postgresQuery);
        }

        [TestMethod]
        public async Task QueryWithMultileColumnPrimaryKey()
        {
            string postgresQuery = @"
                SELECT to_jsonb(subq) AS data
                FROM (
                    SELECT table0.content AS content
                    FROM reviews AS table0
                    WHERE id = 568 AND book_id = 1
                    ORDER BY id, book_id
                    LIMIT 1
                ) AS subq
            ";

            await QueryWithMultileColumnPrimaryKey(postgresQuery);
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
            string postgresQuery = $"SELECT json_agg(to_jsonb(table0)) FROM (SELECT id, title, \"issue_number\" FROM foo.magazines ORDER BY id) as table0 LIMIT 100";
            await TestQueryingTypeWithNullableIntFields(postgresQuery);

        }

        /// <summary>
        /// Get all instances of a type with nullable string fields
        /// </summary>
        [TestMethod]
        public async Task TestQueryingTypeWithNullableStringFields()
        {
            string postgresQuery = $"SELECT json_agg(to_jsonb(table0)) FROM (SELECT id, username FROM website_users ORDER BY id) as table0 LIMIT 100";
            await TestQueryingTypeWithNullableStringFields(postgresQuery);
        }

        /// <summary>
        /// Test to check graphQL support for aliases(arbitrarily set by user while making request).
        /// book_id and book_title are aliases used for corresponding query fields.
        /// The response for the query will contain the alias instead of raw db field.
        /// </summary>
        [TestMethod]
        public async Task TestAliasSupportForGraphQLQueryFields()
        {
            string postgresQuery = @"
SELECT
  json_agg(to_jsonb(table0))
FROM
  (
    SELECT
      id as book_id,
      title as book_title
    FROM
      books
    ORDER BY
      id
    LIMIT 2
  ) as table0";
            await TestAliasSupportForGraphQLQueryFields(postgresQuery);
        }

        /// <summary>
        /// Test to check graphQL support for aliases(arbitrarily set by user while making request).
        /// book_id is an alias, while title is the raw db field.
        /// The response for the query will use the alias where it is provided in the query.
        /// </summary>
        [TestMethod]
        public async Task TestSupportForMixOfRawDbFieldFieldAndAlias()
        {
            string postgresQuery = @"
                SELECT
                  json_agg(to_jsonb(table0))
                FROM
                  (
                    SELECT
                      id as book_id,
                      title as title
                    FROM
                      books
                    ORDER BY
                      id
                    LIMIT
                      2
                  ) as table0";
            await TestSupportForMixOfRawDbFieldFieldAndAlias(postgresQuery);
        }

        /// <summary>
        /// Tests orderBy on a list query
        /// </summary>
        [Ignore]
        [TestMethod]
        public async Task TestOrderByInListQuery()
        {
            string postgresQuery = $"SELECT json_agg(to_jsonb(table0)) FROM (SELECT id, title FROM books ORDER BY title DESC, id ASC) as table0 LIMIT 100";
            await TestOrderByInListQuery(postgresQuery);
        }

        /// <summary>
        /// Use multiple order options and order an entity with a composite pk
        /// </summary>
        [Ignore]
        [TestMethod]
        public async Task TestOrderByInListQueryOnCompPkType()
        {
            string postgresQuery = $"SELECT json_agg(to_jsonb(table0)) FROM (SELECT id, content FROM reviews ORDER BY content ASC, id DESC, book_id ASC) as table0 LIMIT 100";
            await TestOrderByInListQueryOnCompPkType(postgresQuery);
        }

        /// <summary>
        /// Tests null fields in orderBy are ignored
        /// meaning that null pk columns are included in the ORDER BY clause
        /// as ASC by default while null non-pk columns are completely ignored
        /// </summary>
        [Ignore]
        [TestMethod]
        public async Task TestNullFieldsInOrderByAreIgnored()
        {
            string postgresQuery = $"SELECT json_agg(to_jsonb(table0)) FROM (SELECT id, title FROM books ORDER BY title DESC, id ASC) as table0 LIMIT 100";
            await TestNullFieldsInOrderByAreIgnored(postgresQuery);
        }

        /// <summary>
        /// Tests that an orderBy with only null fields results in default pk sorting
        /// </summary>
        [Ignore]
        [TestMethod]
        public async Task TestOrderByWithOnlyNullFieldsDefaultsToPkSorting()
        {
            string postgresQuery = $"SELECT json_agg(to_jsonb(table0)) FROM (SELECT id, title FROM books ORDER BY id ASC) as table0 LIMIT 100";
            await TestOrderByWithOnlyNullFieldsDefaultsToPkSorting(postgresQuery);
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
