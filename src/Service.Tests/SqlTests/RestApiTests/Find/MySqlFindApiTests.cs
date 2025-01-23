// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests.Find
{
    [TestClass, TestCategory(TestCategory.MYSQL)]
    public class MySqlFindApiTests : FindApiTestBase
    {
        protected static string DEFAULT_SCHEMA = string.Empty;
        protected static Dictionary<string, string> _queryMap = new()
        {
            {
                "FindByIdTest",
                @"
                  SELECT JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id) AS data
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
                  SELECT JSON_OBJECT('categoryid', categoryid, 'pieceid', pieceid, 'instant', instant,
                                        'price', price, 'is_wholesale_price', is_wholesale_price) AS data
                  FROM (
                      SELECT categoryid, pieceid, instant, price, if(is_wholesale_price = 1, cast(TRUE as json), cast(FALSE as json)) AS is_wholesale_price
                      FROM " + _tableWithDateTimePK + @"
                       WHERE categoryid = 2 AND pieceid = 1 AND instant = '2023-08-21 15:11:04'
                      LIMIT 1
                  ) AS subq"
            },
            {
                "FindEmptyTable",
                @"
                    SELECT JSON_OBJECT('id', id) AS data
                    FROM (
                        SELECT *
                        FROM " + _emptyTableTableName + @"
                    ) AS subq"
            },
            {
                "FindEmptyResultSetWithQueryFilter",
                @"
                    SELECT JSON_OBJECT('id', id) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        WHERE 1 != 1
                    ) AS subq"
            },
            {
                "FindOnTableWithUniqueCharacters",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('┬─┬ノ( º _ ºノ)', NoteNum,
                  '始計', DetailAssessmentAndPlanning, '作戰', WagingWar,
                  '謀攻', StrategicAttack)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationUniqueCharactersTable + @"
                  ) AS subq"
            },
            {
                "FindViewAll",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                  FROM (
                      SELECT *
                      FROM " + _simple_all_books + @"
                      ORDER BY id asc
                  ) AS subq"
            },
            {
                "FindViewWithKeyAndMapping",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('book_id', id)) AS data
                  FROM (
                      SELECT id
                      FROM " + _book_view_with_key_and_mapping + @"
                      ORDER BY id
                      LIMIT 100
                  ) AS subq"
            },
            {
                "FindViewSelected",
                @"
                    SELECT JSON_OBJECT('categoryid', categoryid, 'pieceid', pieceid, 'categoryName', categoryName,
                                        'piecesAvailable',piecesAvailable) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable
                        FROM " + _simple_subset_stocks + @"
                        WHERE categoryid = 2 AND pieceid = 1
                    ) AS subq
                "
            },
            {
                "FindBooksPubViewComposite",
                @"
                  SELECT JSON_OBJECT('id', id, 'title', title, 'name', name, 'pub_id', pub_id) AS data
                  FROM (
                      SELECT *
                      FROM " + _composite_subset_bookPub + @"
                      WHERE id = 2 AND title = 'Also Awesome book' AND 
                      pub_id = 1234 AND name = 'Big Company' 
                      ORDER BY id asc
                      LIMIT 1
                  ) AS subq"
            },
            {
                "FindTestWithFilterQueryOneGeFilterOnView",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                  FROM (
                      SELECT *
                      FROM " + _simple_all_books + @"
                      WHERE id >= 4
                      ORDER BY id asc
                  ) AS subq"
            },
            {
                "FindByIdTestWithSelectFieldsOnView",
                @"
                  SELECT JSON_OBJECT('id', id, 'title', title) AS data
                  FROM (
                      SELECT *
                      FROM " + _simple_all_books + @"
                      WHERE id = 1
                      ORDER BY id asc
                      LIMIT 1
                  ) AS subq"
            },
            {
                "FindTestWithFilterQueryStringOneEqFilterOnView",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('categoryid', categoryid, 'pieceid', pieceid,
                                        'categoryName', categoryName, 'piecesAvailable',piecesAvailable)) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable
                        FROM " + _simple_subset_stocks + @"
                        WHERE pieceid = 1
                    ) AS subq"
            },
            {
                "FindTestWithFilterQueryOneNotFilterOnView",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('categoryid', categoryid, 'pieceid', pieceid,
                                        'categoryName', categoryName, 'piecesAvailable',piecesAvailable)) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable
                        FROM " + _simple_subset_stocks + @"
                        WHERE NOT(categoryid > 1)
                    ) AS subq"
            },
            {
                "FindTestWithFilterQueryOneLtFilterOnView",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'pub_id', pub_id, 'name', name)) AS data
                  FROM (
                      SELECT *
                      FROM " + _composite_subset_bookPub + @"
                      WHERE id < 5
                      ORDER BY id asc
                  ) AS subq"
            },
            {
                "FindTestWithQueryStringOneField",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('id', `subq1`.`id`)) AS `data`
                    FROM (
                        SELECT `table0`.`id` AS `id`
                        FROM `" + _integrationTableName + @"` AS `table0`
                        ORDER BY `table0`.`id` asc
                        LIMIT 100
                        ) AS `subq1`"
            },
            {
                "FindTestWithQueryStringAllFields",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      ORDER BY id asc
                      LIMIT 100
                  ) AS subq"
            },
            {
                "FindByIdTestWithQueryStringFields",
                @"
                    SELECT JSON_OBJECT('id', id, 'title', title) AS data
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
                "FindTestWithFilterQueryStringOneEqFilter",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        WHERE id = 1
                        ORDER BY id asc
                    ) AS subq
                "
            },
            {
                "FindTestWithFilterQueryStringValueFirstOneEqFilter",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        WHERE id = 2
                        ORDER BY id asc
                    ) AS subq
                "
            },
            {
                "FindTestWithFilterQueryOneGtFilter",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        WHERE id > 3
                        ORDER BY id asc
                    ) AS subq
                "
            },
            {
                "FindTestWithFilterQueryOneGeFilter",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        WHERE id >= 4
                        ORDER BY id asc
                    ) AS subq
                "
            },
            {
                "FindTestWithFilterQueryOneLtFilter",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        WHERE id < 5
                        ORDER BY id asc
                    ) AS subq
                "
            },
            {
                "FindTestWithFilterQueryOneLeFilter",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        WHERE id <= 4
                        ORDER BY id asc
                    ) AS subq
                "
            },
            {
                "FindTestWithFilterQueryOneNeFilter",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        WHERE id != 3
                        ORDER BY id asc
                    ) AS subq
                "
            },
            {
                "FindTestWithFilterQueryOneNotFilter",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        WHERE not (id < 2)
                        ORDER BY id asc
                    ) AS subq
                "
            },
            {
                "FindTestWithFilterQueryOneRightNullEqFilter",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        WHERE NOT (title IS NULL)
                        ORDER BY id asc
                    ) AS subq
                "
            },
            {
                "FindTestWithFilterQueryOneLeftNullNeFilter",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        WHERE title IS NOT NULL
                        ORDER BY id asc
                    ) AS subq
                "
            },
            {
                "FindTestWithFilterQueryStringSingleAndFilter",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        WHERE id < 3 AND id > 1
                        ORDER BY id asc
                    ) AS subq
                "
            },
            {
                "FindTestWithFilterQueryStringSingleOrFilter",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        WHERE id < 3 OR id > 4
                        ORDER BY id asc
                    ) AS subq
                "
            },
            {
                "FindTestWithFilterQueryStringMultipleAndFilters",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        WHERE id < 4 AND id > 1 AND title != 'Awesome book'
                        ORDER BY id asc
                    ) AS subq
                "
            },
            {
                "FindTestWithFilterQueryStringMultipleOrFilters",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        WHERE id = 1 OR id = 2 OR id = 3
                        ORDER BY id asc
                    ) AS subq
                "
            },
            {
                "FindTestWithFilterQueryStringMultipleAndOrFilters",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        WHERE (id > 2 AND id < 4) OR (title = 'Awesome book')
                        ORDER BY id asc
                    ) AS subq
                "
            },
            {
                "FindTestWithFilterQueryStringMultipleNotAndOrFilters",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        WHERE (NOT (id < 3) OR (id < 4) or NOT (title = 'Awesome book'))
                        ORDER BY id asc
                    ) AS subq
                "
            },
            {
                "FindTestWithFilterQueryStringBoolResultFilter",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        WHERE id = (publisher_id > 1)
                        ORDER BY id asc
                    ) AS subq
                "
            },
            {
                "FindTestWithQueryStringMultipleFields",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title)) AS data
                    FROM (
                        SELECT id, title
                        FROM " + _integrationTableName + @"
                        ORDER BY id asc
                        LIMIT 100
                    ) AS subq
                "
            },
            {
                "FindTestWithPrimaryKeyContainingForeignKey",
                @"
                    SELECT JSON_OBJECT('id', id, 'content', content) AS data
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
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      ORDER BY id asc
                      LIMIT 1
                  ) AS subq"
            },
            {
                "FindTest_NoQueryParams_PaginationNextLink",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'bkname', bkname)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationPaginationTableName + @"
                      ORDER BY id asc
                      LIMIT 100
                  ) AS subq"
            },
            {
                "FindTest_Negative1QueryParams_Pagination",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'bkname', bkname)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationPaginationTableName + @"
                      ORDER BY id asc
                      LIMIT 100000
                  ) AS subq"
            },
            {
                "FindTest_OrderByNotFirstQueryParam_PaginationNextLink",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationPaginationTableName + @"
                      ORDER BY id asc
                      LIMIT 100
                  ) AS subq"
            },
            {
                "FindMany_MappedColumn_NoOrderByQueryParameter",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('bkid', id, 'name', bkname)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationMappedPaginationTableName + @"
                      ORDER BY id asc
                      LIMIT 100
                  ) AS subq"
            },
            {
                "FindTestWithFirstMultiKeyPagination",
                @"
                  SELECT JSON_OBJECT('id', id, 'content', content, 'book_id', book_id) AS data
                  FROM (
                      SELECT *
                      FROM " + _tableWithCompositePrimaryKey + @"
                      ORDER BY book_id asc, id asc
                      LIMIT 1
                  ) AS subq"
            },
            {
                "FindTestWithAfterSingleKeyPagination",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      WHERE id > 7
                      ORDER BY id asc
                      LIMIT 100
                  ) AS subq"
            },
            {
                "FindTestWithAfterMultiKeyPagination",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'content', content, 'book_id', book_id)) AS data
                  FROM (
                      SELECT *
                      FROM " + _tableWithCompositePrimaryKey + @"
                      WHERE book_id > 1 OR (book_id = 1 AND id > 567)
                      ORDER BY book_id asc, id asc
                      LIMIT 100
                  ) AS subq"
            },
            {
                "FindTestWithPaginationVerifSinglePrimaryKeyInAfter",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      ORDER BY id asc
                      LIMIT 1
                  ) AS subq"
            },
            {
                "FindTestWithPaginationVerifMultiplePrimaryKeysInAfter",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'content', content, 'book_id', book_id)) AS data
                  FROM (
                      SELECT *
                      FROM " + _tableWithCompositePrimaryKey + @"
                      ORDER BY book_id asc, id asc
                      LIMIT 1
                  ) AS subq"
            },
            {
                "FindTestWithIntTypeNullValuesOrderByAsc",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('typeid', id, 'int_types', int_types)) AS data
                  FROM (
                      SELECT id, int_types
                      FROM " + _integrationTypeTable + @"
                      ORDER BY int_types asc, id asc
                      LIMIT 100
                  ) AS subq"
            },
            {
                "FindTestWithQueryStringAllFieldsOrderByAsc",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      ORDER BY title asc, id asc
                      LIMIT 100
                  ) AS subq"
            },
            {
                "FindTestWithQueryStringAllFieldsMappedEntityOrderByAsc",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('treeId', treeId, 'fancyName', species, 'region', region, 'height', height)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationMappingTable + @"
                      ORDER BY species asc
                      LIMIT 100
                  ) AS subq"
            },
            {
                "FindTestWithQueryStringSpaceInNamesOrderByAsc",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('ID Number', `ID Number`, 'First Name', `First Name`, 'Last Name', `Last Name`)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableHasColumnWithSpace + @"
                      ORDER BY `ID Number` asc
                      LIMIT 100
                  ) AS subq"
            },
            {
                "FindTestWithFirstAndSpacedColumnOrderBy",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('ID Number', `ID Number`, 'First Name', `First Name`, 'Last Name', `Last Name`)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableHasColumnWithSpace + @"
                      ORDER BY `Last Name` asc
                      LIMIT 1
                  ) AS subq"
            },
            {
                "FindTestWithQueryStringAllFieldsOrderByDesc",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      ORDER BY publisher_id desc, id asc
                      LIMIT 100
                  ) AS subq"
            },
            {
                "FindTestWithFirstSingleKeyPaginationAndOrderBy",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      ORDER BY id asc, title asc
                      LIMIT 1
                  ) AS subq"
            },
            {
                "FindTestVerifyMaintainColumnOrderForOrderBy",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                  FROM (
                      SELECT id, title, publisher_id
                      FROM " + _integrationTableName + @"
                      ORDER BY id desc, publisher_id asc
                      LIMIT 100
                  ) AS subq"
            },
            {
                "FindTestVerifyMaintainColumnOrderForOrderByInReverse",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                  FROM (
                      SELECT id, title, publisher_id
                      FROM " + _integrationTableName + @"
                      ORDER BY publisher_id asc, id desc
                      LIMIT 100
                  ) AS subq"
            },
            {
                "FindTestWithFirstSingleKeyIncludedInOrderByAndPagination",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      ORDER BY id asc
                      LIMIT 1
                  ) AS subq"
            },
            {
                "FindTestWithFirstTwoOrderByAndPagination",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      ORDER BY id asc
                      LIMIT 2
                  ) AS subq"
            },
            {
                "FindTestWithFirstTwoVerifyAfterFormedCorrectlyWithOrderBy",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'name', name, 'birthdate', birthdate)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTieBreakTable + @"
                      ORDER BY birthdate asc, name asc, id desc
                      LIMIT 2
                  ) AS subq"
            },
            {
                "FindTestWithFirstTwoVerifyAfterBreaksTieCorrectlyWithOrderBy",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'name', name, 'birthdate', birthdate)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTieBreakTable + @"
                      WHERE ((birthdate > '2001-01-01') OR(birthdate = '2001-01-01' AND name > 'Aniruddh') OR
                      (birthdate = '2001-01-01' AND name = 'Aniruddh' AND id > 125))
                      ORDER BY birthdate asc, name asc, id asc
                      LIMIT 2
                  ) AS subq"
            },
            {
                "FindTestWithFirstMultiKeyIncludeAllInOrderByAndPagination",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'content', content, 'book_id', book_id)) AS data
                  FROM (
                      SELECT *
                      FROM " + _tableWithCompositePrimaryKey + @"
                      ORDER BY id desc, book_id asc
                      LIMIT 1
                  ) AS subq"
            },
            {
                "FindTestWithFirstMultiKeyIncludeOneInOrderByAndPagination",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'content', content, 'book_id', book_id)) AS data
                  FROM (
                      SELECT *
                      FROM " + _tableWithCompositePrimaryKey + @"
                      ORDER BY book_id asc, id asc
                      LIMIT 1
                  ) AS subq"
            },
            {
                "FindTestWithFirstAndMultiColumnOrderBy",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      ORDER BY publisher_id desc, title desc
                      LIMIT 1
                  ) AS subq"
            },
            {
                "FindTestWithFirstAndTiedColumnOrderBy",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      ORDER BY publisher_id desc, id asc
                      LIMIT 1
                  ) AS subq"
            },
            {
                "FindTestWithFirstMultiKeyPaginationAndOrderBy",
                @"
                  SELECT JSON_OBJECT('id', id, 'content', content, 'book_id', book_id) AS data
                  FROM (
                      SELECT *
                      FROM " + _tableWithCompositePrimaryKey + @"
                      ORDER BY content desc, book_id asc, id asc
                      LIMIT 1
                  ) AS subq"
            },
            {
                "FindTestWithMappedFieldsToBeReturned",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('treeId', treeId, 'Scientific Name', species, 'United State\'s Region', region, 'height', height)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationMappingTable + @"
                  ) AS subq"
            },
            {
                "FindTestWithSingleMappedFieldsToBeReturned",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('Scientific Name', species)) AS data
                  FROM (
                      SELECT species
                      FROM " + _integrationMappingTable + @"
                  ) AS subq"
            },
            {
                "FindTestWithUnMappedFieldsToBeReturned",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('treeId', treeId)) AS data
                  FROM (
                      SELECT treeId
                      FROM " + _integrationMappingTable + @"
                  ) AS subq"
            },
            {
                "FindTestWithDifferentMappedFieldsAndFilter",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('treeId', treeId, 'fancyName', species, 'region', region, 'height', height)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationMappingTable + @"
                      WHERE species = 'Tsuga terophylla'
                  ) AS subq"
            },
            {
                "FindTestWithDifferentMappedFieldsAndOrderBy",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('treeId', treeId, 'fancyName', species, 'region', region, 'height', height)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationMappingTable + @"
                      ORDER BY species asc
                      LIMIT 100
                  ) AS subq"
            },
            {
                "FindTestWithDifferentMappingFirstSingleKeyPaginationAndOrderBy",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('treeId', treeId, 'fancyName', species, 'region', region, 'height', height)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationMappingTable + @"
                      ORDER BY species asc
                      LIMIT 1
                  ) AS subq"
            },
            {
                "FindTestWithDifferentMappingAfterSingleKeyPaginationAndOrderBy",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('treeId', treeId, 'fancyName', species, 'region', region, 'height', height)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationMappingTable + @"
                      WHERE species > 'Pseudotsuga menziesii'
                      ORDER BY species asc, treeId asc
                      LIMIT 101
                  ) AS subq"
            },
            {
                "FindManyTestWithDatabasePolicy",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'name', name)) AS data
                  FROM (
                      SELECT *
                      FROM " + _foreignKeyTableName + @"
                      WHERE id != 1234 or id > 1940
                      ORDER BY id asc
                      LIMIT 101
                  ) AS subq"
            },
            {
                "FindInAccessibleRowWithDatabasePolicy",
                @"
                  SELECT JSON_OBJECT('id', id, 'name', name) AS data
                  FROM (
                      SELECT *
                      FROM " + _foreignKeyTableName + @"
                      WHERE id = 1234 and (id != 1234 or id > 1940)
                      ORDER BY id asc
                      LIMIT 101
                  ) AS subq"
            },
            {
                "FindByIdTestWithSelectFieldsOnViewWithoutKeyFields",
                @"
                  SELECT JSON_OBJECT('title', title) AS data
                  FROM (
                      SELECT title
                      FROM " + _simple_all_books + @"
                      WHERE id = 1
                  ) AS subq
                "
            },
            {
                "FindTestWithSelectFieldsWithoutKeyFieldsOnView",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('title', title)) AS data
                  FROM (
                      SELECT title
                      FROM " + _simple_all_books + @"
                      ORDER BY id asc
                  ) AS subq
                "
            },
            {
                "FindByIdTestWithSelectFieldsWithSomeKeyFieldsOnViewWithMultipleKeyFields",
                 @"
                    SELECT JSON_OBJECT('categoryid', categoryid, 'categoryName', categoryName) AS data
                    FROM (
                        SELECT categoryid, categoryName
                        FROM " + _simple_subset_stocks + @"
                        WHERE categoryid = 1 AND pieceid = 1
                    ) AS subq
                 "
            },
            {
                "FindTestWithSelectFieldsWithSomeKeyFieldsOnViewWithMultipleKeyFields",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('categoryid', categoryid, 'categoryName', categoryName)) AS data
                    FROM (
                        SELECT categoryid, categoryName, ROW_NUMBER() OVER(ORDER BY categoryid asc, pieceid asc)
                        FROM " + _simple_subset_stocks + @"    
                    ) AS subq
                "
            },
            {
                "FindByIdTestWithSelectFieldsWithoutKeyFieldsOnViewWithMultipleKeyFields",
                @"
                    SELECT JSON_OBJECT('categoryName', categoryName) AS data
                    FROM (
                        SELECT categoryName
                        FROM " + _simple_subset_stocks + @"
                        WHERE categoryid = 1 AND pieceid = 1
                    ) AS subq
                "
            },
            {
                "FindTestWithSelectFieldsWithoutKeyFieldsOnViewWithMultipleKeyFields",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('categoryName', categoryName)) AS data
                    FROM (
                        SELECT categoryName, ROW_NUMBER() OVER(ORDER BY categoryid asc, pieceid asc)
                        FROM " + _simple_subset_stocks + @"
                    ) AS subq
                "
            },
            {
                "FindByIdWithSelectFieldsWithoutPKOnTable",
                @"
                  SELECT JSON_OBJECT('title', title) AS data
                  FROM (
                      SELECT title FROM " + _integrationTableName + @"
                      WHERE id = 1
                  ) AS subq
                "
            },
            {
                "FindWithSelectFieldsWithoutPKOnTable",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('title', title)) AS data
                  FROM (
                      SELECT title FROM " + _integrationTableName + @"
                  ) AS subq
                "
            },
            {
                "FindByIdWithSelectFieldsWithSomePKOnTableWithCompositePK",
                @"
                    SELECT JSON_OBJECT('categoryid', categoryid, 'categoryName', categoryName) AS data
                    FROM (
                        SELECT categoryid, categoryName
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 1 AND pieceid = 1
                    ) AS subq
                "
            },
            {
                "FindWithSelectFieldsWithSomePKOnTableWithCompositePK",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('categoryid', categoryid, 'categoryName', categoryName)) AS data
                    FROM (
                        SELECT categoryid, categoryName, ROW_NUMBER() OVER (order by categoryid, pieceid)
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                    ) AS subq
                "
            },
            {
                "FindByIdWithSelectFieldsWithoutPKOnTableWithCompositePK",
                @"
                    SELECT JSON_OBJECT('categoryName', categoryName) AS data
                    FROM (
                        SELECT categoryName
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 1 AND pieceid = 1     
                    ) AS subq
                "
            },
            {
                "FindWithSelectFieldsWithoutPKOnTableWithCompositePK",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('categoryName', categoryName)) AS data
                    FROM (
                        SELECT categoryName, ROW_NUMBER() OVER (order by categoryid, pieceid)
                        FROM " + _Composite_NonAutoGenPK_TableName + @"  
                    ) AS subq
                "
            },
            {
                "FindWithSelectAndOrderbyQueryStringsOnViews",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('categoryid', categoryid, 'categoryName', categoryName)) AS data
                    FROM (
                        SELECT categoryid, categoryName, ROW_NUMBER() OVER (ORDER BY piecesAvailable asc, categoryid asc, pieceid asc)
                        FROM " + _simple_subset_stocks + @"     
                    ) AS subq
                "
            },
            {
                "FindWithSelectAndOrderbyQueryStringsOnTables",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title)) AS data
                    FROM (
                        SELECT id, title, ROW_NUMBER() OVER (ORDER BY publisher_id asc, id asc)" + @"
                        FROM " + _integrationTableName + @" 
                    ) AS subq
                "
            },
            {
                "FindTestFilterForVarcharColumnWithNullAndNonNullValues",
                @"
                    SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id', id, 'journalname', journalname, 'color', color, 'ownername', ownername)), JSON_ARRAY()) AS data
                    FROM (
                        SELECT *" + @"
                        FROM " + _tableWithVarcharMax + @"
                        WHERE color IS NULL AND ownername = 'Abhishek'
                    ) AS subq
                "
            },
            {
                "FindTestFilterForVarcharColumnWithNotMaximumSize",
                @"
                    SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('speciesid', speciesid, 'region', region, 'habitat', habitat)), JSON_ARRAY()) AS data
                    FROM (
                        SELECT *" + @"
                        FROM " + _integrationBrokenMappingTable + @"
                        WHERE habitat = 'sand'
                    ) AS subq
                "
            },
            {
                "FindTestFilterForVarcharColumnWithNotMaximumSizeAndNoTruncation",
                @"
                    SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('speciesid', speciesid, 'region', region, 'habitat', habitat)), JSON_ARRAY()) AS data
                    FROM (
                        SELECT *" + @"
                        FROM " + _integrationBrokenMappingTable + @"
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
            DatabaseEngine = TestCategory.MYSQL;
            await InitializeTestFixture();
        }

        /// <summary>
        /// Runs after every test to reset the database state
        /// </summary>
        [TestCleanup]
        public async Task TestCleanup()
        {
            await ResetDbStateAsync();
        }

        #endregion

        public override string GetDefaultSchema()
        {
            return string.Empty;
        }

        /// <summary>
        /// MySql does not have a schema so it lacks
        /// the '.' between schema and table, we
        /// return empty string here for this reason.
        /// </summary>
        /// <exception cref="NotImplementedException"></exception>
        public override string GetDefaultSchemaForEdmModel()
        {
            return string.Empty;
        }

        public override string GetQuery(string key)
        {
            return _queryMap[key];
        }

        /// <summary>
        /// MySql does not have a schema and so this test
        /// which validates we de-conflict the same table
        /// name in different schemas is not valid.
        /// </summary>
        /// <exception cref="NotImplementedException"></exception>
        [TestMethod]
        [Ignore]
        public override Task FindOnTableWithNamingCollision()
        {
            throw new NotImplementedException();
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
    }
}
