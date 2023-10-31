// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests
{
    /// <summary>
    /// Class containing integration tests for MsSql- to validate scenarios when we operate in non-strict mode for REST request body,
    /// i.e. we allow extraneous fields to be present in the request body.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class MsSqlRestBodyNonStrictModeTests : RestBodyNonStrictModeTests
    {
        private static Dictionary<string, string> _queryMap = new()
        {
            {
                "InsertOneWithExtraneousFieldsInRequestBody",
                $"SELECT [categoryid], [pieceid], [categoryName],[piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE [categoryid] = 3 AND [pieceid] = 1 AND [categoryName] = 'SciFi' " +
                $"AND [piecesAvailable] is NULL AND [piecesRequired] = 1 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "InsertOneWithReadOnlyFieldsInRequestBody",
                $"SELECT * FROM {_tableWithReadOnlyFields } WHERE [id] = 2 AND [book_name] = 'Harry Potter' AND [copies_sold] = 50 AND " +
                $"[last_sold_on] = '1999-01-08 10:23:54' AND [last_sold_on_date] = '1999-01-08 10:23:54' " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "InsertOneWithRowversionFieldInRequestBody",
                $"SELECT * FROM {_tableWithReadOnlyFields } WHERE [id] = 2 AND [book_name] = 'Another Awesome Book' " +
                $"AND [copies_sold] = 100 AND [last_sold_on] = '2023-08-28 12:36:08.8666667' " +
                $"AND [last_sold_on_date] = '2023-08-28 12:36:08.8666667' AND [row_version] is NOT NULL " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PutOneWithExtraneousFieldsInRequestBody",
                $"SELECT [categoryid], [pieceid], [categoryName], [piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE [categoryid] = 2 AND [pieceid] = 1 AND [categoryName] = 'SciFi' " +
                $"AND [piecesAvailable] = 10  AND [piecesRequired] = 5 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PutOneUpdateWithComputedFieldInRequestBody",
                $"SELECT * FROM { _tableWithReadOnlyFields } " +
                $"WHERE [id] = 1 AND [book_name] = 'New book' AND [copies_sold] = 101 AND [last_sold_on] is NULL AND [last_sold_on_date] is NULL " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PutOneInsertWithComputedFieldInRequestBody",
                $"SELECT * FROM { _tableWithReadOnlyFields } " +
                $"WHERE [id] = 2 AND [book_name] = 'New book' AND [copies_sold] = 101 AND " +
                $"[last_sold_on] = '1999-01-08 10:23:54' AND [last_sold_on_date] = '1999-01-08 10:23:54' " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PutOneUpdateWithRowversionFieldInRequestBody",
                $"SELECT * FROM {_tableWithReadOnlyFields } WHERE [id] = 1 AND [book_name] = 'Another Awesome Book' " +
                $"AND [copies_sold] = 100 AND [last_sold_on] is NULL AND [last_sold_on_date] is NULL AND [row_version] is NOT NULL " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PutOneInsertWithRowversionFieldInRequestBody",
                $"SELECT * FROM {_tableWithReadOnlyFields } WHERE [id] = 2 AND [book_name] = 'Best seller' " +
                $"AND [copies_sold] = 100 AND [last_sold_on] is NULL AND [last_sold_on_date] is NULL AND [row_version] is NOT NULL " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOneWithExtraneousFieldsInRequestBody",
                $"SELECT [categoryid], [pieceid], [categoryName],[piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE [categoryid] = 1 AND [pieceid] = 1 AND [categoryName] = 'SciFi' " +
                $"AND [piecesAvailable] is NULL AND [piecesRequired] = 0 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOneUpdateWithComputedFieldInRequestBody",
                $"SELECT * FROM { _tableWithReadOnlyFields } " +
                $"WHERE [id] = 1 AND [book_name] = 'New book' AND [copies_sold] = 50 " +
                $"AND [last_sold_on] is not NULL AND [last_sold_on_date] is not NULL " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOneInsertWithComputedFieldInRequestBody",
                $"SELECT * FROM { _tableWithReadOnlyFields } " +
                $"WHERE [id] = 3 AND [book_name] = 'New book' AND [copies_sold] = 50 AND " +
                $"[last_sold_on] = '1999-01-08 10:23:54' AND [last_sold_on_date] = '1999-01-08 10:23:54' " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOneUpdateWithRowversionFieldInRequestBody",
                $"SELECT * FROM {_tableWithReadOnlyFields } WHERE [id] = 1 AND [book_name] = 'Another Awesome Book' " +
                $"AND [copies_sold] = 100 AND [last_sold_on] is NULL AND [row_version] is NOT NULL " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOneInsertWithRowversionFieldInRequestBody",
                $"SELECT * FROM {_tableWithReadOnlyFields } WHERE [id] = 2 AND [book_name] = 'Best seller' " +
                $"AND [copies_sold] = 100 AND [last_sold_on] is NULL AND [last_sold_on_date] is NULL AND [row_version] is NOT NULL " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "InsertOneAndReturnSingleRowWithStoredProcedureWithExtraneousFieldInRequestBody",
                // This query attempts retrieval of the stored procedure insert operation result,
                // and is explicitly not representative of the engine generated insert statement.
                $"SELECT table0.[id], table0.[title], table0.[publisher_id] FROM books AS table0 " +
                $"JOIN (SELECT id FROM publishers WHERE name = 'The First Publisher') AS table1 " +
                $"ON table0.[publisher_id] = table1.[id] " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
        };

        [ClassInitialize]
        public static async Task SetupDatabaseAsync(TestContext TestContext)
        {
            DatabaseEngine = TestCategory.MSSQL;

            // Set rest.request-body-strict = false to simulate scenario when we operate in non-strict mode for fields in request body.
            await InitializeTestFixture(isRestBodyStrict: false);
        }

        [TestCleanup]
        public async Task TestCleanup()
        {
            await ResetDbStateAsync();
        }

        public override string GetQuery(string key)
        {
            return _queryMap[key];
        }

        /// <summary>
        /// Test to validate that rowversion fields are allowed in request body for PUT operation
        /// when we operate non-strict mode for REST request body i.e. runtime.rest.request-body-strict = false.
        /// </summary>
        [TestMethod]
        public async Task InsertOneWithRowversionFieldInRequestBody()
        {
            // Validate successful execution of a POST request when a rowversion field (here 'row_version')
            // is included in the request body. Successful execution of the POST request confirms that we did not
            // attempt to provide a value for the 'row_version' field. Had DAB provided a value for 'row_version' field,
            // we would have got a BadRequest exception as we cannot provide a value for a field with sql server type of 'rowversion'.
            string requestBody = @"
            {
                ""id"": 2,
                ""book_name"": ""Another Awesome Book"",
                ""copies_sold"": 100,
                ""last_sold_on"": ""2023-08-28 12:36:08.8666667"",
                ""row_version"": ""AAAAAAAHgqw=""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: null,
                    queryString: null,
                    entityNameOrPath: _entityWithReadOnlyFields,
                    sqlQuery: GetQuery("InsertOneWithRowversionFieldInRequestBody"),
                    operationType: EntityActionOperation.Insert,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.Created,
                    expectedLocationHeader: string.Empty
                );
        }

        /// <summary>
        /// Test to validate that rowversion fields are allowed in request body for PATCH operation
        /// when we operate non-strict mode for REST request body i.e. runtime.rest.request-body-strict = false.
        /// Successful execution of the PATCH request confirms that we did not attempt to provide a value for the 'row_version' field.
        /// Had DAB provided a value for 'row_version' field, we would have got a BadRequest exception as we cannot provide a value
        /// for a field with sql server type of 'rowversion'.
        /// </summary>
        [TestMethod]
        public async Task PatchOneWithRowversionFieldInRequestBody()
        {
            // Validate successful execution of a PATCH update when a rowversion field (here 'row_version')
            // is included in the request body.
            string requestBody = @"
            {
                ""book_name"": ""Another Awesome Book"",
                ""copies_sold"": 100,
                ""last_sold_on"": null,
                ""row_version"": ""AAAAAAAHgqw=""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: $"id/1",
                    queryString: null,
                    entityNameOrPath: _entityWithReadOnlyFields,
                    sqlQuery: GetQuery("PatchOneUpdateWithRowversionFieldInRequestBody"),
                    operationType: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK
                );

            // Validate successful execution of a PATCH insert when a rowversion field (here 'row_version')
            // is missing from the request body.
            requestBody = @"
            {
                ""book_name"": ""Best seller"",
                ""copies_sold"": 100,
                ""last_sold_on"": null,
                ""row_version"": ""AAAAAAAHgqw=""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: $"id/2",
                    queryString: null,
                    entityNameOrPath: _entityWithReadOnlyFields,
                    sqlQuery: GetQuery("PatchOneInsertWithRowversionFieldInRequestBody"),
                    operationType: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.Created,
                    expectedLocationHeader: string.Empty
                );
        }

        /// <summary>
        /// Test to validate that rowversion fields are allowed in request body for PUT operation
        /// when we operate non-strict mode for REST request body i.e. runtime.rest.request-body-strict = false.
        /// Successful execution of the PUT request confirms that we did not attempt to NULL out the 'row_version' field.
        /// Had DAB attempted to NULL out the 'row_version' field, we would have got an exception as we cannot provide
        /// a value for a field with sql server type of 'rowversion'.
        /// </summary>
        [TestMethod]
        public async Task PutOneWithRowversionFieldInRequestBody()
        {
            // Validate successful execution of a PUT update when a rowversion field (here 'row_version')
            // is missing from the request body.
            string requestBody = @"
            {
                ""book_name"": ""Another Awesome Book"",
                ""copies_sold"": 100,
                ""last_sold_on"": null,
                ""row_version"": ""AAAAAAAHgqw=""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: $"id/1",
                    queryString: null,
                    entityNameOrPath: _entityWithReadOnlyFields,
                    sqlQuery: GetQuery("PutOneUpdateWithRowversionFieldInRequestBody"),
                    operationType: EntityActionOperation.Upsert,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK
                );

            // Validate successful execution of a PUT insert when a rowversion field (here 'row_version')
            // is missing from the request body.
            requestBody = @"
            {
                ""book_name"": ""Best seller"",
                ""copies_sold"": 100,
                ""row_version"": ""AAAAAAAHgqw="",
                ""last_sold_on"": null
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: $"id/2",
                    queryString: null,
                    entityNameOrPath: _entityWithReadOnlyFields,
                    sqlQuery: GetQuery("PutOneInsertWithRowversionFieldInRequestBody"),
                    operationType: EntityActionOperation.Upsert,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.Created,
                    expectedLocationHeader: string.Empty
                );
        }

        /// <summary>
        /// Test to validate that extraneous fields are allowed in request body for Execute operation for stored procedures
        /// when we operate in runtime.rest.request-body-strict = false.
        /// </summary>
        [TestMethod]
        public async Task ExecuteStoredProcedureWithExtraneousFieldInRequestBody()
        {
            string requestBody = @"
            {
                ""title"": ""Happy New Year"",
                ""publisher_name"": ""The First Publisher"",
                ""extra_field"": ""Some value""
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: null,
                queryString: null,
                entityNameOrPath: _integrationProcedureInsertOneAndDisplay_EntityName,
                sqlQuery: GetQuery("InsertOneAndReturnSingleRowWithStoredProcedureWithExtraneousFieldInRequestBody"),
                operationType: EntityActionOperation.Execute,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.Created,
                expectedLocationHeader: string.Empty,
                expectJson: true
            );
        }
    }
}
