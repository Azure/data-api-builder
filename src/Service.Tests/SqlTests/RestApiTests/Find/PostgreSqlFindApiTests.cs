// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
                      ORDER BY id asc
                      LIMIT 1
                  ) AS subq"
            },
            {
                "FindByDateTimePKTest",
                @"
                  SELECT to_jsonb(subq) AS data
                  FROM (
                      SELECT *
                      FROM " + _tableWithDateTimePK + @"
                      WHERE categoryid = 2 AND pieceid = 1 AND instant = '2023-08-21 15:11:04'
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
                "FindViewAll",
                @"
                    SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT * FROM " + _simple_all_books + @"
                        ORDER BY id
                    ) AS subq
                "
            },
            {
                "FindViewWithKeyAndMapping",
                @"
                    SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT id as book_id FROM " + _book_view_with_key_and_mapping + @"
                        ORDER BY id
                        LIMIT 100
                    ) AS subq
                "
            },
            {
                "FindViewSelected",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable""
                        FROM " + _simple_subset_stocks + @"
                        WHERE categoryid = 2 AND pieceid = 1
                        ORDER BY categoryid, pieceid
                        LIMIT 1
                    ) AS subq
                "
            },
            {
                "FindBooksPubViewComposite",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT name, id, title, pub_id FROM " + _composite_subset_bookPub + @"
                        WHERE id = 2 AND pub_id = 1234
                        ORDER BY id, pub_id
                        LIMIT 1
                    ) AS subq
                "
            },
            {
                "FindTestWithFilterQueryOneGeFilterOnView",
                @"
                    SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT * FROM " + _simple_all_books + @"
                        WHERE id >= 4
                        ORDER BY id
                    ) AS subq
                "
            },
            {
                "FindByIdTestWithSelectFieldsOnView",
                @"
                    SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT id, title FROM " + _simple_all_books + @"
                        WHERE id = 1
                        ORDER BY id
                    ) AS subq
                "
            },
            {
                "FindTestWithFilterQueryStringOneEqFilterOnView",
                @"
                    SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable""
                        FROM " + _simple_subset_stocks + @"
                        WHERE pieceid = 1
                        ORDER BY categoryid, pieceid
                    ) AS subq
                "
            },
            {
                "FindTestWithFilterQueryOneNotFilterOnView",
                @"
                    SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable""
                        FROM " + _simple_subset_stocks + @"
                        WHERE NOT categoryid > 1
                        ORDER BY categoryid, pieceid
                    ) AS subq
                "
            },
            {
                "FindTestWithFilterQueryOneLtFilterOnView",
                @"
                    SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT name, id, pub_id, title FROM " + _composite_subset_bookPub + @"
                        WHERE id < 5
                        ORDER BY id
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
                "FindOnTableWithNamingCollision",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT upc, comic_name, issue
                        FROM " + _collisionTable + @"
                        WHERE 1 = 1
                    ) AS subq"
            },
            {
                "FindTestWithQueryStringOneField",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT id
                      FROM " + _integrationTableName + @"
                      ORDER BY id asc
                  ) AS subq"
            },
            {
                "FindTestWithQueryStringAllFields",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      ORDER BY id asc
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
                        ORDER BY id asc
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
                        ORDER BY id asc
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
                      ORDER BY id asc
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
                      ORDER BY id asc
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
                      ORDER BY id asc
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
                      ORDER BY id asc
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
                      ORDER BY id asc
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
                      ORDER BY id asc
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
                      ORDER BY id asc
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
                      ORDER BY id asc
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
                      ORDER BY id asc
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
                      ORDER BY id asc
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
                      ORDER BY id asc
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
                      ORDER BY id asc
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
                      ORDER BY id asc
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
                      ORDER BY id asc
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
                      ORDER BY id asc
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
                      ORDER BY id asc
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
                      ORDER BY id asc
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
                        ORDER BY id asc
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
                        ORDER BY id asc
                        LIMIT 1
                    ) AS subq
                "
            },
            {
                "FindTest_NoQueryParams_PaginationNextLink",
                @"
                    SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationPaginationTableName + @"
                        ORDER BY id asc
                        LIMIT 100
                    ) AS subq
                "
            },
            {
                "FindTest_Negative1QueryParams_Pagination",
                @"
                    SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationPaginationTableName + @"
                        ORDER BY id asc
                        LIMIT 100000
                    ) AS subq
                "
            },
            {
                "FindTest_OrderByNotFirstQueryParam_PaginationNextLink",
                @"
                    SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT id
                        FROM " + _integrationPaginationTableName + @"
                        ORDER BY id asc
                        LIMIT 100
                    ) AS subq
                "
            },
            {
                "FindMany_MappedColumn_NoOrderByQueryParameter",
                @"
                    SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT id AS bkid, bkname AS name
                        FROM " + _integrationMappedPaginationTableName + @"
                        ORDER BY id asc
                        LIMIT 100
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
                        ORDER BY book_id asc, id asc
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
                        ORDER BY id asc
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
                        ORDER BY book_id asc, id asc
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
                        ORDER BY id asc
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
                        ORDER BY book_id asc, id asc
                        LIMIT 1
                    ) AS subq
                "
            },
            {
                "FindTestWithIntTypeNullValuesOrderByAsc",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT id as typeid, int_types
                      FROM " + _integrationTypeTable + @"
                      ORDER BY int_types asc, id asc
                  ) AS subq"
            },
            {
                "FindTestWithQueryStringAllFieldsOrderByAsc",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      ORDER BY title asc, id asc
                  ) AS subq"
            },
            {
                "FindTestWithQueryStringAllFieldsMappedEntityOrderByAsc",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                       SELECT  ""treeId"", ""species"" AS ""fancyName"", ""region"", ""height""
                        FROM " + _integrationMappingTable + @"
                      ORDER BY ""species"" asc
                  ) AS subq"
            },
            {
                "FindTestWithFirstAndSpacedColumnOrderBy",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableHasColumnWithSpace + @"
                      ORDER BY ""Last Name"" asc
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
                      ORDER BY ""ID Number"" asc
                  ) AS subq"
            },
            {
                "FindTestWithQueryStringAllFieldsOrderByDesc",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      ORDER BY publisher_id desc, id asc
                  ) AS subq"
            },
            {
                "FindTestWithFirstSingleKeyPaginationAndOrderBy",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        ORDER BY title asc, id asc
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
                        ORDER BY id desc, publisher_id asc
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
                        ORDER BY publisher_id asc, id desc
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
                        ORDER BY id asc
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
                        ORDER BY id asc
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
                        ORDER BY birthdate asc, name asc, id desc
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
                        ORDER BY birthdate asc, name asc, id asc
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
                        ORDER BY id desc, book_id asc
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
                        ORDER BY book_id asc, id asc
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
                        ORDER BY content desc, book_id asc, id asc
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
                        SELECT ""species"" AS ""Scientific Name""
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
                        ORDER BY species asc
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
                        ORDER BY species asc
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
                        ORDER BY species asc, ""treeId"" asc
                        LIMIT 101
                    ) AS subq
                "
            },
            {
                "FindManyTestWithDatabasePolicy",
                @"
                    SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT  ""id"", ""name""
                        FROM " + _foreignKeyTableName + @"
                        WHERE ""id"" != 1234 or ""id"" > 1940
                        ORDER BY ""id"" asc
                        LIMIT 101
                    ) AS subq
                "
            },
            {
                "FindInAccessibleRowWithDatabasePolicy",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT  ""id"", ""name""
                        FROM " + _foreignKeyTableName + @"
                        WHERE ""id"" = 1234 and (""id"" != 1234 or ""id"" > 1940)
                        ORDER BY ""id"" asc
                        LIMIT 101
                    ) AS subq
                "
            },
            {
                "FindByIdTestWithSelectFieldsOnViewWithoutKeyFields",
                @"
                    SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT title FROM " + _simple_all_books + @"
                        WHERE id = 1
                    ) AS subq
                "
            },
            {
                "FindTestWithSelectFieldsWithoutKeyFieldsOnView",
                @"
                    SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT title FROM " + _simple_all_books + @"
                        ORDER BY id
                    ) AS subq
                "
            },
            {
                "FindByIdTestWithSelectFieldsWithSomeKeyFieldsOnViewWithMultipleKeyFields",
                @"
                    SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT categoryid, ""categoryName"" FROM " + _simple_subset_stocks + @"
                        WHERE categoryid = 1 AND pieceid = 1
                    ) AS subq
                "
            },
            {
                "FindTestWithSelectFieldsWithSomeKeyFieldsOnViewWithMultipleKeyFields",
                @"
                    SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT categoryid, ""categoryName"" FROM " + _simple_subset_stocks + @"
                        ORDER BY categoryid, pieceid
                    ) AS subq
                "
            },
            {
                "FindByIdTestWithSelectFieldsWithoutKeyFieldsOnViewWithMultipleKeyFields",
                @"
                    SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT ""categoryName"" FROM " + _simple_subset_stocks + @"
                        WHERE categoryid = 1 AND pieceid = 1
                    ) AS subq
                "
            },
            {
                "FindTestWithSelectFieldsWithoutKeyFieldsOnViewWithMultipleKeyFields",
                @"
                    SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT ""categoryName"" FROM " + _simple_subset_stocks + @"
                        ORDER BY categoryid, pieceid
                    ) AS subq
                "
            },
            {
                "FindByIdWithSelectFieldsWithoutPKOnTable",
                @"
                    SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT title FROM " + _integrationTableName + @"
                        WHERE id = 1
                    ) AS subq
                "
            },
            {
                "FindWithSelectFieldsWithoutPKOnTable",
                @"
                    SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT title FROM " + _integrationTableName + @"
                        ORDER BY id
                    ) AS subq
                "
            },
            {
                "FindByIdWithSelectFieldsWithSomePKOnTableWithCompositePK",
                @"
                    SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT categoryid, ""categoryName"" FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 1 AND pieceid = 1
                    ) AS subq
                "
            },
            {
                "FindWithSelectFieldsWithSomePKOnTableWithCompositePK",
                @"
                    SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT categoryid, ""categoryName"" FROM " + _Composite_NonAutoGenPK_TableName + @"
                        ORDER BY categoryid, pieceid
                    ) AS subq
                "
            },
            {
                "FindByIdWithSelectFieldsWithoutPKOnTableWithCompositePK",
                @"
                    SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT ""categoryName"" FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 1 AND pieceid = 1
                    ) AS subq
                "
            },
            {
                "FindWithSelectFieldsWithoutPKOnTableWithCompositePK",
                @"
                    SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT ""categoryName"" FROM " + _Composite_NonAutoGenPK_TableName + @"        
                        ORDER BY categoryid, pieceid
                    ) AS subq
                "
            },
            {
                "FindWithSelectAndOrderbyQueryStringsOnViews",
                 @"
                    SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT categoryid, ""categoryName"" FROM " + _simple_subset_stocks + @"
                        ORDER BY ""piecesAvailable"", categoryid, pieceid
                    ) AS subq
                "
            },
            {
                "FindWithSelectAndOrderbyQueryStringsOnTables",
                @"
                    SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT id, title FROM " + _simple_all_books + @"
                        ORDER BY publisher_id, id
                    ) AS subq
                "
            },
            {
                "FindTestFilterForVarcharColumnWithNullAndNonNullValues",
                @"
                    SELECT COALESCE( json_agg(to_jsonb(subq)), '[]') AS data
                    FROM (
                        SELECT * FROM " + _tableWithVarcharMax + @"
                        WHERE color IS NULL AND ownername = 'Abhishek'
                    ) AS subq
                "
            },
            {
                "FindTestFilterForVarcharColumnWithNotMaximumSize",
                @"
                    SELECT COALESCE( json_agg(to_jsonb(subq)), '[]') AS data
                    FROM (
                        SELECT * FROM " + _integrationBrokenMappingTable + @"
                        WHERE habitat = 'sand'
                    ) AS subq
                "
            },
            {
                "FindTestFilterForVarcharColumnWithNotMaximumSizeAndNoTruncation",
                @"
                    SELECT COALESCE( json_agg(to_jsonb(subq)), '[]') AS data
                    FROM (
                        SELECT * FROM " + _integrationBrokenMappingTable + @"
                        WHERE habitat = 'forestland'
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
            await InitializeTestFixture();
        }

        #endregion

        [TestCleanup]
        public async Task TestCleanup()
        {
            await ResetDbStateAsync();
        }

        // Pending Stored Procedure Support
        [TestMethod]
        [Ignore]
        public override Task FindManyStoredProcedureTest()
        {
            throw new NotImplementedException();
        }

        [TestMethod]
        [Ignore]
        public override Task FindOneStoredProcedureTestUsingParameter()
        {
            throw new NotImplementedException();
        }

        [TestMethod]
        [Ignore]
        public override Task FindStoredProcedureWithNonEmptyPrimaryKeyRoute()
        {
            throw new NotImplementedException();
        }

        [TestMethod]
        [Ignore]
        public override Task FindStoredProcedureWithMissingParameter()
        {
            throw new NotImplementedException();
        }

        [TestMethod]
        [Ignore]
        public override Task FindStoredProcedureWithNonexistentParameter()
        {
            throw new NotImplementedException();
        }

        [TestMethod]
        [Ignore]
        public override Task FindApiTestForSPWithRequiredParamsInRequestBody()
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
