// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests.Insert
{
    /// <summary>
    /// Test REST Apis validating expected results are obtained.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class MsSqlInsertApiTests : InsertApiTestBase
    {
        private static Dictionary<string, string> _queryMap = new()
        {
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
                "InsertOneInSupportedTypes",
                $"SELECT [id] as [typeid], [byte_types], [short_types], [int_types], [long_types],[string_types], [nvarchar_string_types], [single_types], [float_types], " +
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
                // This query is the query for the result we get back from the database
                // after the insert operation. Not the query that we generate to perform
                // the insertion.
                $"SELECT [NoteNum] AS [┬─┬ノ( º _ ºノ)], [DetailAssessmentAndPlanning] AS [始計], " +
                $"[WagingWar] AS [作戰], [StrategicAttack] AS [謀攻] FROM { _integrationUniqueCharactersTable } " +
                $"WHERE [NoteNum] = 2 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "InsertOneWithMappingTest",
                // This query is the query for the result we get back from the database
                // after the insert operation. Not the query that we generate to perform
                // the insertion.
                $"SELECT [treeId], [species] AS [Scientific Name], [region] AS " +
                $"[United State's Region], [height] FROM { _integrationMappingTable } " +
                $"WHERE [treeId] = 3 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "InsertOneInCompositeNonAutoGenPKTest",
                // This query is the query for the result we get back from the database
                // after the insert operation. Not the query that we generate to perform
                // the insertion.
                $"SELECT [categoryid],[pieceid],[categoryName],[piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE [categoryid] = 5 AND [pieceid] = 2 AND [categoryName] = 'Tales' " +
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
                "InsertOneWithNullFieldValue",
                $"SELECT [categoryid], [pieceid], [categoryName],[piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE [categoryid] = 3 AND [pieceid] = 1 AND [categoryName] = 'SciFi' " +
                $"AND [piecesAvailable] is NULL AND [piecesRequired] = 1 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "InsertSqlInjectionQuery1",
                // This query is the query for the result we get back from the database
                // after the insert operation. Not the query that we generate to perform
                // the insertion.
                $"SELECT [id], [title], [publisher_id] FROM { _integrationTableName } " +
                $"WHERE [id] = { STARTING_ID_FOR_TEST_INSERTS } " +
                $"AND [title] = ' UNION SELECT * FROM books/*' " +
                $"AND [publisher_id] = 1234 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "InsertSqlInjectionQuery2",
                // This query is the query for the result we get back from the database
                // after the insert operation. Not the query that we generate to perform
                // the insertion.
                $"SELECT [id], [title], [publisher_id] FROM { _integrationTableName } " +
                $"WHERE [id] = { STARTING_ID_FOR_TEST_INSERTS } " +
                $"AND [title] = '; SELECT * FROM information_schema.tables/*' " +
                $"AND [publisher_id] = 1234 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "InsertSqlInjectionQuery3",
                // This query is the query for the result we get back from the database
                // after the insert operation. Not the query that we generate to perform
                // the insertion.
                $"SELECT [id], [title], [publisher_id] FROM { _integrationTableName } " +
                $"WHERE [id] = { STARTING_ID_FOR_TEST_INSERTS } " +
                $"AND [title] = 'value; SELECT * FROM v$version--' " +
                $"AND [publisher_id] = 1234 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "InsertSqlInjectionQuery4",
                // This query is the query for the result we get back from the database
                // after the insert operation. Not the query that we generate to perform
                // the insertion.
                $"SELECT [id], [title], [publisher_id] FROM { _integrationTableName } " +
                $"WHERE [id] = { STARTING_ID_FOR_TEST_INSERTS } " +
                $"AND [title] = 'id; DROP TABLE books;' " +
                $"AND [publisher_id] = 1234 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "InsertSqlInjectionQuery5",
                // This query is the query for the result we get back from the database
                // after the insert operation. Not the query that we generate to perform
                // the insertion.
                $"SELECT [id], [title], [publisher_id] FROM { _integrationTableName } " +
                $"WHERE [id] = { STARTING_ID_FOR_TEST_INSERTS } " +
                $"AND [title] = ' '' UNION SELECT * FROM books/*' " +
                $"AND [publisher_id] = 1234 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "InsertOneAndReturnSingleRowWithStoredProcedureTest",
                // This query attempts retrieval of the stored procedure insert operation result,
                // and is explicitly not representative of the engine generated insert statement.
                $"SELECT table0.[id], table0.[title], table0.[publisher_id] FROM books AS table0 " +
                $"JOIN (SELECT id FROM publishers WHERE name = 'The First Publisher') AS table1 " +
                $"ON table0.[publisher_id] = table1.[id] " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "InsertOneAndReturnMultipleRowsWithStoredProcedureTest",
                // This query attempts retrieval of the stored procedure insert operation result,
                // and is explicitly not representative of the engine generated insert statement.
                $"SELECT table0.[id], table0.[title], table0.[publisher_id] FROM books AS table0 " +
                $"JOIN (SELECT id FROM publishers WHERE name = 'Big Company') AS table1 " +
                $"ON table0.[publisher_id] = table1.[id] "
            },
            {
                "InsertOneWithRowversionFieldMissingFromRequestBody",
                $"SELECT * FROM {_tableWithReadOnlyFields } WHERE [id] = 2 AND [book_name] = 'Another Awesome Book' " +
                $"AND [copies_sold] = 100 AND [last_sold_on] = '2023-08-28 12:36:08.8666667' " +
                $"AND [last_sold_on_date] = '2023-08-28 12:36:08.8666667' AND [row_version] is NOT NULL " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
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
        public override async Task InsertOneTestViolatingForeignKeyConstraint()
        {
            string requestBody = @"
            {
                ""title"": ""My New Book"",
                ""publisher_id"": 12345
            }";

            string expectedErrorMessage = "The INSERT statement conflicted with the FOREIGN KEY constraint" +
                    $" \"book_publisher_fk\". The conflict occurred in database \"{DatabaseName}\", table \"{_defaultSchemaName}.publishers\"" +
                    ", column 'id'.";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entityNameOrPath: _integrationEntityName,
                sqlQuery: string.Empty,
                operationType: EntityActionOperation.Insert,
                requestBody: requestBody,
                exceptionExpected: true,
                expectedErrorMessage: expectedErrorMessage,
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed.ToString()
            );
        }

        /// <inheritdoc/>
        [TestMethod]
        public override async Task InsertOneTestViolatingUniqueKeyConstraint()
        {
            string requestBody = @"
            {
                ""categoryid"": 1,
                ""pieceid"": 1,
                ""categoryName"": ""SciFi""
            }";

            string expectedErrorMessage = $"Cannot insert duplicate key in object '{_defaultSchemaName}.{_Composite_NonAutoGenPK_TableName}'.";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                sqlQuery: string.Empty,
                operationType: EntityActionOperation.Insert,
                requestBody: requestBody,
                exceptionExpected: true,
                expectedErrorMessage: expectedErrorMessage,
                expectedStatusCode: HttpStatusCode.Conflict,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed.ToString(),
                isExpectedErrorMsgSubstr: true
            );
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
            DatabaseEngine = TestCategory.MSSQL;
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
        /// Test to validate successful execution of a request when a rowversion field is missing from the request body.
        /// In such a case, we skip inserting the field. 
        /// </summary>
        [TestMethod]
        public async Task InsertOneWithRowversionFieldMissingFromRequestBody()
        {
            // Validate successful execution of a POST request when a rowversion field (here 'row_version')
            // is missing from the request body. Successful execution of the POST request confirms that we did not
            // attempt to provide a value for the 'row_version' field. Had DAB provided a value for 'row_version' field,
            // we would have got a BadRequest exception as we cannot provide a value for a field with sql server type of 'rowversion'.
            string requestBody = @"
            {
                ""id"": 2,
                ""book_name"": ""Another Awesome Book"",
                ""copies_sold"": 100,
                ""last_sold_on"": ""2023-08-28 12:36:08.8666667""
            }";
            string expectedLocationHeader = $"id/2";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: null,
                    queryString: null,
                    entityNameOrPath: _entityWithReadOnlyFields,
                    sqlQuery: GetQuery("InsertOneWithRowversionFieldMissingFromRequestBody"),
                    operationType: EntityActionOperation.Insert,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.Created,
                    expectedLocationHeader: expectedLocationHeader
                );
        }

        /// <summary>
        /// Test to validate that whenever a rowversion field is included in the request body, we throw a BadRequest exception
        /// as it is not allowed to provide value (not even null value) for a rowversion field.
        /// </summary>
        [TestMethod]
        public async Task InsertOneWithRowversionFieldInRequestBody()
        {
            // Validate that a BadRequest exception is thrown when a rowversion field (here 'row_version') is included in request body.
            string requestBody = @"
            {
                ""id"": 2,
                ""book_name"": ""Another Awesome Book"",
                ""copies_sold"": 100,
                ""last_sold_on"": null,
                ""row_version"": null
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: null,
                queryString: string.Empty,
                entityNameOrPath: _entityWithReadOnlyFields,
                sqlQuery: string.Empty,
                operationType: EntityActionOperation.Insert,
                exceptionExpected: true,
                requestBody: requestBody,
                expectedErrorMessage: "Field 'row_version' cannot be included in the request body.",
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
                );
        }

        [TestMethod]
        public async Task InsertOneInViewBadRequestTest()
        {
            string expectedErrorMessage = $"View or function '{_defaultSchemaName}.{_composite_subset_bookPub}' is not updatable " +
                                          $"because the modification affects multiple base tables.";
            await base.InsertOneInViewBadRequestTest(expectedErrorMessage, isExpectedErrorMsgSubstr: false);
        }

        /// <summary>
        /// Tests the Insert one and returns either single or multiple rows functionality with a REST POST request
        /// using stored procedure.
        /// The request executes a stored procedure which attempts to insert a book for a given publisher
        /// and then returns all books under that publisher.
        /// </summary>
        [DataRow("The First Publisher", "InsertOneAndReturnSingleRowWithStoredProcedureTest", true, DisplayName = "Test Single row result")]
        [DataRow("Big Company", "InsertOneAndReturnMultipleRowsWithStoredProcedureTest", false, DisplayName = "Test multiple row result")]
        [DataTestMethod]
        public async Task InsertOneAndVerifyReturnedRowsWithStoredProcedureTest(
            string publisherName,
            string queryName,
            bool expectJson)
        {
            string requestBody = @"
            {
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
                expectJson: expectJson
            );
        }

        /// <summary>
        /// Test to validate that even when an insert DML trigger is enabled on a table, we still return the
        /// latest data (values written by trigger). To validate that the data is returned after the trigger is executed,
        /// we use the new values (written by the trigger) in the WHERE predicates of the verifying sql query.
        /// </summary>
        [TestMethod]
        public async Task InsertOneInTableWithInsertTrigger()
        {
            // Given input item with salary: 102, this test verifies that the selection would return salary = 100.
            // Thus confirming that we return the data being updated by the trigger where,
            // the trigger behavior is that it updates the salary to max(0,min(100,salary)).
            string requestBody = @"
            {
                ""name"": ""Joel"",
                ""salary"": 102
            }";

            string expectedLocationHeader = $"id/{STARTING_ID_FOR_TEST_INSERTS}/u_id/2";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: null,
                queryString: null,
                entityNameOrPath: _autogenPKEntityWithTrigger,
                sqlQuery: GetQuery("InsertOneInTableWithAutoGenPKAndTrigger"),
                operationType: EntityActionOperation.Insert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.Created,
                expectedLocationHeader: expectedLocationHeader
            );

            // Given input item with salary: 102, this test verifies that the selection would return salary = 30.
            // Thus confirming that we return the data being updated by the trigger where,
            // the trigger behavior is that it updates the salary to max(0,min(30,salary)).
            requestBody = @"
            {
                ""id"": 3,
                ""name"": ""Tommy"",
                ""salary"": 102
            }";

            expectedLocationHeader = $"id/3/months/2";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: null,
                queryString: null,
                entityNameOrPath: _nonAutogenPKEntityWithTrigger,
                sqlQuery: GetQuery("InsertOneInTableWithNonAutoGenPKAndTrigger"),
                operationType: EntityActionOperation.Insert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.Created,
                expectedLocationHeader: expectedLocationHeader
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
