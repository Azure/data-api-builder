// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests.Insert
{
    /// <summary>
    /// Test REST Apis validating expected results are obtained.
    /// </summary>
    [TestClass, TestCategory(TestCategory.DWSQL)]
    public class DwSqlInsertApiTests : InsertApiTestBase
    {
        private static Dictionary<string, string> _queryMap = new()
        {
            {
                "InsertOneTest",
                $"SELECT [id], [title], [publisher_id] FROM { _integrationTableName } " +
                $"WHERE [id] = { STARTING_ID_FOR_TEST_INSERTS } AND [title] = 'My New Book' " +
                $"AND [publisher_id] = 1234 "
            },
            {
                "InsertOneInSupportedTypes",
                $"SELECT [id] as [typeid], [byte_types], [short_types], [int_types], [long_types],string_types, [single_types], [float_types], " +
                $"[decimal_types], [boolean_types], [date_types], [datetime_types], [datetime2_types], [datetimeoffset_types], [smalldatetime_types], " +
                $"[time_types], [bytearray_types], LOWER([uuid_types]) as [uuid_types] FROM { _integrationTypeTable } " +
                $"WHERE [id] = { STARTING_ID_FOR_TEST_INSERTS } AND [bytearray_types] is NULL " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "InsertOneWithComputedFieldMissingInRequestBody",
                $"SELECT * FROM {_tableWithReadOnlyFields } WHERE [id] = 2 AND [book_name] = 'Harry Potter' AND [copies_sold] = 50 AND " +
                $"[last_sold_on] = '1999-01-08 10:23:54' AND [last_sold_on_date] = '1999-01-08 10:23:54' " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "InsertOneInBooksViewAll",
                $"SELECT [id], [title], [publisher_id] FROM { _simple_all_books } " +
                $"WHERE [id] = { STARTING_ID_FOR_TEST_INSERTS } AND [title] = 'My New Book' " +
                $"AND [publisher_id] = 1234 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "InsertOneInStocksViewSelected",
                $"SELECT [categoryid], [pieceid], [categoryName],[piecesAvailable] " +
                $"FROM { _simple_subset_stocks } WHERE [categoryid] = 4 " +
                $"AND [pieceid] = 1 AND [categoryName] = 'SciFi' AND [piecesAvailable] = 0 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "InsertOneUniqueCharactersTest",
                $"SELECT [NoteNum] AS [┬─┬ノ( º _ ºノ)], [DetailAssessmentAndPlanning] AS [始計], " +
                $"[WagingWar] AS [作戰], [StrategicAttack] AS [謀攻] FROM { _integrationUniqueCharactersTable } " +
                $"WHERE [NoteNum] = 2 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "InsertOneWithMappingTest",
                $"SELECT [treeId], [species] AS [Scientific Name], [region] AS " +
                $"[United State's Region], [height] FROM { _integrationMappingTable } " +
                $"WHERE [treeId] = 3 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "InsertOneInCompositeNonAutoGenPKTest",
                $"SELECT [categoryid],[pieceid],[categoryName],[piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE [categoryid] = 5 AND [pieceid] = 2 AND [categoryName] = 'Tales' " +
                $"AND [piecesAvailable] = 0 AND [piecesRequired] = 0 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "InsertOneInCompositeKeyTableTest",
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
                "InsertOneWithNullFieldValue",
                $"SELECT [categoryid], [pieceid], [categoryName],[piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE [categoryid] = 3 AND [pieceid] = 1 AND [categoryName] = 'SciFi' " +
                $"AND [piecesAvailable] is NULL AND [piecesRequired] = 1 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "InsertSqlInjectionQuery1",
                $"SELECT [id], [title], [publisher_id] FROM { _integrationTableName } " +
                $"WHERE [id] = { STARTING_ID_FOR_TEST_INSERTS } " +
                $"AND [title] = ' UNION SELECT * FROM books/*' " +
                $"AND [publisher_id] = 1234 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "InsertSqlInjectionQuery2",
                $"SELECT [id], [title], [publisher_id] FROM { _integrationTableName } " +
                $"WHERE [id] = { STARTING_ID_FOR_TEST_INSERTS } " +
                $"AND [title] = '; SELECT * FROM information_schema.tables/*' " +
                $"AND [publisher_id] = 1234 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "InsertSqlInjectionQuery3",
                $"SELECT [id], [title], [publisher_id] FROM { _integrationTableName } " +
                $"WHERE [id] = { STARTING_ID_FOR_TEST_INSERTS } " +
                $"AND [title] = 'value; SELECT * FROM v$version--' " +
                $"AND [publisher_id] = 1234 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "InsertSqlInjectionQuery4",
                $"SELECT [id], [title], [publisher_id] FROM { _integrationTableName } " +
                $"WHERE [id] = { STARTING_ID_FOR_TEST_INSERTS } " +
                $"AND [title] = 'id; DROP TABLE books;' " +
                $"AND [publisher_id] = 1234 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "InsertSqlInjectionQuery5",
                $"SELECT [id], [title], [publisher_id] FROM { _integrationTableName } " +
                $"WHERE [id] = { STARTING_ID_FOR_TEST_INSERTS } " +
                $"AND [title] = ' '' UNION SELECT * FROM books/*' " +
                $"AND [publisher_id] = 1234 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "InsertOneAndReturnSingleRowWithStoredProcedureTest",
                $"SELECT table0.[id], table0.[title], table0.[publisher_id] FROM books AS table0 " +
                $"JOIN (SELECT id FROM publishers WHERE name = 'The First Publisher') AS table1 " +
                $"ON table0.[publisher_id] = table1.[id] "
            },
            {
                "InsertOneAndReturnMultipleRowsWithStoredProcedureTest",
                $"SELECT table0.[id], table0.[title], table0.[publisher_id] FROM books AS table0 " +
                $"JOIN (SELECT id FROM publishers WHERE name = 'Big Company') AS table1 " +
                $"ON table0.[publisher_id] = table1.[id] "
            },
            {
                "InsertOneWithRowversionFieldMissingFromRequestBody",
                $"SELECT * FROM {_tableWithReadOnlyFields } WHERE [id] = 2 AND [book_name] = 'Another Awesome Book' " +
                $"AND [copies_sold] = 100 AND [last_sold_on] = '2023-08-28 12:36:08.8666667' " +
                $"AND [last_sold_on_date] = '2023-08-28 12:36:08.8666667'"
            },
            {
                "InsertOneInTableWithAutoGenPKAndTrigger",
                $"SELECT * FROM { _autogenPKTableWithTrigger } " +
                $"WHERE [id] = {STARTING_ID_FOR_TEST_INSERTS} AND [u_id] = 2 AND [salary] = 100 AND [name] = 'Joel' " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "InsertOneInTableWithNonAutoGenPKAndTrigger",
                $"SELECT * FROM { _nonAutogenPKTableWithTrigger } " +
                $"WHERE [id] = 3 AND [months] = 2 AND [name] = 'Tommy' AND [salary] = 30 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "InsertOneWithExcludeFieldsTest",
                $"SELECT [id], [title] FROM { _integrationTableName } " +
                $"WHERE [id] = {STARTING_ID_FOR_TEST_INSERTS} " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "InsertOneWithNoReadPermissionsTest",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE [id] = {STARTING_ID_FOR_TEST_INSERTS} AND 0 = 1 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "InsertOneRowWithBuiltInMethodAsDefaultvaluesTest",
                $"SELECT * FROM { _defaultValueAsBuiltInMethodsTable } " +
                $"WHERE [id] = {STARTING_ID_FOR_TEST_INSERTS} " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            }
        };

        #region overridden tests

        /// <inheritdoc/>
        [TestMethod]
        [Ignore]
        public override Task InsertOneTestViolatingForeignKeyConstraint()
        {
            throw new NotImplementedException("Datawarehouse does not support the foreign key constraint.");
        }

        /// <inheritdoc/>
        [TestMethod]
        [Ignore]
        public override Task InsertOneTestViolatingUniqueKeyConstraint()
        {
            throw new NotImplementedException("Datawarehouse does not support the unique key constraint.");
        }

        #endregion

        #region Test Fixture Setup

        /// <summary>
        /// Sets up test fixture for class, only to be run once per test run, as defined by
        /// MSTest decorator.
        /// </summary>
        /// <param name="context"></param>
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.DWSQL;
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

        /// <summary>
        /// Tests the Insert one and returns either single or multiple rows functionality with a REST POST request
        /// using stored procedure.
        /// The request executes a stored procedure which attempts to insert a book for a given publisher
        /// and then returns all books under that publisher.
        /// </summary>
        [DataRow("The First Publisher", "InsertOneAndReturnSingleRowWithStoredProcedureTest", DisplayName = "Test Single row result")]
        [DataRow("Big Company", "InsertOneAndReturnMultipleRowsWithStoredProcedureTest", DisplayName = "Test multiple row result")]
        [DataTestMethod]
        public async Task InsertOneAndVerifyReturnedRowsWithStoredProcedureTest(
            string publisherName,
            string queryName)
        {
            string requestBody = @"
            {
                ""book_id"": 1000,
                ""title"": ""Happy New Year"",
                ""publisher_name"": """ + $"{publisherName}" + @"""" +
            "}";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: null,
                queryString: null,
                entityNameOrPath: _integrationProcedureInsertOneAndDisplay_EntityName,
                sqlQuery: GetQuery(queryName),
                operationType: EntityActionOperation.Execute,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.Created,
                expectedLocationHeader: string.Empty,
                expectJson: false
            );
        }

        #region RestApiTestBase Overrides

        public override string GetQuery(string key)
        {
            return _queryMap[key];
        }

        #endregion
    }
}
