// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
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
            await InitializeTestFixture();
        }

        #region Tests
        [TestMethod]
        public async Task MultipleResultQuery()
        {
            string postgresQuery = $"SELECT json_agg(to_jsonb(table0)) FROM (SELECT id, title FROM books ORDER BY id asc LIMIT 100) as table0";
            await MultipleResultQuery(postgresQuery);
        }

        [TestMethod]
        public async Task MultipleResultQueryWithMappings()
        {
            string postgresQuery = $"SELECT json_agg(to_jsonb(table0)) FROM (SELECT __column1 AS column1, __column2 AS column2 FROM GQLMappings ORDER BY __column1 asc LIMIT 100) as table0";
            await MultipleResultQueryWithMappings(postgresQuery);
        }

        /// <summary>
        /// Gets array of results for querying a table containing computed columns.
        /// </summary>
        /// <check>rows from sales table</check>
        [TestMethod]
        public async Task MultipleResultQueryContainingComputedColumns()
        {
            string postgresQuery = @"SELECT json_agg(to_jsonb(table0)) FROM
                (SELECT
                    id,
                    item_name,
                    subtotal,
                    tax,
                    total
                FROM sales ORDER BY id asc LIMIT 100) as table0";
            await MultipleResultQueryContainingComputedColumns(postgresQuery);
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
SELECT COALESCE(jsonb_agg(to_jsonb(""subq7"")), '[]') AS ""data""
FROM
    (SELECT ""table0"".""id"" AS ""id"",
            ""table0"".""title"" AS ""title"",
            ""table1_subq"".""data"" AS ""websiteplacement""
     FROM ""public"".""books"" AS ""table0""
     LEFT OUTER JOIN LATERAL
         (SELECT to_jsonb(""subq6"") AS ""data""
          FROM
              (SELECT ""table1"".""price"" AS ""price""
               FROM ""public"".""book_website_placements"" AS ""table1""
               WHERE ""table1"".""book_id"" = ""table0"".""id""
               ORDER BY ""table1"".""id"" ASC
               LIMIT 1) AS ""subq6"") AS ""table1_subq"" ON TRUE
     WHERE 1 = 1
     ORDER BY ""table0"".""id"" ASC
     LIMIT 100) AS ""subq7""
            ";

            await OneToOneJoinQuery(postgresQuery);
        }

        /// <summary>
        /// Test query on One-To-One relationship when the fields defining
        /// the relationship in the entity include fields that are mapped in
        /// that same entity.
        /// <summary>
        [TestMethod]
        public async Task OneToOneJoinQueryWithMappedFieldNamesInRelationship()
        {
            string postgresQuery = @"
SELECT COALESCE(jsonb_agg(to_jsonb(""subq7"")), '[]') AS ""data""
FROM
    (SELECT ""table0"".""species"" AS ""fancyName"",
            ""table1_subq"".""data"" AS ""fungus""
     FROM ""public"".""trees"" AS ""table0""
     LEFT OUTER JOIN LATERAL
         (SELECT to_jsonb(""subq6"") AS ""data""
          FROM
              (SELECT ""table1"".""habitat"" AS ""habitat""
               FROM ""public"".""fungi"" AS ""table1""
               WHERE ""table1"".""habitat"" = ""table0"".""species""
               ORDER BY ""table1"".""habitat"" ASC
               LIMIT 1) AS ""subq6"") AS ""table1_subq"" ON TRUE
     WHERE 1 = 1
     LIMIT 100) AS ""subq7""
            ";

            await OneToOneJoinQueryWithMappedFieldNamesInRelationship(postgresQuery);
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
        public async Task QueryWithSingleColumnPrimaryKeyAndMappings()
        {
            string postgresQuery = @"
                SELECT to_jsonb(subq) AS data
                FROM (
                    SELECT table0.__column1 AS column1
                    FROM GQLMappings AS table0
                    WHERE __column1 = 1
                    ORDER BY __column1 asc
                    LIMIT 1
                ) AS subq
            ";

            await QueryWithSingleColumnPrimaryKeyAndMappings(postgresQuery);
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
                      table1_subq.data AS myseries
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

        [TestMethod]
        public async Task TestQueryOnBasicView()
        {
            string postgresQuery = $"SELECT json_agg(to_jsonb(table0)) FROM (SELECT id, title FROM books_view_all ORDER BY id ASC LIMIT 5) as table0";
            await base.TestQueryOnBasicView(postgresQuery);
        }

        [TestMethod]
        public async Task TestQueryOnCompositeView()
        {
            string postgresQuery = $"SELECT json_agg(to_jsonb(table0)) FROM (SELECT id, name FROM books_publishers_view_composite ORDER BY id ASC LIMIT 5) as table0";
            await base.TestQueryOnCompositeView(postgresQuery);
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
                DatabaseType.PostgreSQL,
                TestCategory.POSTGRESQL);
        }

        #endregion
    }
}
