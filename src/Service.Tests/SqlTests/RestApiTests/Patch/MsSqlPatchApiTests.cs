// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests.Patch
{
    /// <summary>
    /// Test REST Apis validating expected results are obtained.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class MsSqlPatchApiTests : PatchApiTestBase
    {
        private static Dictionary<string, string> _queryMap = new()
        {
            {
                "PatchOne_Insert_NonAutoGenPK_Test",
                $"SELECT [id], [title], [issue_number] FROM [foo].{ _integration_NonAutoGenPK_TableName } " +
                $"WHERE id = 2 AND [title] = 'Batman Begins' " +
                $"AND [issue_number] = 1234 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOne_Insert_UniqueCharacters_Test",
                $"SELECT [NoteNum] AS [┬─┬ノ( º _ ºノ)], [DetailAssessmentAndPlanning] AS [始計], " +
                $"[WagingWar] AS [作戰], [StrategicAttack] AS [謀攻] " +
                $"FROM { _integrationUniqueCharactersTable } " +
                $"WHERE [NoteNum] = 2 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOne_Insert_CompositeNonAutoGenPK_Test",
                $"SELECT [categoryid], [pieceid], [categoryName],[piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE [categoryid] = 4 AND [pieceid] = 1 AND [categoryName] = 'Tales' " +
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
                "PatchOne_Insert_Mapping_Test",
                $"SELECT [treeId], [species] AS [Scientific Name], [region] AS " +
                $"[United State's Region], [height] FROM { _integrationMappingTable } " +
                $"WHERE [treeId] = 4 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOneInsertInStocksViewSelected",
                $"SELECT [categoryid], [pieceid], [categoryName],[piecesAvailable] " +
                $"FROM { _simple_subset_stocks } WHERE [categoryid] = 4 " +
                $"AND [pieceid] = 1 AND [categoryName] = 'SciFi' AND [piecesAvailable] = 0 " +
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
                "PatchOne_Update_IfMatchHeaders_Test",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE id = 1 AND title = 'The Hobbit Returns to The Shire' " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOneUpdateWithDatabasePolicy",
                $"SELECT [categoryid], [pieceid], [categoryName],[piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE [categoryid] = 100 AND [pieceid] = 99 AND [categoryName] = 'Historical' " +
                $"AND [piecesAvailable]= 4 AND [piecesRequired] = 0 AND [pieceid] != 1 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOneInsertWithDatabasePolicy",
                $"SELECT [categoryid], [pieceid], [categoryName],[piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE [categoryid] = 0 AND [pieceid] = 7 AND [categoryName] = 'SciFi' " +
                $"AND [piecesAvailable]= 4 AND [piecesRequired] = 0 AND ([pieceid] != 6 AND [piecesAvailable] > 0) " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOne_Update_Default_Test",
                $"SELECT [id], [book_id], [content], [websiteuser_id] FROM { _tableWithCompositePrimaryKey } " +
                $"WHERE id = 567 AND [book_id] = 1 AND [content] = 'That''s a great book' " +
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
                "PatchOneUpdateWithComputedFieldMissingFromRequestBody",
                $"SELECT * FROM { _tableWithReadOnlyFields } " +
                $"WHERE [id] = 1 AND [book_name] = 'New book' AND [copies_sold] = 50 " +
                $"AND [last_sold_on] is not NULL AND [last_sold_on_date] is not NULL " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOneInsertWithComputedFieldMissingFromRequestBody",
                $"SELECT * FROM { _tableWithReadOnlyFields } " +
                $"WHERE [id] = 2 AND [book_name] = 'New book' AND [copies_sold] = 50 AND " +
                $"[last_sold_on] = '1999-01-08 10:23:54' AND [last_sold_on_date] = '1999-01-08 10:23:54' " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOneUpdateWithRowversionFieldMissingFromRequestBody",
                $"SELECT * FROM {_tableWithReadOnlyFields } WHERE [id] = 1 AND [book_name] = 'Another Awesome Book' " +
                $"AND [copies_sold] = 100 AND [last_sold_on] is NULL AND [row_version] is NOT NULL " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOneInsertWithRowversionFieldMissingFromRequestBody",
                $"SELECT * FROM {_tableWithReadOnlyFields } WHERE [id] = 2 AND [book_name] = 'Best seller' " +
                $"AND [copies_sold] = 100 AND [last_sold_on] is NULL AND [last_sold_on_date] is NULL AND [row_version] is NOT NULL " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOneUpdateStocksViewSelected",
                $"SELECT [categoryid], [pieceid], [categoryName], [piecesAvailable] " +
                $"FROM {_simple_subset_stocks} WHERE categoryid = 2 AND pieceid = 1 " +
                $"AND [categoryName] = 'Historical' AND [piecesAvailable] = 0 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOne_Update_Nulled_Test",
                $"SELECT [categoryid], [pieceid], [categoryName],[piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE [categoryid] = 1 AND [pieceid] = 1 AND [categoryName] = 'SciFi' " +
                $"AND [piecesAvailable] is NULL AND [piecesRequired] = 0 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOne_Insert_PKAutoGen_Test",
                $"INSERT INTO { _integrationTableName } " +
                $"(id, title, publisher_id)" +
                $"VALUES (1000,'The Hobbit Returns to The Shire',1234)"
            },
            {
                "PatchOneUpdateAccessibleRowWithSecPolicy",
                $"SELECT [id], [revenue], [category], [accessible_role] FROM { _tableWithSecurityPolicy } " +
                $"WHERE [id] = 1 AND [revenue] = 2000 AND [category] = 'Book' AND [accessible_role] = 'Anonymous' " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOneUpdateInTableWithAutoGenPKAndTrigger",
                $"SELECT * FROM { _autogenPKTableWithTrigger } " +
                $"WHERE [id] = 1 AND [salary] = 0 AND [u_id] = 2 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOneUpdateInTableWithNonAutoGenPKAndTrigger",
                $"SELECT * FROM { _nonAutogenPKTableWithTrigger } " +
                $"WHERE [id] = 1 AND [months] = 3 AND [salary] = 50 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOneInsertInTableWithNonAutoGenPKAndTrigger",
                $"SELECT * FROM { _nonAutogenPKTableWithTrigger } " +
                $"WHERE [id] = 3 AND [months] = 2 AND [salary] = 30 AND [name] = 'Paris' " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOne_Update_NoReadTest",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE 0 = 1 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "Patch_Update_WithExcludeFieldsTest",
                $"SELECT [id], [title] FROM { _integrationTableName } " +
                $"WHERE id = 8 AND [title] = 'Heart of Darkness' " +
                $"AND [publisher_id] = 2324 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchInsert_NoReadTest",
                $"SELECT [categoryid], [pieceid], [categoryName],[piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE 0 = 1 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "Patch_Insert_WithExcludeFieldsTest",
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
        public async Task PatchOneViewBadRequestTest()
        {
            string expectedErrorMessage = $"View or function '{_defaultSchemaName}.{_composite_subset_bookPub}' is not updatable " +
                                           "because the modification affects multiple base tables.";
            await base.PatchOneViewBadRequestTest(expectedErrorMessage);
        }

        /// <summary>
        /// Test to validate successful execution of a request when a rowversion field is missing from the request body.
        /// </summary>
        [TestMethod]
        public async Task PatchOneWithRowversionFieldMissingFromRequestBody()
        {
            // Validate successful execution of a PATCH update when a rowversion field (here 'row_version')
            // is missing from the request body. A PATCH update request should not try to update any field which is not provided in the request body.
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
                    sqlQuery: GetQuery("PatchOneUpdateWithRowversionFieldMissingFromRequestBody"),
                    operationType: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK
                );

            // Validate successful execution of a PATCH insert when a rowversion field (here 'row_version')
            // is missing from the request body. Successful execution of the PATCH request confirms that we did not
            // attempt to NULL out the 'row_version' field while inserting the record. Had DAB attempted to NULL out the 'row_version' field,
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
                    sqlQuery: GetQuery("PatchOneInsertWithRowversionFieldMissingFromRequestBody"),
                    operationType: EntityActionOperation.UpsertIncremental,
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
        public virtual async Task PatchOneWithRowversionFieldInRequestBody()
        {
            // Validate that a BadRequest exception is thrown for a PATCH update when a rowversion field is included in request body.
            string requestBody = @"
            {
                ""row_version"": null
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/1",
                queryString: string.Empty,
                entityNameOrPath: _entityWithReadOnlyFields,
                sqlQuery: string.Empty,
                operationType: EntityActionOperation.UpsertIncremental,
                exceptionExpected: true,
                requestBody: requestBody,
                expectedErrorMessage: "Field 'row_version' cannot be included in the request body.",
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
                );

            // Validate that a BadRequest exception is thrown for a PATCH insert when a rowversion field is included in request body.
            requestBody = @"
            {
                ""row_version"": null
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/2",
                queryString: string.Empty,
                entityNameOrPath: _entityWithReadOnlyFields,
                sqlQuery: string.Empty,
                operationType: EntityActionOperation.UpsertIncremental,
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
        /// after the trigger is executed, we use the new values (written by the trigger) in the WHERE predicates of the verifying sql query.
        /// </summary>
        [TestMethod]
        public async Task PatchOneUpdateInTableWithUpdateTrigger()
        {
            // Validate that PATCH operation (resulting in update) succeeds when an update DML trigger is configured for a table
            // with autogenerated primary key. Given input item with salary: -9, the selection would return salary = 0.
            // Thus confirming that we return the data being updated by the trigger where, the trigger behavior is that it
            // updates the salary to max(0,min(150,salary)).
            string requestBody = @"
            {
                ""salary"": -9
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/1/u_id/2",
                queryString: null,
                entityNameOrPath: _autogenPKEntityWithTrigger,
                sqlQuery: GetQuery("PatchOneUpdateInTableWithAutoGenPKAndTrigger"),
                operationType: EntityActionOperation.UpsertIncremental,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.OK
            );

            // Validate that PATCH operation (resulting in update) succeeds when an update DML trigger is enabled for a table
            // with non-autogenerated primary key. Given input item with salary: 100, the selection would return salary = 100.
            // Thus confirming that we return the data being updated by the trigger where, the trigger behavior is that it
            // updates the salary to max(0,min(50,salary)).
            requestBody = @"
            {
                ""salary"": 100
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/1/months/3",
                queryString: null,
                entityNameOrPath: _nonAutogenPKEntityWithTrigger,
                sqlQuery: GetQuery("PatchOneUpdateInTableWithNonAutoGenPKAndTrigger"),
                operationType: EntityActionOperation.UpsertIncremental,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.OK
            );
        }

        /// <summary>
        /// Test to validate that even when an insert DML trigger is enabled on a table, we still return the
        /// latest data (values written by trigger). To validate that the data is returned after the trigger is executed,
        /// we use the new values (written by the trigger) in the WHERE predicates of the verifying sql query.
        /// </summary>
        [TestMethod]
        public async Task PatchOneInsertInTableWithInsertTrigger()
        {
            // Validate that PATCH operation (resulting in insert) succeeds when an insert DML trigger is enabled for a table
            // with non-autogenerated primary key. Given input item with salary: 100, the selection would return salary = 30.
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
                sqlQuery: GetQuery("PatchOneInsertInTableWithNonAutoGenPKAndTrigger"),
                operationType: EntityActionOperation.UpsertIncremental,
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

        /// <inheritdoc/>
        [TestMethod]
        public override async Task PatchOneUpdateTestOnTableWithSecurityPolicy()
        {
            string requestBody = @"
            {
                ""revenue"": ""2000""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "id/1",
                    queryString: null,
                    entityNameOrPath: _entityWithSecurityPolicy,
                    sqlQuery: GetQuery("PatchOneUpdateAccessibleRowWithSecPolicy"),
                    operationType: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK
                );
        }

        #endregion
    }
}
