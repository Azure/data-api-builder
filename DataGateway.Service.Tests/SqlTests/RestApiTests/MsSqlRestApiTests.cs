using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests.RestApiTests
{
    /// <summary>
    /// Test REST Apis validating expected results are obtained.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class MsSqlRestApiTests : RestApiTestBase
    {
        protected static string DEFAULT_SCHEMA = "dbo";
        private static Dictionary<string, string> _queryMap = new()
        {
            {
                "FindByIdTest",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE id = 2 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "FindEmptyTable",
                $"SELECT * FROM { _emptyTableTableName } " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindEmptyResultSetWithQueryFilter",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE 1 != 1 FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindViewAll",
                $"SELECT * FROM { _simple_all_books } " +
                $"WHERE id = 2 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "FindViewSelected",
                $"SELECT categoryid, pieceid, categoryName, piecesAvailable FROM {_simple_subset_stocks} " +
                $"WHERE categoryid = 2 AND pieceid = 1 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "FindViewComposite",
                $"SELECT name ,id, publisher_id FROM {_composite_subset_bookPub} " +
                $"WHERE id=2 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "FindTestWithFilterQueryOneGeFilterOnView",
                $"SELECT * FROM { _simple_all_books } " +
                $"WHERE id >= 4 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindByIdTestWithQueryStringFieldsOnView",
                $"SELECT[id], [title] FROM { _simple_all_books } " +
                $"WHERE id = 1 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "FindTestWithFilterQueryStringOneEqFilterOnView",
                $"SELECT [categoryid],[pieceid],[categoryName],[piecesAvailable] " +
                $"FROM { _simple_subset_stocks } " +
                $"WHERE [pieceid] = 1 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFilterQueryOneNotFilterOnView",
                $"SELECT [categoryid],[pieceid],[categoryName],[piecesAvailable] " +
                $"FROM { _simple_subset_stocks } " +
                $"WHERE NOT([categoryid] > 1)" +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFilterQueryOneLtFilterOnView",
                $"SELECT[id], [name],[publisher_id] FROM { _composite_subset_bookPub } " +
                $"WHERE id < 5 FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindByIdTestWithQueryStringFields",
                $"SELECT[id], [title] FROM { _integrationTableName } " +
                $"WHERE id = 1 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "FindTestWithQueryStringOneField",
                $"SELECT [id] FROM { _integrationTableName } " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithQueryStringMultipleFields",
                $"SELECT [id], [title] FROM { _integrationTableName } " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithQueryStringAllFields",
                $"SELECT * FROM { _integrationTableName } " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFilterQueryStringOneEqFilter",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE id = 1 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFilterQueryStringValueFirstOneEqFilter",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE id = 2 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFilterQueryOneGtFilter",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE id > 3 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFilterQueryOneGeFilter",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE id >= 4 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFilterQueryOneLtFilter",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE id < 5 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFilterQueryOneLeFilter",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE id <= 4 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFilterQueryOneNeFilter",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE id != 3 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFilterQueryOneNotFilter",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE NOT (id < 2) " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFilterQueryOneRightNullEqFilter",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE NOT (title IS NULL) " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFilterQueryOneLeftNullNeFilter",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE title IS NOT NULL " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFilterQueryOneLeftNullRightNullGtFilter",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE NULL > NULL " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFilterQueryStringSingleAndFilter",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE id < 3 AND id > 1 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFilterQueryStringSingleOrFilter",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE id < 3 OR id > 4 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFilterQueryStringMultipleAndFilters",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE id < 4 AND id > 1 AND title != 'Awesome book' " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFilterQueryStringMultipleOrFilters",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE id = 1 OR id = 2 OR id = 3 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFilterQueryStringMultipleAndOrFilters",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE (id > 2 AND id < 4) OR title = 'Awesome book' " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFilterQueryStringMultipleNotAndOrFilters",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE (NOT (id < 3) OR id < 4) OR NOT (title = 'Awesome book') " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithPrimaryKeyContainingForeignKey",
                $"SELECT [id], [content] FROM reviews " +
                $"WHERE id = 567 AND book_id = 1 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "FindTestWithFirstSingleKeyPagination",
                $"SELECT TOP 1 * FROM { _integrationTableName } " +
                $"ORDER BY id " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFirstMultiKeyPagination",
                $"SELECT TOP 1 * FROM REVIEWS " +
                $"WHERE 1=1 " +
                $"ORDER BY book_id, id " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithAfterSingleKeyPagination",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE id > 7 " +
                $"ORDER BY id " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithAfterMultiKeyPagination",
                $"SELECT * FROM REVIEWS " +
                "WHERE book_id > 1 OR (book_id = 1 AND id > 567) " +
                $"ORDER BY book_id, id " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithPaginationVerifSinglePrimaryKeyInAfter",
                $"SELECT TOP 1 * FROM { _integrationTableName } " +
                $"ORDER BY id " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithPaginationVerifMultiplePrimaryKeysInAfter",
                $"SELECT TOP 1 * FROM REVIEWS " +
                $"ORDER BY book_id, id " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithQueryStringAllFieldsOrderByAsc",
                $"SELECT * FROM { _integrationTableName } " +
                $"ORDER BY title, id " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithQueryStringSpaceInNamesOrderByAsc",
                $"SELECT * FROM { _integrationTableHasColumnWithSpace } " +
                $"ORDER BY [ID Number] " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFirstAndSpacedColumnOrderBy",
                $"SELECT TOP 1 * FROM { _integrationTableHasColumnWithSpace } " +
                $"ORDER BY [Last Name] " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithQueryStringAllFieldsOrderByDesc",
                $"SELECT * FROM { _integrationTableName } " +
                $"ORDER BY publisher_id desc, id " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFirstSingleKeyPaginationAndOrderBy",
                $"SELECT TOP 1 * FROM { _integrationTableName } " +
                $"ORDER BY title, id " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestVerifyMaintainColumnOrderForOrderBy",
                $"SELECT * FROM { _integrationTableName } " +
                $"ORDER BY id desc, publisher_id " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestVerifyMaintainColumnOrderForOrderByInReverse",
                $"SELECT * FROM { _integrationTableName } " +
                $"ORDER BY publisher_id, id desc " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFirstSingleKeyIncludedInOrderByAndPagination",
                $"SELECT TOP 1 * FROM { _integrationTableName } " +
                $"ORDER BY id " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },

            {
                "FindTestWithFirstTwoOrderByAndPagination",
                $"SELECT TOP 2 * FROM { _integrationTableName } " +
                $"ORDER BY id " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFirstTwoVerifyAfterFormedCorrectlyWithOrderBy",
                $"SELECT TOP 2 * FROM { _integrationTieBreakTable } " +
                $"ORDER BY birthdate, name, id desc " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFirstTwoVerifyAfterBreaksTieCorrectlyWithOrderBy",
                $"SELECT TOP 2 * FROM { _integrationTieBreakTable } " +
                $"WHERE ((birthdate > '2001-01-01') OR (birthdate = '2001-01-01' AND name > 'Aniruddh') " +
                $"OR (birthdate = '2001-01-01' AND name = 'Aniruddh' AND id > 125)) " +
                $"ORDER BY birthdate, name, id " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFirstMultiKeyIncludeAllInOrderByAndPagination",
                $"SELECT TOP 1 * FROM { _tableWithCompositePrimaryKey } " +
                $"ORDER BY id desc, book_id " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFirstMultiKeyIncludeOneInOrderByAndPagination",
                $"SELECT TOP 1 * FROM { _tableWithCompositePrimaryKey } " +
                $"ORDER BY book_id, id " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFirstAndMultiColumnOrderBy",
                $"SELECT TOP 1 * FROM { _integrationTableName } " +
                $"ORDER BY publisher_id desc, title desc " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFirstAndTiedColumnOrderBy",
                $"SELECT TOP 1 * FROM { _integrationTableName } " +
                $"ORDER BY publisher_id desc, id asc " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFirstMultiKeyPaginationAndOrderBy",
                $"SELECT TOP 1 * FROM { _tableWithCompositePrimaryKey } " +
                $"WHERE 1=1 " +
                $"ORDER BY content desc, book_id, id " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithMappedFieldsToBeReturned",
                $"SELECT [treeId], [species] AS [Scientific Name], [region] AS [United State's Region], [height] FROM { _integrationMappingTable } " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithSingleMappedFieldsToBeReturned",
                $"SELECT [species] AS [Scientific Name] FROM { _integrationMappingTable } " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithUnMappedFieldsToBeReturned",
                $"SELECT [treeId] FROM { _integrationMappingTable } " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithDifferentMappedFieldsAndFilter",
                $"SELECT [treeId], [species] AS [fancyName], [region], [height] FROM { _integrationMappingTable } " +
                $"WHERE [species] = 'Tsuga terophylla' " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithDifferentMappedFieldsAndOrderBy",
                $"SELECT [treeId], [species] AS [fancyName], [region], [height] FROM { _integrationMappingTable } " +
                $"ORDER BY [trees].[species] " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithDifferentMappingFirstSingleKeyPaginationAndOrderBy",
                $"SELECT TOP 1 [treeId], [species] AS [fancyName], [region], [height] FROM { _integrationMappingTable } " +
                $"ORDER BY [trees].[species] " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithDifferentMappingAfterSingleKeyPaginationAndOrderBy",
                $"SELECT TOP 101 [treeId], [species] AS [fancyName], [region], [height] FROM { _integrationMappingTable } " +
                $"WHERE [trees].[treeId] < 2 " +
                $"ORDER BY [trees].[species], [trees].[treeId] " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "InsertOneTest",
                // This query is the query for the result we get back from the database
                // after the insert operation. Not the query that we generate to perform
                // the insertion.
                $"SELECT [id], [title], [publisher_id] FROM { _integrationTableName } " +
                $"WHERE [id] = { STARTING_ID_FOR_TEST_INSERTS } AND [title] = 'My New Book' " +
                $"AND [publisher_id] = 1234 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "InsertOneInCompositeNonAutoGenPKTest",
                // This query is the query for the result we get back from the database
                // after the insert operation. Not the query that we generate to perform
                // the insertion.
                $"SELECT [categoryid],[pieceid],[categoryName],[piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE [categoryid] = 5 AND [pieceid] = 2 AND [categoryName] = 'FairyTales' " +
                $"AND [piecesAvailable] = 0 AND [piecesRequired] = 0 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "InsertOneInCompositeKeyTableTest",
                // This query is the query for the result we get back from the database
                // after the insert operation. Not the query that we generate to perform
                // the insertion.
                $"SELECT [id], [content], [book_id] FROM { _tableWithCompositePrimaryKey } " +
                $"WHERE [id] = { STARTING_ID_FOR_TEST_INSERTS } AND [book_id] = 1 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "InsertOneInDefaultTestTable",
                $"SELECT [id], [book_id], [content] FROM { _tableWithCompositePrimaryKey } " +
                $"WHERE [id] = { STARTING_ID_FOR_TEST_INSERTS + 1} AND [book_id] = 2 AND [content] = 'Its a classic' " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "DeleteOneTest",
                // This query is used to confirm that the item no longer exists, not the
                // actual delete query.
                $"SELECT [id] FROM { _integrationTableName } " +
                $"WHERE id = 5 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PutOne_Update_Test",
                $"SELECT [id], [title], [publisher_id] FROM { _integrationTableName } " +
                $"WHERE id = 7 AND [title] = 'The Hobbit Returns to The Shire' " +
                $"AND [publisher_id] = 1234" +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PutOne_Update_IfMatchHeaders_Test_Confirm_Update",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE id = 1 AND title = 'The Return of the King' " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PutOne_Update_Default_Test",
                $"SELECT [id], [book_id], [content] FROM { _tableWithCompositePrimaryKey } " +
                $"WHERE [id] = 568 AND [book_id] = 1 AND [content]='Good book to read' " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PutOne_Update_CompositeNonAutoGenPK_Test",
                $"SELECT [categoryid], [pieceid], [categoryName], [piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE [categoryid] = 2 AND [pieceid] = 1 AND [categoryName] = 'SciFi' " +
                $"AND [piecesAvailable] = 10  AND [piecesRequired] = 5 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PutOne_Update_NullOutMissingField_Test",
                $"SELECT [categoryid], [pieceid], [categoryName],[piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE [categoryid] = 1 AND [pieceid] = 1 AND [categoryName] = 'SciFi' " +
                $"AND [piecesAvailable] is NULL  AND [piecesRequired] = 5 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PutOne_Update_Empty_Test",
                $"SELECT [categoryid], [pieceid], [categoryName],[piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE [categoryid] = 2 AND [pieceid] = 1 AND [categoryName] = '' " +
                $"AND [piecesAvailable] = 2  AND [piecesRequired] = 3 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PutOne_Update_Nulled_Test",
                $"SELECT [categoryid], [pieceid], [categoryName],[piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE [categoryid] = 2 AND [pieceid] = 1 AND [categoryName] = 'FairyTales' " +
                $"AND [piecesAvailable] is NULL  AND [piecesRequired] = 4 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PutOne_Insert_Test",
                $"SELECT [id], [title], [issue_number] FROM [foo].{ _integration_NonAutoGenPK_TableName } " +
                $"WHERE id = { STARTING_ID_FOR_TEST_INSERTS } AND [title] = 'Batman Returns' " +
                $"AND [issue_number] = 1234" +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PutOne_Insert_Nullable_Test",
                $"SELECT [id], [title], [issue_number] FROM [foo].{ _integration_NonAutoGenPK_TableName } " +
                $"WHERE id = { STARTING_ID_FOR_TEST_INSERTS + 1 } AND [title] = 'Times' " +
                $"AND [issue_number] IS NULL " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PutOne_Insert_PKAutoGen_Test",
                $"INSERT INTO { _integrationTableName } " +
                $"(id, title, publisher_id)" +
                $"VALUES (1000,'The Hobbit Returns to The Shire',1234)"
            },
            {
                "PutOne_Insert_AutoGenNonPK_Test",
                $"SELECT [id], [title], [volume], [categoryName] FROM { _integration_AutoGenNonPK_TableName } " +
                $"WHERE id = { STARTING_ID_FOR_TEST_INSERTS } AND [title] = 'Star Trek' " +
                $"AND [categoryName] = 'Suspense' " +
                $"AND [volume] IS NOT NULL " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PutOne_Insert_CompositeNonAutoGenPK_Test",
                $"SELECT [categoryid], [pieceid], [categoryName],[piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE [categoryid] = 3 AND [pieceid] = 1 AND [categoryName] = 'SciFi' " +
                $"AND [piecesAvailable] = 2 AND [piecesRequired] = 1 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PutOne_Insert_Default_Test",
                $"SELECT [categoryid], [pieceid], [categoryName],[piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE [categoryid] = 8 AND [pieceid] = 1 AND [categoryName] = 'SciFi' " +
                $"AND [piecesAvailable] = 0 AND [piecesRequired] = 0 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PutOne_Insert_Empty_Test",
                $"SELECT [categoryid], [pieceid], [categoryName],[piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE [categoryid] = 4 AND [pieceid] = 1 AND [categoryName] = '' " +
                $"AND [piecesAvailable] = 2 AND [piecesRequired] = 3 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PutOne_Insert_Nulled_Test",
                $"SELECT [categoryid], [pieceid], [categoryName],[piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE [categoryid] = 4 AND [pieceid] = 1 AND [categoryName] = 'SciFi' " +
                $"AND [piecesAvailable] is NULL AND [piecesRequired] = 4 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOne_Insert_NonAutoGenPK_Test",
                $"SELECT [id], [title], [issue_number] FROM [foo].{ _integration_NonAutoGenPK_TableName } " +
                $"WHERE id = 2 AND [title] = 'Batman Begins' " +
                $"AND [issue_number] = 1234 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOne_Insert_CompositeNonAutoGenPK_Test",
                $"SELECT [categoryid], [pieceid], [categoryName],[piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE [categoryid] = 4 AND [pieceid] = 1 AND [categoryName] = 'FairyTales' " +
                $"AND [piecesAvailable] = 5 AND [piecesRequired] = 4 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOne_Insert_Empty_Test",
                $"SELECT [categoryid], [pieceid], [categoryName],[piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE [categoryid] = 5 AND [pieceid] = 1 AND [categoryName] = '' " +
                $"AND [piecesAvailable] = 5 AND [piecesRequired] = 4 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOne_Insert_Default_Test",
                $"SELECT [categoryid], [pieceid], [categoryName],[piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE [categoryid] = 7 AND [pieceid] = 1 AND [categoryName] = 'SciFi' " +
                $"AND [piecesAvailable] = 0 AND [piecesRequired] = 0 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOne_Insert_Nulled_Test",
                $"SELECT [categoryid], [pieceid], [categoryName],[piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE [categoryid] = 3 AND [pieceid] = 1 AND [categoryName] = 'SciFi' " +
                $"AND [piecesAvailable] is NULL AND [piecesRequired] = 4 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOne_Update_Test",
                $"SELECT [id], [title], [publisher_id] FROM { _integrationTableName } " +
                $"WHERE id = 8 AND [title] = 'Heart of Darkness' " +
                $"AND [publisher_id] = 2324 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOne_Update_IfMatchHeaders_Test_Confirm_Update",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE id = 1 AND title = 'The Hobbit Returns to The Shire' " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOne_Update_Default_Test",
                $"SELECT [id], [book_id], [content] FROM { _tableWithCompositePrimaryKey } " +
                $"WHERE id = 567 AND [book_id] = 1 AND [content] = 'That's a great book' " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOne_Update_CompositeNonAutoGenPK_Test",
                $"SELECT [categoryid], [pieceid], [categoryName],[piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE [categoryid] = 1 AND [pieceid] = 1 AND [categoryName] = 'SciFi' " +
                $"AND [piecesAvailable]= 10 AND [piecesRequired] = 0 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOne_Update_Empty_Test",
                $"SELECT [categoryid], [pieceid], [categoryName],[piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE [categoryid] = 1 AND [pieceid] = 1 AND [categoryName] = '' " +
                $"AND [piecesAvailable]= 10 AND [piecesRequired] = 0 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOne_Update_Nulled_Test",
                $"SELECT [categoryid], [pieceid], [categoryName],[piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE [categoryid] = 1 AND [pieceid] = 1 AND [categoryName] = 'books' " +
                $"AND [piecesAvailable] is NULL AND [piecesRequired] = 0 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "InsertOneWithNullFieldValue",
                $"SELECT [categoryid], [pieceid], [categoryName],[piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE [categoryid] = 3 AND [pieceid] = 1 AND [categoryName] = 'SciFi' " +
                $"AND [piecesAvailable] is NULL AND [piecesRequired] = 1 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
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
            await InitializeTestFixture(context, TestCategory.MSSQL);

            // Setup REST Components
            _restService = new RestService(_queryEngine,
                _mutationEngine,
                _sqlMetadataProvider,
                _httpContextAccessor.Object,
                _authorizationService.Object);
            _restController = new RestController(_restService);
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

        #region Additional tests

        /// <summary>
        /// This test verifies that when we have an unsupported opration,
        /// in this case a none operation, that we return the correct error
        /// response.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task HandleAndExecuteUnsupportedOperationUnitTestAsync()
        {
            string expected = "{\"error\":{\"code\":\"BadRequest\",\"message\":\"This operation is not supported.\",\"status\":400}}";
            // need header to instantiate identity in controller
            HeaderDictionary headers = new();
            headers.Add("x-ms-client-principal", Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"hello\":\"world\"}")));

            // Features are used to setup the httpcontext such that the test will run without null references
            IFeatureCollection features = new FeatureCollection();
            features.Set<IHttpRequestFeature>(new HttpRequestFeature { Headers = headers });
            features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(new MemoryStream()));
            features.Set<IHttpResponseFeature>(new HttpResponseFeature { StatusCode = (int)HttpStatusCode.OK });
            DefaultHttpContext httpContext = new(features);

            ConfigureRestController(_restController, string.Empty);
            _restController.ControllerContext.HttpContext = httpContext;

            // Setup params to invoke function with
            // Must use valid entity name
            string entityName = "Book";
            Operation operationType = Operation.None;
            string primaryKeyRoute = string.Empty;

            // Reflection to invoke a private method to unit test all code paths
            PrivateObject testObject = new(_restController);
            IActionResult actionResult = await testObject.Invoke("HandleOperation", new object[] { entityName, operationType, primaryKeyRoute });
            SqlTestHelper.VerifyResult(actionResult, expected, System.Net.HttpStatusCode.BadRequest, string.Empty);
        }

        #endregion

        #region RestApiTestBase Overrides

        public override string GetDefaultSchema()
        {
            return DEFAULT_SCHEMA;
        }

        /// <summary>
        /// We include a '.' for the Edm Model
        /// schema to allow both MsSql/PostgreSql
        /// and MySql to share code. MySql does not
        /// include a '.' but MsSql does so
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

        /// <summary>
        /// We have 1 test that is named
        /// PutOneUpdateNonNullableDefaultFieldMissingFromJsonBodyTest
        /// which will have Db specific error messages.
        /// We return the mssql specific message here.
        /// </summary>
        /// <returns></returns>
        public override string GetUniqueDbErrorMessage()
        {
            return "Cannot insert the value NULL into column 'piecesRequired', " +
                   "table 'master.dbo.stocks'; column does not allow nulls. UPDATE fails.";
        }

        #endregion

        #region Private helpers

        /// <summary>
        /// Helper function uses reflection to invoke
        /// private methods from outside class.
        /// Expects async method returning Task.
        /// </summary>
        class PrivateObject
        {
            private readonly object _classToInvoke;
            public PrivateObject(object classToInvoke)
            {
                _classToInvoke = classToInvoke;
            }

            public Task<IActionResult> Invoke(string privateMethodName, params object[] privateMethodArgs)
            {
                MethodInfo methodInfo = _classToInvoke.GetType().GetMethod(privateMethodName, BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (methodInfo is null)
                {
                    throw new System.Exception($"{privateMethodName} not found in class '{_classToInvoke.GetType()}'");
                }

                return (Task<IActionResult>)methodInfo.Invoke(_classToInvoke, privateMethodArgs);
            }
        }

        #endregion
    }
}
