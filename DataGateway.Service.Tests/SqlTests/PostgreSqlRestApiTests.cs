using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests
{

    [TestClass, TestCategory(TestCategory.POSTGRESQL)]
    public class PostgreSqlRestApiTests : RestApiTestBase
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
                    SELECT to_jsonb(subq) AS data
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
                "InsertOneTest",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @"
                    ) AS subq
                "
            },
            {
                "InsertOneInCompositeNonAutoGenPKTest",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable"", ""piecesRequired""
                        FROM " + _Composite_NonAutoGenPK + @"
                        WHERE categoryid = 5 AND pieceid = 2 AND ""categoryName"" = 'FairyTales'
                            AND ""piecesAvailable"" = 0 AND ""piecesRequired"" = 0
                    ) AS subq
                "
            },
            {
                "InsertOneInCompositeKeyTableTest",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, content, book_id
                        FROM " + _tableWithCompositePrimaryKey + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @"
                        AND book_id = 1
                    ) AS subq
                "
            },
            {
                "InsertOneInDefaultTestTable",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, content, book_id
                        FROM " + _tableWithCompositePrimaryKey + @"
                        WHERE id = " + (STARTING_ID_FOR_TEST_INSERTS + 1) + @" AND book_id = 2
                    ) AS subq
                "
            },
            {
                "DeleteOneTest",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id
                        FROM " + _integrationTableName + @"
                        WHERE id = 5
                    ) AS subq
                "
            },
            {
                "PutOne_Update_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = 7 AND title = 'The Hobbit Returns to The Shire'
                            AND publisher_id = 1234
                    ) AS subq
                "
            },
            {
                "PutOne_Update_IfMatchHeaders_Test_Confirm_Update",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = 1 AND title = 'The Return of the King'
                    ) AS subq
                "
            },
            {
                "PutOne_Update_Default_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, book_id, content
                        FROM " + _tableWithCompositePrimaryKey + @"
                        WHERE id = 568 AND book_id = 1 AND content ='Good book to read'
                            AND publisher_id = 1234
                    ) AS subq
                "
            },
            {
                "PutOne_Update_CompositeNonAutoGenPK_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable"", ""piecesRequired""
                        FROM " + _Composite_NonAutoGenPK + @"
                        WHERE categoryid = 2 AND pieceid = 1 AND ""categoryName"" = 'SciFi'
                            AND ""piecesAvailable"" = 10 AND ""piecesRequired"" = 5
                    ) AS subq
                "
            },
            {
                "PutOne_Update_NullOutMissingField_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable"", ""piecesRequired""
                        FROM " + _Composite_NonAutoGenPK + @"
                        WHERE categoryid = 1 AND pieceid = 1 AND ""categoryName"" = 'SciFi'
                            AND ""piecesAvailable"" is NULL AND ""piecesRequired"" = 5
                    ) AS subq
                "
            },
            {
                "PutOne_Update_Empty_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable"", ""piecesRequired""
                        FROM " + _Composite_NonAutoGenPK + @"
                        WHERE categoryid = 2 AND pieceid = 1 AND ""categoryName"" = ''
                            AND ""piecesAvailable"" = 2 AND ""piecesRequired"" = 3
                    ) AS subq
                "
            },
            {
                "PutOne_Update_Nulled_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable"", ""piecesRequired""
                        FROM " + _Composite_NonAutoGenPK + @"
                        WHERE categoryid = 2 AND pieceid = 1 AND ""categoryName"" = 'FairyTales'
                            AND ""piecesAvailable"" is NULL AND ""piecesRequired"" = 4
                    ) AS subq
                "
            },
            {
                "PutOne_Insert_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, title, issue_number
                        FROM " + "foo." + _integration_NonAutoGenPK_TableName + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @" AND title = 'Batman Returns'
                            AND issue_number = 1234
                    ) AS subq
                "
            },
            {
                "PutOne_Insert_Nullable_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, title, issue_number
                        FROM " + "foo." + _integration_NonAutoGenPK_TableName + @"
                        WHERE id = " + (STARTING_ID_FOR_TEST_INSERTS + 1) + @"
                            AND title = 'Times' AND issue_number is NULL
                    ) AS subq
                "
            },
            {
                "PutOne_Insert_PKAutoGen_Test",
                $"INSERT INTO { _integrationTableName } " +
                $"(id, title, publisher_id)" +
                $"VALUES (1000,'The Hobbit Returns to The Shire',1234)"
            },
            {
                "PutOne_Insert_AutoGenNonPK_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, title, volume, ""categoryName""
                        FROM " + _integration_AutoGenNonPK_TableName + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @"
                            AND title = 'Star Trek' AND volume IS NOT NULL
                    ) AS subq
                "
            },
            {
                "PutOne_Insert_CompositeNonAutoGenPK_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable"", ""piecesRequired""
                        FROM " + _Composite_NonAutoGenPK + @"
                        WHERE categoryid = 3 AND pieceid = 1 AND ""categoryName"" = 'SciFi'
                            AND ""piecesAvailable"" = 2 AND ""piecesRequired"" = 1
                    ) AS subq
                "
            },
            {
                "PutOne_Insert_Default_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable"", ""piecesRequired""
                        FROM " + _Composite_NonAutoGenPK + @"
                        WHERE categoryid = 8 AND pieceid = 1 AND ""categoryName"" = 'SciFi'
                            AND ""piecesAvailable"" = 0 AND ""piecesRequired"" = 0
                    ) AS subq
                "
            },
            {
                "PutOne_Insert_Empty_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable"", ""piecesRequired""
                        FROM " + _Composite_NonAutoGenPK + @"
                        WHERE categoryid = 4 AND pieceid = 1 AND ""categoryName"" = ''
                            AND ""piecesAvailable"" = 2 AND ""piecesRequired"" = 3
                    ) AS subq
                "
            },
            {
                "PutOne_Insert_Nulled_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable"", ""piecesRequired""
                        FROM " + _Composite_NonAutoGenPK + @"
                        WHERE categoryid = 4 AND pieceid = 1 AND ""categoryName"" = 'SciFi'
                            AND ""piecesAvailable"" is NULL AND ""piecesRequired"" = 4
                    ) AS subq
                "
            },
            {
                "PatchOne_Insert_NonAutoGenPK_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, title, issue_number
                        FROM " + "foo." + _integration_NonAutoGenPK_TableName + @"
                        WHERE id = 2 AND title = 'Batman Begins' AND issue_number = 1234
                    ) AS subq
                "
            },
            {
                "PatchOne_Insert_CompositeNonAutoGenPK_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable"", ""piecesRequired""
                        FROM " + _Composite_NonAutoGenPK + @"
                        WHERE categoryid = 4 AND pieceid = 1 AND ""categoryName"" = 'FairyTales'
                            AND ""piecesAvailable"" = 5 AND ""piecesRequired"" = 4
                    ) AS subq
                "
            },
            {
                "PatchOne_Insert_Empty_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable"", ""piecesRequired""
                        FROM " + _Composite_NonAutoGenPK + @"
                        WHERE categoryid = 5 AND pieceid = 1 AND ""categoryName"" = ''
                            AND ""piecesAvailable"" = 5 AND ""piecesRequired"" = 4
                    ) AS subq
                "
            },
            {
                "PatchOne_Insert_Default_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable"", ""piecesRequired""
                        FROM " + _Composite_NonAutoGenPK + @"
                        WHERE categoryid = 7 AND pieceid = 1 AND ""categoryName"" = 'SciFi'
                            AND ""piecesAvailable"" = 0 AND ""piecesRequired"" = 0
                    ) AS subq
                "
            },
            {
                "PatchOne_Insert_Nulled_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable"", ""piecesRequired""
                        FROM " + _Composite_NonAutoGenPK + @"
                        WHERE categoryid = 3 AND pieceid = 1 AND ""categoryName"" = 'SciFi'
                            AND ""piecesAvailable"" is NULL AND ""piecesRequired"" = 4
                    ) AS subq
                "
            },
            {
                "PatchOne_Update_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = 8 AND title = 'Heart of Darkness' AND publisher_id = 2324
                    ) AS subq
                "
            },
            {
                "PatchOne_Update_IfMatchHeaders_Test_Confirm_Update",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = 1 AND title = 'The Hobbit Returns to The Shire'
                    ) AS subq
                "
            },
            {
                "PatchOne_Update_Default_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, book_id, content
                        FROM " + _tableWithCompositePrimaryKey + @"
                        WHERE id = 567 AND book_id = 1 AND content = 'That's a great book'
                    ) AS subq
                "
            },
            {
                "PatchOne_Update_CompositeNonAutoGenPK_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable"", ""piecesRequired""
                        FROM " + _Composite_NonAutoGenPK + @"
                        WHERE categoryid = 1 AND pieceid = 1 AND ""categoryName"" = 'SciFi'
                            AND ""piecesAvailable"" = 10 AND ""piecesRequired"" = 0
                    ) AS subq
                "
            },
            {
                "PatchOne_Update_Empty_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable"", ""piecesRequired""
                        FROM " + _Composite_NonAutoGenPK + @"
                        WHERE categoryid = 1 AND pieceid = 1 AND ""categoryName"" = ''
                            AND ""piecesAvailable"" = 10 AND ""piecesRequired"" = 0
                    ) AS subq
                "
            },
            {
                "PatchOne_Update_Nulled_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable"", ""piecesRequired""
                        FROM " + _Composite_NonAutoGenPK + @"
                        WHERE categoryid = 1 AND pieceid = 1 AND ""categoryName"" = 'SciFi'
                            AND ""piecesAvailable"" is NULL AND ""piecesRequired"" = 0
                    ) AS subq
                "
            },
            {
                "InsertOneWithNullFieldValue",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable"", ""piecesRequired""
                        FROM " + _Composite_NonAutoGenPK + @"
                        WHERE categoryid = 3 AND pieceid = 1 AND ""categoryName"" = 'SciFi'
                            AND ""piecesAvailable"" is NULL AND ""piecesRequired"" = 1
                    ) AS subq
                "
            },
            {
                "PatchOne_Insert_PKAutoGen_Test",
                $"INSERT INTO { _integrationTableName } " +
                $"(id, title, publisher_id)" +
                $"VALUES (1000,'The Hobbit Returns to The Shire',1234)"
            }
        };

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

            _restService = new RestService(_queryEngine,
                _mutationEngine,
                _sqlMetadataProvider,
                _httpContextAccessor.Object,
                _authorizationService.Object);
            _restController = new RestController(_restService);
        }

        #endregion

        [TestCleanup]
        public async Task TestCleanup()
        {
            await ResetDbStateAsync();
        }

        public override string GetDefaultSchema()
        {
            return DEFAULT_SCHEMA;
        }

        public override string GetQuery(string key)
        {
            return _queryMap[key];
        }
    }
}
