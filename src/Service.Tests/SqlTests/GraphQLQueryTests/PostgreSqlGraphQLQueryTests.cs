using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLQueryTests
{

    [TestClass, TestCategory(TestCategory.POSTGRESQL)]
    public class PostgreSqlGraphQLQueryTests : GraphQLQueryTestBase
    {
        /// <summary>
        /// Set the database engine for the tests
        /// </summary>
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.POSTGRESQL;
            await InitializeTestFixture(context);
        }

        #region Tests
        [TestMethod]
        public async Task MultipleResultQuery()
        {
            string postgresQuery = $"SELECT json_agg(to_jsonb(table0)) FROM (SELECT id, title FROM books ORDER BY id asc LIMIT 100) as table0";
            await MultipleResultQuery(postgresQuery);
        }

        [TestMethod]
        public async Task MultipleResultQueryWithVariables()
        {
            string postgresQuery = $"SELECT json_agg(to_jsonb(table0)) FROM (SELECT id, title FROM books ORDER BY id asc LIMIT 100) as table0";
            await MultipleResultQueryWithVariables(postgresQuery);
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

        [TestMethod]
        public async Task QueryWithSingleColumnPrimaryKey()
        {
            string postgresQuery = @"
                SELECT to_jsonb(subq) AS data
                FROM (
                    SELECT table0.title AS title
                    FROM books AS table0
                    WHERE id = 2
                    ORDER BY id asc
                    LIMIT 1
                ) AS subq
            ";

            await QueryWithSingleColumnPrimaryKey(postgresQuery);
        }

        [TestMethod]
        public async Task QueryWithMultipleColumnPrimaryKey()
        {
            string postgresQuery = @"
                SELECT to_jsonb(subq) AS data
                FROM (
                    SELECT table0.content AS content
                    FROM reviews AS table0
                    WHERE id = 568 AND book_id = 1
                    ORDER BY id asc, book_id asc
                    LIMIT 1
                ) AS subq
            ";

            await QueryWithMultipleColumnPrimaryKey(postgresQuery);
        }

        [TestMethod]
        public async Task QueryWithNullableForeignKey()
        {
            string postgresQuery = @"
            SELECT to_jsonb(subq7) AS data
            FROM
              (SELECT table0.title AS title,
                      table1_subq.data AS series
               FROM public.comics AS table0
               LEFT OUTER JOIN LATERAL
                 (SELECT to_jsonb(subq6) AS data
                  FROM
                    (SELECT table1.name AS name
                     FROM public.series AS table1
                     WHERE table0.series_id = table1.id
                     ORDER BY table1.id ASC
                     LIMIT 1) AS subq6) AS table1_subq ON TRUE
               WHERE table0.id = 1
               ORDER BY table0.id ASC
               LIMIT 1) AS subq7";

            await QueryWithNullableForeignKey(postgresQuery);
        }

        /// <summary>
        /// Get all instances of a type with nullable interger fields
        /// </summary>
        [TestMethod]
        public async Task TestQueryingTypeWithNullableIntFields()
        {
            string postgresQuery = $"SELECT json_agg(to_jsonb(table0)) FROM (SELECT id, title, \"issue_number\" FROM foo.magazines ORDER BY id asc LIMIT 100) as table0";
            await TestQueryingTypeWithNullableIntFields(postgresQuery);

        }

        /// <summary>
        /// Get all instances of a type with nullable string fields
        /// </summary>
        [TestMethod]
        public async Task TestQueryingTypeWithNullableStringFields()
        {
            string postgresQuery = $"SELECT json_agg(to_jsonb(table0)) FROM (SELECT id, username FROM website_users ORDER BY id asc LIMIT 100) as table0";
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
      id asc
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
                      id asc
                    LIMIT
                      2
                  ) as table0";
            await TestSupportForMixOfRawDbFieldFieldAndAlias(postgresQuery);
        }

        /// <summary>
        /// Tests orderBy on a list query
        /// </summary>
        [TestMethod]
        public async Task TestOrderByInListQuery()
        {
            string postgresQuery = $"SELECT json_agg(to_jsonb(table0)) FROM (SELECT id, title FROM books ORDER BY title DESC, id ASC LIMIT 100) as table0";
            await TestOrderByInListQuery(postgresQuery);
        }

        /// <summary>
        /// Use multiple order options and order an entity with a composite pk
        /// </summary>
        [TestMethod]
        public async Task TestOrderByInListQueryOnCompPkType()
        {
            string postgresQuery = $"SELECT json_agg(to_jsonb(table0)) FROM (SELECT id, content FROM reviews ORDER BY content ASC, id DESC, book_id ASC LIMIT 100) as table0";
            await TestOrderByInListQueryOnCompPkType(postgresQuery);
        }

        /// <summary>
        /// Tests null fields in orderBy are ignored
        /// meaning that null pk columns are included in the ORDER BY clause
        /// as ASC by default while null non-pk columns are completely ignored
        /// </summary>
        [TestMethod]
        public async Task TestNullFieldsInOrderByAreIgnored()
        {
            string postgresQuery = $"SELECT json_agg(to_jsonb(table0)) FROM (SELECT id, title FROM books ORDER BY title DESC, id ASC LIMIT 100) as table0";
            await TestNullFieldsInOrderByAreIgnored(postgresQuery);
        }

        /// <summary>
        /// Tests that an orderBy with only null fields results in default pk sorting
        /// </summary>
        [TestMethod]
        public async Task TestOrderByWithOnlyNullFieldsDefaultsToPkSorting()
        {
            string postgresQuery = $"SELECT json_agg(to_jsonb(table0)) FROM (SELECT id, title FROM books ORDER BY id ASC LIMIT 100) as table0";
            await TestOrderByWithOnlyNullFieldsDefaultsToPkSorting(postgresQuery);
        }

        [TestMethod]
        public async Task TestSettingOrderByOrderUsingVariable()
        {
            string postgresQuery = $"SELECT json_agg(to_jsonb(table0)) FROM (SELECT id, title FROM books ORDER BY id DESC LIMIT 4) as table0";
            await TestSettingOrderByOrderUsingVariable(postgresQuery);
        }

        [TestMethod]
        public async Task TestSettingComplexArgumentUsingVariables()
        {
            string postgresQuery = $"SELECT json_agg(to_jsonb(table0)) FROM (SELECT id, title FROM books ORDER BY id ASC LIMIT 100) as table0";
            await base.TestSettingComplexArgumentUsingVariables(postgresQuery);
        }

        [TestMethod]
        public async Task TestQueryWithExplicitlyNullArguments()
        {
            string postgresQuery = $"SELECT json_agg(to_jsonb(table0)) FROM (SELECT id, title FROM books ORDER BY id asc LIMIT 100) as table0";
            await TestQueryWithExplicitlyNullArguments(postgresQuery);
        }

        #endregion
    }
}
