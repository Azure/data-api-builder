// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests.Put
{
    /// <summary>
    /// Test REST Apis validating expected results are obtained.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class MsSqlPutApiTests : PutApiTestBase
    {
        private static Dictionary<string, string> _queryMap = new()
        {
            {
                "PutOne_Update_Test",
                $"SELECT [id], [title], [publisher_id] FROM { _integrationTableName } " +
                $"WHERE id = 7 AND [title] = 'The Hobbit Returns to The Shire' " +
                $"AND [publisher_id] = 1234" +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PutOne_Update_IfMatchHeaders_Test",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE id = 1 AND title = 'The Return of the King' " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PutOneUpdateWithDatabasePolicy",
                $"SELECT [categoryid], [pieceid], [categoryName],[piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE [categoryid] = 100 AND [pieceid] = 99 AND [categoryName] = 'SciFi' " +
                $"AND [piecesAvailable]= 4 AND [piecesRequired] = 5 AND [pieceid] != 1 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PutOneInsertWithDatabasePolicy",
                $"SELECT [categoryid], [pieceid], [categoryName],[piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE [categoryid] = 0 AND [pieceid] = 7 AND [categoryName] = 'SciFi' " +
                $"AND [piecesAvailable]= 4 AND [piecesRequired] = 0 AND ([pieceid] != 6 AND [piecesAvailable] > 0) " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PutOne_Update_Default_Test",
                $"SELECT [id], [book_id], [content], [websiteuser_id] FROM { _tableWithCompositePrimaryKey } " +
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
                $"WHERE [categoryid] = 2 AND [pieceid] = 1 AND [categoryName] = 'Tales' " +
                $"AND [piecesAvailable] is NULL  AND [piecesRequired] = 4 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PutOneUpdateWithComputedFieldMissingFromRequestBody",
                $"SELECT * FROM { _tableWithReadOnlyFields } " +
                $"WHERE [id] = 1 AND [book_name] = 'New book' AND [copies_sold] = 101 AND [last_sold_on] = '2023-09-12 05:30:30' AND [last_sold_on_date] = '2023-09-12 05:30:30' " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PutOneInsertWithComputedFieldMissingFromRequestBody",
                $"SELECT * FROM { _tableWithReadOnlyFields } " +
                $"WHERE [id] = 2 AND [book_name] = 'New book' AND [copies_sold] = 101 AND " +
                $"[last_sold_on] = '1999-01-08 10:23:54' AND [last_sold_on_date] = '1999-01-08 10:23:54' " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PutOneUpdateWithRowversionFieldMissingFromRequestBody",
                $"SELECT * FROM {_tableWithReadOnlyFields } WHERE [id] = 1 AND [book_name] = 'Another Awesome Book' " +
                $"AND [copies_sold] = 100 AND [last_sold_on] is NULL AND [last_sold_on_date] is NULL AND [row_version] is NOT NULL " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PutOneInsertWithRowversionFieldMissingFromRequestBody",
                $"SELECT * FROM {_tableWithReadOnlyFields } WHERE [id] = 2 AND [book_name] = 'Best seller' " +
                $"AND [copies_sold] = 100 AND [last_sold_on] is NULL AND [last_sold_on_date] is NULL AND [row_version] is NOT NULL " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PutOneUpdateStocksViewSelected",
                $"SELECT [categoryid], [pieceid], [categoryName],[piecesAvailable] " +
                $"FROM { _simple_subset_stocks } WHERE [categoryid] = 2 " +
                $"AND [pieceid] = 1 AND [categoryName] = 'Historical' AND [piecesAvailable] is NULL " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PutOne_Update_With_Mapping_Test",
                $"SELECT [treeId], [species] AS [Scientific Name], [region] AS " +
                $"[United State's Region], [height] FROM { _integrationMappingTable } " +
                $"WHERE treeId = 1 " +
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
                $"SELECT [id], [title], [volume], [categoryName], [series_id] FROM { _integration_AutoGenNonPK_TableName } " +
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
                "PutOneInsertInStocksViewSelected",
                $"SELECT [categoryid], [pieceid], [categoryName],[piecesAvailable] " +
                $"FROM { _simple_subset_stocks } WHERE [categoryid] = 4 " +
                $"AND [pieceid] = 1 AND [categoryName] = 'SciFi' AND [piecesAvailable] = 0 " +
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
                "UpdateSqlInjectionQuery1",
                $"SELECT [id], [title], [publisher_id] FROM { _integrationTableName } " +
                $"WHERE id = 7 AND [title] = ' UNION SELECT * FROM books/*' " +
                $"AND [publisher_id] = 1234" +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "UpdateSqlInjectionQuery2",
                $"SELECT [id], [title], [publisher_id] FROM { _integrationTableName } " +
                $"WHERE id = 7 AND [title] = '; SELECT * FROM information_schema.tables/*' " +
                $"AND [publisher_id] = 1234" +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "UpdateSqlInjectionQuery3",
                $"SELECT [id], [title], [publisher_id] FROM { _integrationTableName } " +
                $"WHERE id = 7 AND [title] = 'value; SELECT * FROM v$version--' " +
                $"AND [publisher_id] = 1234" +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "UpdateSqlInjectionQuery4",
                $"SELECT [id], [title], [publisher_id] FROM { _integrationTableName } " +
                $"WHERE id = 7 AND [title] = 'value; DROP TABLE authors;' " +
                $"AND [publisher_id] = 1234" +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PutOneUpdateAccessibleRowWithSecPolicy",
                $"SELECT [id], [revenue], [category], [accessible_role] FROM { _tableWithSecurityPolicy } " +
                $"WHERE [id] = 1 AND [revenue] = 2000 AND [category] = 'anime' AND [accessible_role] = 'Anonymous' " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PutOneUpdateInTableWithAutoGenPKAndTrigger",
                $"SELECT * FROM { _autogenPKTableWithTrigger } " +
                $"WHERE [id] = 1 AND [u_id] = 2 AND [salary] = 0 AND [name] = 'Joel' AND [position] = 'Senior Dev' " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PutOneUpdateInTableWithNonAutoGenPKAndTrigger",
                $"SELECT * FROM { _nonAutogenPKTableWithTrigger } " +
                $"WHERE [id] = 1 AND [months] = 3 AND [name] = 'Joel' AND [salary] = 50 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PutOneInsertInTableWithNonAutoGenPKAndTrigger",
                $"SELECT * FROM { _nonAutogenPKTableWithTrigger } " +
                $"WHERE [id] = 3 AND [months] = 2 AND [name] = 'Paris' AND [salary] = 30 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PutOne_Update_WithNoReadAction_Test",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE 0 = 1 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PutOne_Update_WithExcludeFields_Test",
                $"SELECT [id], [title] FROM { _integrationTableName } " +
                $"WHERE id = 7 AND [title] = 'The Hobbit Returns to The Shire' " +
                $"AND [publisher_id] = 1234" +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PutInsert_NoReadTest",
                $"SELECT [categoryid], [pieceid], [categoryName],[piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE 0 = 1 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "Put_Insert_WithExcludeFieldsTest",
                $"SELECT [categoryid], [pieceid], [piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE [categoryid] = 0 AND [pieceid] = 7 AND [categoryName] = 'SciFi' " +
                $"AND [piecesAvailable]= 4 AND [piecesRequired] = 4 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
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

        [TestMethod]
        public async Task PutOneInViewBadRequest()
        {
            string expectedErrorMessage = $"View or function '{_defaultSchemaName}.{_composite_subset_bookPub}' is not updatable " +
                                           "because the modification affects multiple base tables.";
            await base.PutOneInViewBadRequest(expectedErrorMessage);
        }

        /// <summary>
        /// Test to validate successful execution of a request when a rowversion field is missing from the request body.
        /// In such a case, we don't attempt to NULL out rowversion field (as per PUT semantics) but instead skip updating/inserting the field. 
        /// </summary>
        [TestMethod]
        public async Task PutOneWithRowversionFieldMissingFromRequestBody()
        {
            // Validate successful execution of a PUT update when a rowversion field (here 'row_version')
            // is missing from the request body. Successful execution of the PUT request confirms that we did not
            // attempt to NULL out the 'row_version' field. Had DAB attempted to NULL out the 'row_version' field,
            // we would have got an exception as we cannot provide a value for a field with sql server type of 'rowversion'.
            string requestBody = @"
            {
                ""book_name"": ""Another Awesome Book"",
                ""copies_sold"": 100,
                ""last_sold_on"": null
            }";
            string expectedLocationHeader = $"id/1";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: expectedLocationHeader,
                    queryString: null,
                    entityNameOrPath: _entityWithReadOnlyFields,
                    sqlQuery: GetQuery("PutOneUpdateWithRowversionFieldMissingFromRequestBody"),
                    operationType: EntityActionOperation.Upsert,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK
                );

            // Validate successful execution of a PUT insert when a rowversion field (here 'row_version')
            // is missing from the request body. Successful execution of the PUT request confirms that we did not
            // attempt to NULL out the 'row_version' field. Had DAB attempted to NULL out the 'row_version' field,
            // we would have got an exception as we cannot provide a value for a field with sql server type of 'rowversion'.
            requestBody = @"
            {
                ""book_name"": ""Best seller"",
                ""copies_sold"": 100,
                ""last_sold_on"": null
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: $"id/2",
                    queryString: null,
                    entityNameOrPath: _entityWithReadOnlyFields,
                    sqlQuery: GetQuery("PutOneInsertWithRowversionFieldMissingFromRequestBody"),
                    operationType: EntityActionOperation.Upsert,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.Created,
                    expectedLocationHeader: string.Empty
                );
        }

        /// <summary>
        /// Test to validate that whenever a rowversion field is included in the request body, we throw a BadRequest exception
        /// as it is not allowed to provide value (to insert/update) for a rowversion field.
        /// </summary>
        [TestMethod]
        public virtual async Task PutOneWithRowversionFieldInRequestBody()
        {
            // Validate that a BadRequest exception is thrown for a PUT update when a rowversion field (here 'row_version') is included in request body.
            string requestBody = @"
            {
                ""book_name"": ""Another Awesome Book"",
                ""copies_sold"": 100,
                ""last_sold_on"": null,
                ""row_version"": null
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/1",
                queryString: string.Empty,
                entityNameOrPath: _entityWithReadOnlyFields,
                sqlQuery: string.Empty,
                operationType: EntityActionOperation.Upsert,
                exceptionExpected: true,
                requestBody: requestBody,
                expectedErrorMessage: "Field 'row_version' cannot be included in the request body.",
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
                );

            // Validate that a BadRequest exception is thrown for a PUT insert when a rowversion field (here 'row_version') is included in request body.
            requestBody = @"
            {
                ""book_name"": ""New book"",
                ""copies_sold"": 100,
                ""last_sold_on"": null,
                ""row_version"": null
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/2",
                queryString: string.Empty,
                entityNameOrPath: _entityWithReadOnlyFields,
                sqlQuery: string.Empty,
                operationType: EntityActionOperation.Upsert,
                exceptionExpected: true,
                requestBody: requestBody,
                expectedErrorMessage: "Field 'row_version' cannot be included in the request body.",
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
                );
        }
        /// <summary>
        /// Test to validate that even when an update DML trigger is enabled on a table, we still return the
        /// latest data as it is present after the trigger gets executed. To validate that the data is returned
        /// as it is after the trigger is executed, we use the values which are updated by the trigger in the WHERE predicates of the verifying sql query.
        /// </summary>
        [TestMethod]
        public async Task PutOneUpdateInTableWithUpdateTrigger()
        {
            // Validate that PUT operation (resulting in update) succeeds when an update DML trigger is enabled for a table
            // with autogenerated primary key.  Given input item with salary: -9, the selection would return salary = 0.
            // Thus confirming that we return the data being updated by the trigger where, the trigger behavior is that it
            // updates the salary to max(0, min(150, salary)).
            string requestBody = @"
            {
                ""name"": ""Joel"",
                ""salary"": -9,
                ""position"": ""Senior Dev""
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/1/u_id/2",
                queryString: null,
                entityNameOrPath: _autogenPKEntityWithTrigger,
                sqlQuery: GetQuery("PutOneUpdateInTableWithAutoGenPKAndTrigger"),
                operationType: EntityActionOperation.Upsert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.OK
            );

            // Validate that PUT operation (resulting in update) succeeds when an update DML trigger is enabled for a table
            // with non-autogenerated primary key.  Given input item with salary: 100, the selection would return salary = 50.
            // Thus confirming that we return the data being updated by the trigger where, the trigger behavior is that it
            // updates the salary to max(0,min(50,salary)).
            requestBody = @"
            {
                ""name"": ""Joel"",
                ""salary"": 100
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/1/months/3",
                queryString: null,
                entityNameOrPath: _nonAutogenPKEntityWithTrigger,
                sqlQuery: GetQuery("PutOneUpdateInTableWithNonAutoGenPKAndTrigger"),
                operationType: EntityActionOperation.Upsert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.OK
            );
        }

        /// <summary>
        /// Test to validate that even when an insert DML trigger is enabled on a table, we still return the
        /// latest data as it is present after the trigger gets executed. To validate that the data is returned
        /// as it is after the trigger is executed, we use the values which are updated by the trigger in the WHERE predicates of the verifying sql query.
        /// </summary>
        [TestMethod]
        public async Task PutOneInsertInTableWithInsertTrigger()
        {
            // Validate that PUT operation (resulting in insert) succeeds when an insert DML trigger is enabled for a table
            // with non-autogenerated primary key.  Given input item with salary: 100, the selection would return salary = 30.
            // Thus confirming that we return the data being updated by the trigger where, the trigger behavior is that it
            // updates the salary to max(0,min(30,salary)).
            string requestBody = @"
            {
                ""name"": ""Paris"",
                ""salary"": 100
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/3/months/2",
                queryString: null,
                entityNameOrPath: _nonAutogenPKEntityWithTrigger,
                sqlQuery: GetQuery("PutOneInsertInTableWithNonAutoGenPKAndTrigger"),
                operationType: EntityActionOperation.Upsert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.Created,
                expectedLocationHeader: string.Empty
            );
        }

        #region RestApiTestBase Overrides

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
            return $"Cannot insert the value NULL into column 'piecesRequired', " +
                   $"table '{DatabaseName}.dbo.stocks'; column does not allow nulls. UPDATE fails.";
        }

        /// <inheritdoc/>
        [TestMethod]
        public override async Task PutOneUpdateTestOnTableWithSecurityPolicy()
        {
            string requestBody = @"
            {
                ""revenue"": ""2000"",
                ""category"" : ""anime"",
                ""accessible_role"": ""Anonymous""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "id/1",
                    queryString: null,
                    entityNameOrPath: _entityWithSecurityPolicy,
                    sqlQuery: GetQuery("PutOneUpdateAccessibleRowWithSecPolicy"),
                    operationType: EntityActionOperation.Upsert,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK
                );
        }

        [TestMethod]
        public async Task PutOneUpdateNonNullableDefaultFieldMissingFromJsonBodyTest()
        {
            await base.PutOneUpdateNonNullableDefaultFieldMissingFromJsonBodyTest();
        }

        #endregion
    }
}
