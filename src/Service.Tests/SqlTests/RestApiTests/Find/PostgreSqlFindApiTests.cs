using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.Controllers;
using Azure.DataApiBuilder.Service.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests.Find
{
    [TestClass, TestCategory(TestCategory.POSTGRESQL)]
    public class PostgreSqlFindApiTests : FindApiTestBase
    {
        protected static string DEFAULT_SCHEMA = "public";
        protected static Dictionary<string, string> _queryMap = new()
        {
            {
                "FindByIdTest",
                @"
                  SELECT to_jsonb(subq) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      WHERE id = 2
                      ORDER BY id
                      LIMIT 1
                  ) AS subq"
            },
            {
                "FindEmptyTable",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT *
                        FROM " + _emptyTableTableName + @"
                    ) AS subq"
            },
            {
                "FindOnTableWithUniqueCharacters",
                @"
                    SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT  ""NoteNum"" AS ""┬─┬ノ( º _ ºノ)"", ""DetailAssessmentAndPlanning""
                        AS ""始計"", ""WagingWar"" AS ""作戰"", ""StrategicAttack"" AS ""謀攻""
                        FROM " + _integrationUniqueCharactersTable + @"
                    ) AS subq
                "
            },
            {
                "FindEmptyResultSetWithQueryFilter",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        WHERE 1 <> 1
                    ) AS subq"
            },
            {
                "FindTestWithQueryStringOneField",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT id
                      FROM " + _integrationTableName + @"
                      ORDER BY id
                  ) AS subq"
            },
            {
                "FindTestWithQueryStringAllFields",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      ORDER BY id
                  ) AS subq"
            },
            {
                "FindByIdTestWithQueryStringFields",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, title
                        FROM " + _integrationTableName + @"
                        WHERE id = 1
                        ORDER BY id
                        LIMIT 1
                    ) AS subq
                "
            },
            {
                "FindTestWithQueryStringMultipleFields",
                @"
                    SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT id, title
                        FROM " + _integrationTableName + @"
                        ORDER BY id
                    ) AS subq
                "
            },
            {
                "FindTestWithFilterQueryStringOneEqFilter",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      WHERE id = 1
                      ORDER BY id
                  ) AS subq"
            },
            {
                "FindTestWithFilterQueryStringValueFirstOneEqFilter",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      WHERE id = 2
                      ORDER BY id
                  ) AS subq"
            },
            {
                "FindTestWithFilterQueryOneGtFilter",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      WHERE id > 3
                      ORDER BY id
                  ) AS subq"
            },
            {
                "FindTestWithFilterQueryOneGeFilter",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      WHERE id >= 4
                      ORDER BY id
                  ) AS subq"
            },
            {
                "FindTestWithFilterQueryOneLtFilter",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      WHERE id < 5
                      ORDER BY id
                  ) AS subq"
            },
            {
                "FindTestWithFilterQueryOneLeFilter",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      WHERE id <= 4
                      ORDER BY id
                  ) AS subq"
            },
            {
                "FindTestWithFilterQueryOneNeFilter",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      WHERE id != 3
                      ORDER BY id
                  ) AS subq"
            },
            {
                "FindTestWithFilterQueryOneNotFilter",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      WHERE not (id < 2)
                      ORDER BY id
                  ) AS subq"
            },
            {
                "FindTestWithFilterQueryOneRightNullEqFilter",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      WHERE NOT (title IS NULL)
                      ORDER BY id
                  ) AS subq"
            },
            {
                "FindTestWithFilterQueryOneLeftNullNeFilter",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      WHERE title IS NOT NULL
                      ORDER BY id
                  ) AS subq"
            },
            {
                "FindTestWithFilterQueryOneLeftNullRightNullGtFilter",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      WHERE NULL > NULL
                      ORDER BY id
                  ) AS subq"
            },
            {
                "FindTestWithFilterQueryStringSingleAndFilter",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      WHERE id < 3 AND id > 1
                      ORDER BY id
                  ) AS subq"
            },
            {
                "FindTestWithFilterQueryStringSingleOrFilter",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      WHERE id < 3 OR id > 4
                      ORDER BY id
                  ) AS subq"
            },
            {
                "FindTestWithFilterQueryStringMultipleAndFilters",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      WHERE id < 4 AND id > 1 AND title != 'Awesome book'
                      ORDER BY id
                  ) AS subq"
            },
            {
                "FindTestWithFilterQueryStringMultipleOrFilters",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      WHERE id = 1 OR id = 2 OR id = 3
                      ORDER BY id
                  ) AS subq"
            },
            {
                "FindTestWithFilterQueryStringMultipleAndOrFilters",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      WHERE (id > 2 AND id < 4) OR (title = 'Awesome book')
                      ORDER BY id
                  ) AS subq"
            },
            {
                "FindTestWithFilterQueryStringMultipleNotAndOrFilters",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      WHERE (NOT (id < 3) OR id < 4) OR NOT (title = 'Awesome book')
                      ORDER BY id
                  ) AS subq"
            },
            {
                "FindTestWithPrimaryKeyContainingForeignKey",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, content
                        FROM reviews" + @"
                        WHERE id = 567 AND book_id = 1
                        ORDER BY id
                        LIMIT 1
                    ) AS subq
                "
            },
            {
                "FindTestWithFirstSingleKeyPagination",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        ORDER BY id
                        LIMIT 1
                    ) AS subq
                "
            },
            {
                "FindTestWithFirstMultiKeyPagination",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT *
                        FROM " + _tableWithCompositePrimaryKey + @"
                        ORDER BY book_id, id
                        LIMIT 1
                    ) AS subq
                "
            },
            {
                "FindTestWithAfterSingleKeyPagination",
                @"
                    SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        WHERE id > 7
                        ORDER BY id
                        LIMIT 100
                    ) AS subq
                "
            },
            {
                "FindTestWithAfterMultiKeyPagination",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT *
                        FROM " + _tableWithCompositePrimaryKey + @"
                        WHERE book_id > 1 OR (book_id = 1 AND id > 567)
                        ORDER BY book_id, id
                        LIMIT 100
                    ) AS subq
                "
            },
            {
                "FindTestWithPaginationVerifSinglePrimaryKeyInAfter",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        ORDER BY id
                        LIMIT 1
                    ) AS subq
                "
            },
            {
                "FindTestWithPaginationVerifMultiplePrimaryKeysInAfter",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT *
                        FROM " + _tableWithCompositePrimaryKey + @"
                        ORDER BY book_id, id
                        LIMIT 1
                    ) AS subq
                "
            },
            {
                "FindTestWithQueryStringAllFieldsOrderByAsc",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      ORDER BY title, id
                  ) AS subq"
            },
            {
                "FindTestWithQueryStringAllFieldsMappedEntityOrderByAsc",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                       SELECT  ""treeId"", ""species"" AS ""fancyName"", ""region"", ""height""
                        FROM " + _integrationMappingTable + @"
                      ORDER BY ""species""
                  ) AS subq"
            },
            {
                "FindTestWithFirstAndSpacedColumnOrderBy",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableHasColumnWithSpace + @"
                      ORDER BY ""Last Name""
                      LIMIT 1
                  ) AS subq"
            },
            {
                "FindTestWithQueryStringSpaceInNamesOrderByAsc",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableHasColumnWithSpace + @"
                      ORDER BY ""ID Number""
                  ) AS subq"
            },
            {
                "FindTestWithQueryStringAllFieldsOrderByDesc",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      ORDER BY publisher_id desc, id
                  ) AS subq"
            },
            {
                "FindTestWithFirstSingleKeyPaginationAndOrderBy",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        ORDER BY title, id
                        LIMIT 1
                    ) AS subq
                "
            },
            {
                "FindTestVerifyMaintainColumnOrderForOrderBy",
                @"
                    SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        ORDER BY id desc, publisher_id
                    ) AS subq
                "
            },
            {
                "FindTestVerifyMaintainColumnOrderForOrderByInReverse",
                @"
                    SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        ORDER BY publisher_id, id desc
                    ) AS subq
                "
            },
            {
                "FindTestWithFirstSingleKeyIncludedInOrderByAndPagination",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        ORDER BY id
                        LIMIT 1
                    ) AS subq
                "
            },
            {
                "FindTestWithFirstTwoOrderByAndPagination",
                @"
                    SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        ORDER BY id
                        LIMIT 2
                    ) AS subq
                "
            },
            {
                "FindTestWithFirstTwoVerifyAfterFormedCorrectlyWithOrderBy",
                @"
                    SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTieBreakTable + @"
                        ORDER BY birthdate, name, id desc
                        LIMIT 2
                    ) AS subq
                "
            },
            {
                "FindTestWithFirstTwoVerifyAfterBreaksTieCorrectlyWithOrderBy",
                @"
                    SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTieBreakTable + @"
                        WHERE ((birthdate > '2001-01-01') OR(birthdate = '2001-01-01' AND name > 'Aniruddh') OR
                        (birthdate = '2001-01-01' AND name = 'Aniruddh' AND id > 125))
                        ORDER BY birthdate, name, id
                        LIMIT 2
                    ) AS subq
                "
            },
            {
                "FindTestWithFirstMultiKeyIncludeAllInOrderByAndPagination",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT *
                        FROM " + _tableWithCompositePrimaryKey + @"
                        ORDER BY id desc, book_id
                        LIMIT 1
                    ) AS subq
                "
            },
            {
                "FindTestWithFirstMultiKeyIncludeOneInOrderByAndPagination",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT *
                        FROM " + _tableWithCompositePrimaryKey + @"
                        ORDER BY book_id, id
                        LIMIT 1
                    ) AS subq
                "
            },
            {
                "FindTestWithFirstAndMultiColumnOrderBy",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        ORDER BY publisher_id desc, title desc
                        LIMIT 1
                    ) AS subq
                "
            },
            {
                "FindTestWithFirstAndTiedColumnOrderBy",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        ORDER BY publisher_id desc, id asc
                        LIMIT 1
                    ) AS subq
                "
            },
            {
                "FindTestWithFirstMultiKeyPaginationAndOrderBy",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT *
                        FROM " + _tableWithCompositePrimaryKey + @"
                        ORDER BY content desc, book_id, id
                        LIMIT 1
                    ) AS subq
                "
            },
            {
                "FindTestWithMappedFieldsToBeReturned",
                @"
                    SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT  ""treeId"", ""species"" AS ""Scientific Name"", ""region"" AS ""United State's Region"", ""height""
                        FROM " + _integrationMappingTable + @"
                    ) AS subq
                "
            },
            {
                "FindTestWithSingleMappedFieldsToBeReturned",
                @"
                    SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT  ""species"" AS ""Scientific Name""
                        FROM " + _integrationMappingTable + @"
                    ) AS subq
                "
            },
            {
                "FindTestWithUnMappedFieldsToBeReturned",
                @"
                    SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT  ""treeId""
                        FROM " + _integrationMappingTable + @"
                    ) AS subq
                "
            },
            {
                "FindTestWithDifferentMappedFieldsAndFilter",
                @"
                    SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT  ""treeId"", ""species"" AS ""fancyName"", ""region"", ""height""
                        FROM " + _integrationMappingTable + @"
                        WHERE species = 'Tsuga terophylla'
                    ) AS subq
                "
            },
            {
                "FindTestWithDifferentMappedFieldsAndOrderBy",
                @"
                    SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT  ""treeId"", ""species"" AS ""fancyName"", ""region"", ""height""
                        FROM " + _integrationMappingTable + @"
                        ORDER BY species
                    ) AS subq
                "
            },
            {
                "FindTestWithDifferentMappingFirstSingleKeyPaginationAndOrderBy",
                @"
                    SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT  ""treeId"", ""species"" AS ""fancyName"", ""region"", ""height""
                        FROM " + _integrationMappingTable + @"
                        ORDER BY species
                        LIMIT 1
                    ) AS subq
                "
            },
            {
                "FindTestWithDifferentMappingAfterSingleKeyPaginationAndOrderBy",
                @"
                    SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT  ""treeId"", ""species"" AS ""fancyName"", ""region"", ""height""
                        FROM " + _integrationMappingTable + @"
                        WHERE ""treeId"" < 2
                        ORDER BY species, ""treeId""
                        LIMIT 101
                    ) AS subq
                "
            }
        };

        #region Test Fixture Setup

        /// <summary>
        /// Sets up test fixture for class, only to be run once per test run, as defined by
        /// MSTest decorator.
        /// </summary>
        /// <param name="context"></param>
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.POSTGRESQL;
            await InitializeTestFixture(context);
            _restService = new RestService(_queryEngine,
                _mutationEngine,
                _sqlMetadataProvider,
                _httpContextAccessor.Object,
                _authorizationService.Object,
                _authorizationResolver,
                _runtimeConfigProvider);
            _restController = new RestController(_restService,
                                                 _restControllerLogger);
        }

        #endregion

        [TestCleanup]
        public async Task TestCleanup()
        {
            await ResetDbStateAsync();
        }

        [TestMethod]
        [Ignore]
        public override Task FindOnViews()
        {
            throw new NotImplementedException();
        }

        [TestMethod]
        [Ignore]
        public override Task FindTestWithQueryStringOnViews()
        {
            throw new NotImplementedException();
        }

        [TestMethod]
        [Ignore]
        public override Task FindTestWithInvalidFieldsInQueryStringOnViews()
        {
            throw new NotImplementedException();
        }

        public override string GetDefaultSchema()
        {
            return DEFAULT_SCHEMA;
        }

        /// <summary>
        /// We include a '.' for the Edm Model
        /// schema to allow both MsSql/PostgreSql
        /// and MySql to share code. MySql does not
        /// include a '.' but PostgreSql does so
        /// we must include here.
        /// </summary>
        /// <returns></returns>
        public override string GetDefaultSchemaForEdmModel()
        {
            return $"{DEFAULT_SCHEMA}.";
        }

        public override string GetQuery(string key)
        {
            return _queryMap[key];
        }
    }
}
