// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests.Put
{
    /// <summary>
    /// Test PUT REST Api validating expected results are obtained.
    /// </summary>
    [TestClass]
    public abstract class PutApiTestBase : RestApiTestBase
    {
        public abstract string GetUniqueDbErrorMessage();

        #region Positive Tests

        /// <summary>
        /// Tests the PutOne functionality with a REST PUT request
        /// with item that exists, resulting in potentially destructive update.
        /// </summary>
        [TestMethod]
        public virtual async Task PutOne_Update_Test()
        {
            string requestBody = @"
            {
                ""title"": ""The Hobbit Returns to The Shire"",
                ""publisher_id"": 1234
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "id/7",
                    queryString: null,
                    entityNameOrPath: _integrationEntityName,
                    sqlQuery: GetQuery(nameof(PutOne_Update_Test)),
                    operationType: EntityActionOperation.Upsert,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK
                );

            requestBody = @"
            {
                ""content"": ""Good book to read""
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: $"book_id/1/id/568",
                queryString: null,
                entityNameOrPath: _entityWithCompositePrimaryKey,
                sqlQuery: GetQuery("PutOne_Update_Default_Test"),
                operationType: EntityActionOperation.Upsert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.OK
                );

            requestBody = @"
            {
               ""categoryName"":""SciFi"",
               ""piecesAvailable"":""10"",
               ""piecesRequired"":""5""
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: $"categoryid/2/pieceid/1",
                queryString: null,
                entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                sqlQuery: GetQuery("PutOne_Update_CompositeNonAutoGenPK_Test"),
                operationType: EntityActionOperation.Upsert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.OK
                );

            // Perform a PUT UPDATE which nulls out a missing field from the request body
            // which is nullable.
            requestBody = @"
            {
                ""categoryName"":""SciFi"",
                ""piecesRequired"":""5""
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: $"categoryid/1/pieceid/1",
                queryString: null,
                entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                sqlQuery: GetQuery("PutOne_Update_NullOutMissingField_Test"),
                operationType: EntityActionOperation.Upsert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.OK
            );

            requestBody = @"
            {
               ""categoryName"":"""",
               ""piecesAvailable"":""2"",
               ""piecesRequired"":""3""
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: $"categoryid/2/pieceid/1",
                queryString: null,
                entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                sqlQuery: GetQuery("PutOne_Update_Empty_Test"),
                operationType: EntityActionOperation.Upsert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.OK
                );
        }

        /// <summary>
        /// Tests that for a successful PUT API request, the response returned takes into account that no read action is configured for the role.
        /// Since, the role has no read action configured, an empty response is expected.
        /// </summary>
        [TestMethod]
        public virtual async Task PutOne_Update_WithNoReadAction_Test()
        {
            string requestBody = @"
            {
                ""title"": ""The Hobbit Returns to The Shire"",
                ""publisher_id"": 1234
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "id/7",
                    queryString: null,
                    entityNameOrPath: _integrationEntityName,
                    sqlQuery: GetQuery(nameof(PutOne_Update_WithNoReadAction_Test)),
                    operationType: EntityActionOperation.Upsert,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK,
                    clientRoleHeader: "test_role_with_noread"
                );
        }

        /// <summary>
        /// Tests that for a successful PUT API request, the response returned takes into account the include and exclude fields configured for the read action.
        /// The read action for the role excludes the publisher_id field. So, the response is expected to contain only the id and title fields.
        /// </summary>
        [TestMethod]
        public virtual async Task PutOne_Update_WithExcludeFields_Test()
        {
            string requestBody = @"
            {
                ""title"": ""The Hobbit Returns to The Shire"",
                ""publisher_id"": 1234
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "id/7",
                    queryString: null,
                    entityNameOrPath: _integrationEntityName,
                    sqlQuery: GetQuery(nameof(PutOne_Update_WithExcludeFields_Test)),
                    operationType: EntityActionOperation.Upsert,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK,
                    clientRoleHeader: "test_role_with_excluded_fields"
                );
        }

        /// <summary>
        /// Tests that for a successful PUT API request, the response returned takes into account the database policy configured for the read action.
        /// policy_tester_excludefields_dbpolicy role has a database policy configured for read action that prevents the retrieval of rows with title = Test.
        /// Since, this test updates the title to Test, an empty response body is expected.
        /// </summary>
        [TestMethod]
        public virtual async Task PutOne_Update_WithReadDbPolicy_Test()
        {
            string requestBody = @"
            {
                ""title"": ""Test"",
                ""publisher_id"": 1234
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "id/7",
                    queryString: null,
                    entityNameOrPath: _integrationEntityName,
                    sqlQuery: GetQuery(nameof(PutOne_Update_WithNoReadAction_Test)),
                    operationType: EntityActionOperation.Upsert,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK,
                    clientRoleHeader: "test_role_with_policy_excluded_fields"
                );
        }

        /// <summary>
        /// Tests that for a successful PUT API request, the response returned takes into account the database policy configured for the read action.
        /// policy_tester_excludefields_dbpolicy role has a database policy configured for read action that prevents the retrieval of rows with title = Test.
        /// Since, this test updates the title to a different value, a non-empty response body is expected.
        /// Also, since the role excludes the publisher_id field, response should not contain the publisher_id field.
        /// </summary>
        [TestMethod]
        public virtual async Task PutOne_Update_WithReadDbPolicyUnsatisfied_Test()
        {
            string requestBody = @"
            {
                ""title"": ""The Hobbit Returns to The Shire"",
                ""publisher_id"": 1234
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "id/7",
                    queryString: null,
                    entityNameOrPath: _integrationEntityName,
                    sqlQuery: GetQuery(nameof(PutOne_Update_WithExcludeFields_Test)),
                    operationType: EntityActionOperation.Upsert,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK,
                    clientRoleHeader: "test_role_with_policy_excluded_fields"
                );
        }

        /// <summary>
        /// Tests that the PUT updates can only update the rows which are accessible after applying the
        /// security policy which uses data from session context.
        /// </summary>
        [TestMethod]
        public virtual Task PutOneUpdateTestOnTableWithSecurityPolicy()
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Tests the PutOne functionality with a REST PUT request using
        /// headers that include as a key "If-Match" with an item that does exist,
        /// resulting in an update occuring. We then verify that the update occurred.
        /// </summary>
        [TestMethod]
        public virtual async Task PutOne_Update_IfMatchHeaders_Test()
        {
            Dictionary<string, StringValues> headerDictionary = new();
            headerDictionary.Add("If-Match", "*");
            string requestBody = @"
            {
                ""title"": ""The Return of the King"",
                ""publisher_id"": 1234
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "id/1",
                    queryString: null,
                    entityNameOrPath: _integrationEntityName,
                    sqlQuery: GetQuery(nameof(PutOne_Update_IfMatchHeaders_Test)),
                    operationType: EntityActionOperation.Upsert,
                    headers: new HeaderDictionary(headerDictionary),
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK
                );
        }

        /// <summary>
        /// Test to validate successful execution of PUT operation which satisfies the database policy for the insert operation it resolves into.
        /// </summary>
        [TestMethod]
        public virtual async Task PutOneInsertWithDatabasePolicy()
        {
            // PUT operation resolves to insert because we don't have a record present for given PK.
            // Since the database policy for insert operation ("@item.pieceid ne 6 and @item.piecesAvailable gt 0") is satisfied by the operation, it executes successfully.
            string requestBody = @"
            {
                ""piecesAvailable"": 4,
                ""categoryName"": ""SciFi""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "categoryid/0/pieceid/7",
                    queryString: null,
                    operationType: EntityActionOperation.Upsert,
                    entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                    sqlQuery: GetQuery("PutOneInsertWithDatabasePolicy"),
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.Created,
                    clientRoleHeader: "database_policy_tester",
                    expectedLocationHeader: string.Empty
                );
        }

        /// <summary>
        /// Test to validate successful execution of PUT operation which satisfies the database policy for the update operation it resolves into.
        /// </summary>
        [TestMethod]
        public virtual async Task PutOneUpdateWithDatabasePolicy()
        {
            // PUT operation resolves to update because we have a record present for given PK.
            // Since the database policy for update operation ("@item.pieceid ne 1") is satisfied by the operation, it executes successfully.
            string requestBody = @"
            {
                ""piecesAvailable"": 4,
                ""piecesRequired"": 5,
                ""categoryName"": ""SciFi""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "categoryid/100/pieceid/99",
                    queryString: null,
                    entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                    sqlQuery: GetQuery("PutOneUpdateWithDatabasePolicy"),
                    operationType: EntityActionOperation.Upsert,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK,
                    clientRoleHeader: "database_policy_tester"
                );
        }

        /// <summary>
        /// Tests the PutOne functionality with a REST PUT request
        /// with item that does NOT exist, results in an insert with
        /// the specified ID as table does NOT have Identity() PK column.
        /// </summary>
        [TestMethod]
        public virtual async Task PutOne_Insert_Test()
        {
            string requestBody = @"
            {
                ""title"": ""Batman Returns"",
                ""issue_number"": 1234
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: $"id/{STARTING_ID_FOR_TEST_INSERTS}",
                    queryString: null,
                    entityNameOrPath: _integration_NonAutoGenPK_EntityName,
                    sqlQuery: GetQuery(nameof(PutOne_Insert_Test)),
                    operationType: EntityActionOperation.Upsert,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.Created,
                    expectedLocationHeader: string.Empty
                );

            // It should result in a successful insert,
            // where the nullable field 'issue_number' is properly left alone by the query validation methods.
            // The request body doesn't contain this field that neither has a default
            // nor is autogenerated.
            requestBody = @"
            {
                ""title"": ""Times""
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: $"id/{STARTING_ID_FOR_TEST_INSERTS + 1}",
                queryString: null,
                entityNameOrPath: _integration_NonAutoGenPK_EntityName,
                sqlQuery: GetQuery("PutOne_Insert_Nullable_Test"),
                operationType: EntityActionOperation.Upsert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.Created,
                expectedLocationHeader: string.Empty
                );

            // It should result in a successful insert,
            // where the autogen'd field 'volume' is properly populated by the db.
            // The request body doesn't contain this non-nullable, non primary key
            // that is autogenerated.
            requestBody = @"
            {
                ""title"": ""Star Trek"",
                ""categoryName"" : ""Suspense""
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: $"id/{STARTING_ID_FOR_TEST_INSERTS}",
                queryString: null,
                entityNameOrPath: _integration_AutoGenNonPK_EntityName,
                sqlQuery: GetQuery("PutOne_Insert_AutoGenNonPK_Test"),
                operationType: EntityActionOperation.Upsert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.Created,
                expectedLocationHeader: string.Empty
                );

            requestBody = @"
            {
               ""categoryName"":""SciFi"",
               ""piecesAvailable"":""2"",
               ""piecesRequired"":""1""
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: $"categoryid/3/pieceid/1",
                queryString: null,
                entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                sqlQuery: GetQuery("PutOne_Insert_CompositeNonAutoGenPK_Test"),
                operationType: EntityActionOperation.Upsert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.Created,
                expectedLocationHeader: string.Empty
                );

            requestBody = @"
            {
               ""categoryName"":""SciFi""
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: $"categoryid/8/pieceid/1",
                queryString: null,
                entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                sqlQuery: GetQuery("PutOne_Insert_Default_Test"),
                operationType: EntityActionOperation.Upsert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.Created,
                expectedLocationHeader: string.Empty
                );

            requestBody = @"
            {
               ""categoryName"":"""",
               ""piecesAvailable"":""2"",
               ""piecesRequired"":""3""
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: $"categoryid/4/pieceid/1",
                queryString: null,
                entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                sqlQuery: GetQuery("PutOne_Insert_Empty_Test"),
                operationType: EntityActionOperation.Upsert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.Created,
                expectedLocationHeader: string.Empty
                );
        }

        /// <summary>
        /// Tests successful execution of PUT insert requests which try to
        /// modify fields belonging to one base table in the view.
        /// </summary>
        [TestMethod]
        public virtual async Task PutOneInsertInViewTest()
        {
            // PUT insertion on a simple view based on one table where not
            // all the fields from base table are selected in view.
            // The missing field has to be nullable or has default value.
            string requestBody = @"
            {
               ""categoryName"": ""SciFi""
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "categoryid/4/pieceid/1",
                queryString: null,
                entityNameOrPath: _simple_subset_stocks,
                sqlQuery: GetQuery("PutOneInsertInStocksViewSelected"),
                operationType: EntityActionOperation.Upsert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.Created
                );
        }

        /// <summary>
        /// Tests the PutOne functionality with a REST PUT request
        /// with a nullable column specified as NULL.
        /// The test should pass successfully for update as well as insert.
        /// </summary>
        [TestMethod]
        public virtual async Task PutOne_Nulled_Test()
        {
            // Performs a successful PUT insert when a nullable column
            // is specified as null in the request body.
            string requestBody = @"
            {
                ""categoryName"": ""SciFi"",
                ""piecesAvailable"": null,
                ""piecesRequired"": ""4""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: $"categoryid/4/pieceid/1",
                    queryString: null,
                    entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                    sqlQuery: GetQuery("PutOne_Insert_Nulled_Test"),
                    operationType: EntityActionOperation.Upsert,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.Created,
                    expectedLocationHeader: string.Empty
                );

            // Performs a successful PUT update when a nullable column
            // is specified as null in the request body.
            requestBody = @"
            {
               ""categoryName"":""Tales"",
               ""piecesAvailable"":null,
               ""piecesRequired"":""4""
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: $"categoryid/2/pieceid/1",
                queryString: null,
                entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                sqlQuery: GetQuery("PutOne_Update_Nulled_Test"),
                operationType: EntityActionOperation.Upsert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.OK
                );
        }

        /// <summary>
        /// Test to validate successful execution of a request when a computed field is missing from the request body.
        /// In such a case, we don't attempt to NULL out computed field (as per PUT semantics) but instead skip updating/inserting the field. 
        /// </summary>
        [TestMethod]
        public virtual async Task PutOneWithComputedFieldMissingFromRequestBody()
        {
            // Validate successful execution of a PUT update when a computed field (here 'last_sold_on_update')
            // is missing from the request body. Successful execution of the PUT request confirms that we did not
            // attempt to NULL out the 'last_sold_on_update' field.
            string requestBody = @"
            {
                ""book_name"": ""New book"",
                ""copies_sold"": 101,
                ""last_sold_on"": ""2023-09-12 05:30:30""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: $"id/1",
                    queryString: null,
                    entityNameOrPath: _entityWithReadOnlyFields,
                    sqlQuery: GetQuery("PutOneUpdateWithComputedFieldMissingFromRequestBody"),
                    operationType: EntityActionOperation.Upsert,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK
                );

            // Validate successful execution of a PUT insert when a computed field (here 'last_sold_on_update')
            // is missing from the request body. Successful execution of the PUT request confirms that we did not
            // attempt to NULL out the 'last_sold_on_update' field.
            requestBody = @"
            {
                ""book_name"": ""New book"",
                ""copies_sold"": 101
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: $"id/2",
                    queryString: null,
                    entityNameOrPath: _entityWithReadOnlyFields,
                    sqlQuery: GetQuery("PutOneInsertWithComputedFieldMissingFromRequestBody"),
                    operationType: EntityActionOperation.Upsert,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.Created,
                    expectedLocationHeader: string.Empty
                );
        }

        /// <summary>
        /// Tests successful execution of PUT update requests which try to
        /// modify fields belonging to one base table in the view.
        /// </summary>
        [TestMethod]
        public virtual async Task PutOneUpdateViewTest()
        {
            // Put update on simple view with subset of fields from base table.
            string requestBody = @"
            {
                ""categoryName"": ""Historical""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "categoryid/2/pieceid/1",
                    queryString: null,
                    entityNameOrPath: _simple_subset_stocks,
                    sqlQuery: GetQuery("PutOneUpdateStocksViewSelected"),
                    operationType: EntityActionOperation.Upsert,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK
                );
        }

        /// <summary>
        /// Tests REST PutOne which results in update with
        /// and entity that has remapped column names.
        /// URI Path: PK of existing record.
        /// Req Body: Valid Parameter with intended update.
        /// Expects: 200 OK where sqlQuery validates update.
        /// </summary>
        [TestMethod]
        public virtual async Task PutOne_Update_With_Mapping_Test()
        {
            string requestBody = @"
            {
                ""Scientific Name"": ""Humulus Lupulus"",
                ""United State's Region"": ""Pacific North West""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "treeId/1",
                    queryString: null,
                    entityNameOrPath: _integrationMappingEntity,
                    sqlQuery: GetQuery(nameof(PutOne_Update_With_Mapping_Test)),
                    operationType: EntityActionOperation.Upsert,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK
                );
        }

        /// <summary>
        /// We try to Sql Inject through the body of an update operation.
        /// If the update happens successfully we know the sql injection
        /// failed.
        /// </summary>
        [DataTestMethod]
        [DataRow(" UNION SELECT * FROM books/*", "UpdateSqlInjectionQuery1")]
        [DataRow("; SELECT * FROM information_schema.tables/*", "UpdateSqlInjectionQuery2")]
        [DataRow("value; SELECT * FROM v$version--", "UpdateSqlInjectionQuery3")]
        [DataRow("value; DROP TABLE authors;", "UpdateSqlInjectionQuery4")]
        public virtual async Task PutOne_Update_SqlInjection_Test(string sqlInjection, string query)
        {
            string requestBody = @"
            {
                ""title"": """ + sqlInjection + @""",
                ""publisher_id"": 1234
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "id/7",
                    queryString: null,
                    entityNameOrPath: _integrationEntityName,
                    sqlQuery: GetQuery(query),
                    operationType: EntityActionOperation.Upsert,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK
                );
        }

        /// <summary>
        /// Test to validate that for a PUT API request that results in a successful insert operation,
        /// the response returned takes into account that no read action is configured for the role.
        /// URI Path: Contains a Non-existent PK.
        /// Req Body: Valid Parameter with intended insert data.
        /// Expects:
        /// Status: 201 Created since the PUT results in an insert operation
        /// Response Body: Empty because the role policy_tester_noread has no read action configured.
        /// </summary>
        [TestMethod]
        public virtual async Task PutInsert_NoReadTest()
        {
            string requestBody = @"
            {
                ""piecesAvailable"": 4,
                ""categoryName"": ""SciFi""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "categoryid/0/pieceid/7",
                    queryString: null,
                    entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                    sqlQuery: GetQuery("PutInsert_NoReadTest"),
                    operationType: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.Created,
                    clientRoleHeader: "test_role_with_noread",
                    expectedLocationHeader: string.Empty
                );
        }

        /// <summary>
        /// Tests that for a PUT API request that results in a successful insert operation,
        /// the response returned takes into account the include and exclude fields configured for the read action.
        /// URI Path: Contains a non-existent PK.
        /// Req Body: Valid Parameter with intended update.
        /// Expects:
        /// Status: 201 Created as PUT results in an insert operation.
        /// Response Body: Does not contain the categoryName field as it is excluded in the read configuration.
        /// </summary>
        [TestMethod]
        public virtual async Task Put_Insert_WithExcludeFieldsTest()
        {
            string requestBody = @"
            {
                ""piecesAvailable"": 4,
                ""categoryName"": ""SciFi"",
                ""piecesRequired"": 4
            }";

            await SetupAndRunRestApiTest(
                   primaryKeyRoute: "categoryid/0/pieceid/7",
                   queryString: null,
                   entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                   sqlQuery: GetQuery("Put_Insert_WithExcludeFieldsTest"),
                   operationType: EntityActionOperation.UpsertIncremental,
                   requestBody: requestBody,
                   expectedStatusCode: HttpStatusCode.Created,
                   clientRoleHeader: "test_role_with_excluded_fields",
                   expectedLocationHeader: string.Empty
               );
        }

        /// <summary>
        /// Tests that for a PUT API request that results in a successful insert operation,
        /// the response returned takes into account the database policy configured for the read action.
        /// URI Path: Contains a non-existent PK.
        /// Req Body: Valid Parameter with intended update.
        /// Expects:
        /// Status: 201 Created as PUT results in an insert operation.
        /// Response Body: Empty. The database policy configured for the read action states that piecesAvailable cannot be 0.
        /// Since, the PUT request inserts a record with piecesAvailable = 0, the response must be empty.
        /// </summary>
        [TestMethod]
        public virtual async Task Put_Insert_WithReadDatabasePolicyTest()
        {
            string requestBody = @"
            {
                ""piecesAvailable"": 0,
                ""categoryName"": ""SciFi"",
                ""piecesRequired"": 4
            }";

            await SetupAndRunRestApiTest(
                   primaryKeyRoute: "categoryid/0/pieceid/7",
                   queryString: null,
                   entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                   sqlQuery: GetQuery("PutInsert_NoReadTest"),
                   operationType: EntityActionOperation.UpsertIncremental,
                   requestBody: requestBody,
                   expectedStatusCode: HttpStatusCode.Created,
                   clientRoleHeader: "test_role_with_policy_excluded_fields",
                   expectedLocationHeader: string.Empty
               );
        }

        /// <summary>
        /// Tests that for a PUT API request that results in a successful insert operation,
        /// the response returned takes into account the database policy and the include/exlude fields
        /// configured for the read action.
        /// URI Path: Contains a non-existent PK.
        /// Req Body: Valid Parameter with intended update.
        /// Expects:
        /// Status: 201 Created as PUT results in an insert operation.
        /// Response Body: Non-empty and should not contain the categoryName. The database policy configured for the read action states that piecesAvailable cannot be 0.
        /// But, the PUT request inserts a record with piecesAvailable = 4, so the policy is unsatisfied. Hence, the response should be non-empty.
        /// The policy also excludes the categoryName field, so the response should not contain the categoryName field.
        /// </summary>
        [TestMethod]
        public virtual async Task Put_Insert_WithReadDatabasePolicyUnsatisfiedTest()
        {
            string requestBody = @"
            {
                ""piecesAvailable"": 4,
                ""categoryName"": ""SciFi"",
                ""piecesRequired"": 4
            }";

            await SetupAndRunRestApiTest(
                   primaryKeyRoute: "categoryid/0/pieceid/7",
                   queryString: null,
                   entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                   sqlQuery: GetQuery("Put_Insert_WithExcludeFieldsTest"),
                   operationType: EntityActionOperation.UpsertIncremental,
                   requestBody: requestBody,
                   expectedStatusCode: HttpStatusCode.Created,
                   clientRoleHeader: "test_role_with_policy_excluded_fields",
                   expectedLocationHeader: string.Empty
               );
        }

        #endregion

        #region Negative Tests

        [TestMethod]
        public virtual async Task PutOneWithNonNullableFieldMissingInJsonBodyTest()
        {
            // Behaviour expected when a non-nullable and non-default field
            // is missing from request body. This would fail in the RequestValidator.ValidateColumn
            // as our requestBody is missing a non-nullable and non-default field.
            string requestBody = @"
            {
                ""piecesRequired"":""6""
            }";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: "categoryid/1/pieceid/1",
                queryString: string.Empty,
                entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                sqlQuery: string.Empty,
                operationType: EntityActionOperation.Upsert,
                requestBody: requestBody,
                exceptionExpected: true,
                expectedErrorMessage: "Invalid request body. Missing field in body: categoryName.",
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: "BadRequest"
            );
        }

        [TestMethod]
        public virtual async Task PutOneUpdateNonNullableDefaultFieldMissingFromJsonBodyTest(
            bool isExpectedErrorMsgSubstr = false)
        {
            // Behaviour expected when a non-nullable but default field
            // is missing from request body. In this case, when we try to null out
            // this field, the db would throw an exception.
            string requestBody = @"
            {
                ""categoryName"":""comics""
            }";
            string expectedErrorMessage = GetUniqueDbErrorMessage();
            await SetupAndRunRestApiTest(
                primaryKeyRoute: "categoryid/1/pieceid/1",
                queryString: string.Empty,
                entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                sqlQuery: string.Empty,
                operationType: EntityActionOperation.Upsert,
                requestBody: requestBody,
                exceptionExpected: true,
                expectedErrorMessage: expectedErrorMessage,
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: $"{DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed}",
                isExpectedErrorMsgSubstr: isExpectedErrorMsgSubstr
            );
        }

        /// <summary>
        /// Tests the PutOne functionality with a REST PUT request
        /// with item that does NOT exist, AND parameters incorrectly match schema, results in BadRequest.
        /// sqlQuery represents the query used to get 'expected' result of zero items.
        /// </summary>
        [TestMethod]
        public virtual async Task PutOne_Insert_BadReq_Test()
        {
            string requestBody = @"
            {
                ""title"": ""The Hobbit Returns to The Shire""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "id/7",
                    queryString: string.Empty,
                    entityNameOrPath: _integrationEntityName,
                    sqlQuery: string.Empty,
                    operationType: EntityActionOperation.Upsert,
                    requestBody: requestBody,
                    exceptionExpected: true,
                    expectedErrorMessage: "Invalid request body. Missing field in body: publisher_id.",
                    expectedStatusCode: HttpStatusCode.BadRequest,
                    expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
                );
        }

        /// <summary>
        /// Tests the PutOne functionality with a REST PUT request
        /// with item that does NOT exist, AND primary key defined is autogenerated in schema,
        /// which results in a BadRequest.
        /// sqlQuery represents the query used to get 'expected' result of zero items.
        /// </summary>
        [TestMethod]
        public virtual async Task PutOne_Insert_PKAutoGen_Test()
        {
            string requestBody = @"
            {
                ""title"": ""The Hobbit Returns to The Shire"",
                ""publisher_id"": 1234
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "id/1000",
                    queryString: string.Empty,
                    entityNameOrPath: _integrationEntityName,
                    sqlQuery: string.Empty,
                    operationType: EntityActionOperation.Upsert,
                    requestBody: requestBody,
                    exceptionExpected: true,
                    expectedErrorMessage: $"Cannot perform INSERT and could not find {_integrationEntityName} with primary key <id: 1000> to perform UPDATE on.",
                    expectedStatusCode: HttpStatusCode.NotFound,
                    expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.ItemNotFound.ToString()
                );
        }

        [TestMethod]
        public virtual async Task PutOne_Insert_CompositePKAutoGen_Test()
        {
            string requestBody = @"
            {
               ""content"":""Great book to read""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: $"id/{STARTING_ID_FOR_TEST_INSERTS + 1}/book_id/1",
                    queryString: string.Empty,
                    entityNameOrPath: _entityWithCompositePrimaryKey,
                    sqlQuery: string.Empty,
                    operationType: EntityActionOperation.Upsert,
                    requestBody: requestBody,
                    exceptionExpected: true,
                    expectedErrorMessage: $"Cannot perform INSERT and could not find {_entityWithCompositePrimaryKey} with primary key <id: 5002, book_id: 1> to perform UPDATE on.",
                    expectedStatusCode: HttpStatusCode.NotFound,
                    expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.ItemNotFound.ToString()
                );
        }
        /// <summary>
        /// Tests the PutOne functionality with a REST PUT request
        /// with item that does NOT exist, AND primary key defined is autogenerated in schema,
        /// which results in a BadRequest.
        /// sqlQuery represents the query used to get 'expected' result of zero items.
        /// </summary>
        [TestMethod]
        public virtual async Task PutOne_Insert_BadReq_NonNullable_Test()
        {
            string requestBody = @"
            {
                ""title"": ""The Hobbit Returns to The Shire""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "id/1000",
                    queryString: string.Empty,
                    entityNameOrPath: _integrationEntityName,
                    sqlQuery: string.Empty,
                    operationType: EntityActionOperation.Upsert,
                    requestBody: requestBody,
                    exceptionExpected: true,
                    expectedErrorMessage: $"Invalid request body. Missing field in body: publisher_id.",
                    expectedStatusCode: HttpStatusCode.BadRequest,
                    expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
                );

            requestBody = @"
            {
                ""piecesAvailable"": ""7""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "categoryid/1/pieceid/1",
                    queryString: string.Empty,
                    entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                    sqlQuery: string.Empty,
                    operationType: EntityActionOperation.Upsert,
                    requestBody: requestBody,
                    exceptionExpected: true,
                    expectedErrorMessage: $"Invalid request body. Missing field in body: categoryName.",
                    expectedStatusCode: HttpStatusCode.BadRequest,
                    expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
                );
        }

        /// <summary>
        /// Tests failure of a PUT on a non-existent item
        /// with the request body containing a non-nullable,
        /// autogenerated field.
        /// </summary>
        public virtual async Task PutOne_Insert_BadReq_AutoGen_NonNullable_Test()
        {
            string requestBody = @"
            {
                ""title"": ""Star Trek"",
                ""volume"": ""1""
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: $"/id/{STARTING_ID_FOR_TEST_INSERTS}",
                queryString: null,
                entityNameOrPath: _integration_AutoGenNonPK_EntityName,
                sqlQuery: string.Empty,
                operationType: EntityActionOperation.Upsert,
                requestBody: requestBody,
                expectedErrorMessage: @"Invalid request body. Either insufficient or extra fields supplied.",
                expectedStatusCode: HttpStatusCode.BadRequest
                );
        }

        /// <summary>
        /// Tests the PutOne functionality with a REST PUT request using
        /// headers that include as a key "If-Match" with an item that does not exist,
        /// resulting in a DataApiBuilderException with status code of Precondition Failed.
        /// </summary>
        [TestMethod]
        public virtual async Task PutOne_Update_IfMatchHeaders_NoUpdatePerformed_Test()
        {
            Dictionary<string, StringValues> headerDictionary = new();
            headerDictionary.Add("If-Match", "*");
            headerDictionary.Add("StatusCode", "200");
            string requestBody = @"
            {
                ""title"": ""The Return of the King"",
                ""publisher_id"": 1234
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "id/24",
                    queryString: string.Empty,
                    entityNameOrPath: _integrationEntityName,
                    sqlQuery: string.Empty,
                    operationType: EntityActionOperation.Upsert,
                    headers: new HeaderDictionary(headerDictionary),
                    requestBody: requestBody,
                    exceptionExpected: true,
                    expectedErrorMessage: "No Update could be performed, record not found",
                    expectedStatusCode: HttpStatusCode.NotFound,
                    expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.EntityNotFound.ToString()
                );
        }

        /// <summary>
        /// Tests the Put functionality with a REST PUT request
        /// without a primary key route. We expect a failure and so
        /// no sql query is provided.
        /// </summary>
        [TestMethod]
        public virtual async Task PutWithNoPrimaryKeyRouteTest()
        {
            string requestBody = @"
            {
                ""title"": ""Batman Returns"",
                ""issue_number"": 1234
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: string.Empty,
                    queryString: null,
                    entityNameOrPath: _integration_NonAutoGenPK_EntityName,
                    sqlQuery: string.Empty,
                    operationType: EntityActionOperation.Upsert,
                    requestBody: requestBody,
                    exceptionExpected: true,
                    expectedErrorMessage: RequestValidator.PRIMARY_KEY_NOT_PROVIDED_ERR_MESSAGE,
                    expectedStatusCode: HttpStatusCode.BadRequest
                );
        }

        /// <summary>
        /// Tests that a cast failure of primary key value type results in HTTP 400 Bad Request.
        /// e.g. Attempt to cast a string '{}' to the 'publisher_id' column type of int will fail.
        /// </summary>
        [TestMethod]
        public async Task PutWithUncastablePKValue()
        {
            string requestBody = @"
            {
                ""title"": ""BookTitle"",
                ""publisher_id"": ""StringFailsToCastToInt""
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/1",
                queryString: string.Empty,
                entityNameOrPath: _integrationEntityName,
                sqlQuery: null,
                operationType: EntityActionOperation.Upsert,
                requestBody: requestBody,
                exceptionExpected: true,
                expectedErrorMessage: "Parameter \"StringFailsToCastToInt\" cannot be resolved as column \"publisher_id\" with type \"Int32\".",
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        /// <summary>
        /// Tests the Put functionality with a REST PUT request
        /// with the request body having null value for non-nullable column
        /// We expect a failure and so no sql query is provided.
        /// </summary>
        [TestMethod]
        public virtual async Task PutOneWithNonNullableFieldAsNull()
        {
            //Negative test case for Put resulting in a failed update
            string requestBody = @"
            {
                ""piecesAvailable"": ""3"",
                ""piecesRequired"": ""1"",
                ""categoryName"":null
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "categoryid/2/pieceid/1",
                queryString: string.Empty,
                entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                sqlQuery: string.Empty,
                operationType: EntityActionOperation.Upsert,
                requestBody: requestBody,
                exceptionExpected: true,
                expectedErrorMessage: "Invalid value for field categoryName in request body.",
                expectedStatusCode: HttpStatusCode.BadRequest
            );

            //Negative test case for Put resulting in a failed insert
            requestBody = @"
            {
                ""piecesAvailable"": ""3"",
                ""piecesRequired"": ""1"",
                ""categoryName"":null
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "categoryid/3/pieceid/1",
                queryString: string.Empty,
                entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                sqlQuery: string.Empty,
                operationType: EntityActionOperation.Upsert,
                requestBody: requestBody,
                exceptionExpected: true,
                expectedErrorMessage: "Invalid value for field categoryName in request body.",
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        /// <summary>
        /// Verifies that we throw an exception when an extraneous field that does not map to a backing column in the table
        /// is provided in the request body for an PUT operation. This test validates the behavior of rest.request-body-strict == true.
        /// Such behavior arises when rest.request-body-strict is:
        /// 1. Included in runtime config (and set to true)
        /// 2. Excluded from runtime config (defaults to true)
        /// </summary>
        [TestMethod]
        public async Task PutOneTestWithExtraneousFieldsInRequestBody()
        {
            // Non-existing field 'categoryName' included in the request body for a table with autogenerated PK.
            string requestBody = @"
            {
                ""title"" : ""New book"",
                ""publisher_id"": 1234,
                ""categoryName"": ""SciFi""
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/1",
                queryString: string.Empty,
                entityNameOrPath: _integrationEntityName,
                sqlQuery: string.Empty,
                operationType: EntityActionOperation.Upsert,
                exceptionExpected: true,
                requestBody: requestBody,
                expectedErrorMessage: "Invalid request body. Either insufficient or extra fields supplied.",
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
                );
        }

        /// <summary>
        /// Test to verify that we throw exception for invalid/bad
        /// PUT requests on views.
        /// </summary>
        [TestMethod]
        public virtual async Task PutOneInViewBadRequest(
            string expectedErrorMessage,
            bool isExpectedErrorMsgSubstr = false)
        {
            // PUT update trying to modify fields from multiple base table
            // will result in error.
            string requestBody = @"
            {
                ""name"": ""new publisher"",
                ""title"": ""new Book""
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/1/pub_id/1234",
                queryString: string.Empty,
                entityNameOrPath: _composite_subset_bookPub,
                sqlQuery: string.Empty,
                operationType: EntityActionOperation.Upsert,
                requestBody: requestBody,
                exceptionExpected: true,
                expectedErrorMessage: expectedErrorMessage,
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed.ToString(),
                isExpectedErrorMsgSubstr: isExpectedErrorMsgSubstr
            );
        }

        /// <summary>
        /// Test to validate failure of PUT operation failing to satisfy the database policy for the operation to be executed
        /// (insert/update based on whether a record exists for given PK).
        /// </summary>
        [TestMethod]
        public virtual async Task PutOneWithUnsatisfiedDatabasePolicy()
        {
            // PUT operation resolves to update because we have a record present for given PK.
            // However, the update fails to execute successfully because the database policy ("@item.pieceid ne 1") for update operation is not satisfied.
            string requestBody = @"
            {
                ""categoryName"": ""SciFi"",
                ""piecesRequired"": 5,
                ""piecesAvailable"": 2
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "categoryid/0/pieceid/1",
                    queryString: null,
                    entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                    operationType: EntityActionOperation.Upsert,
                    requestBody: requestBody,
                    sqlQuery: string.Empty,
                    exceptionExpected: true,
                    expectedErrorMessage: DataApiBuilderException.AUTHORIZATION_FAILURE,
                    expectedStatusCode: HttpStatusCode.Forbidden,
                    expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.DatabasePolicyFailure.ToString(),
                    clientRoleHeader: "database_policy_tester"
                    );

            // PUT operation resolves to insert because we don't have a record present for given PK.
            // However, the insert fails to execute successfully because the database policy ("@item.pieceid ne 6 and @item.piecesAvailable gt 6")
            // for insert operation is not satisfied.
            requestBody = @"
            {
                ""categoryName"": ""SciFi"",
                ""piecesRequired"": 5,
                ""piecesAvailable"": 2
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "categoryid/0/pieceid/6",
                    queryString: null,
                    entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                    operationType: EntityActionOperation.Upsert,
                    requestBody: requestBody,
                    sqlQuery: string.Empty,
                    exceptionExpected: true,
                    expectedErrorMessage: DataApiBuilderException.AUTHORIZATION_FAILURE,
                    expectedStatusCode: HttpStatusCode.Forbidden,
                    expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.DatabasePolicyFailure.ToString(),
                    clientRoleHeader: "database_policy_tester"
                    );
        }

        /// <summary>
        /// Test to validate failure of a request when one or more fields referenced in the database policy for create operation are not provided in the request body.
        /// </summary>
        [TestMethod]
        public virtual async Task PutOneInsertInTableWithFieldsInDbPolicyNotPresentInBody()
        {
            string requestBody = @"
            {
                ""categoryName"":""SciFi""
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "categoryid/100/pieceid/99",
                queryString: string.Empty,
                entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                sqlQuery: string.Empty,
                operationType: EntityActionOperation.Upsert,
                exceptionExpected: true,
                requestBody: requestBody,
                clientRoleHeader: "database_policy_tester",
                expectedErrorMessage: "One or more fields referenced by the database policy are not present in the request.",
                expectedStatusCode: HttpStatusCode.Forbidden,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed.ToString()
                );
        }

        /// <summary>
        /// Test to validate that whenever a computed field is included in the request body, we throw a BadRequest exception
        /// as it is not allowed to provide value (to insert/update) for a computed field.
        /// </summary>
        [TestMethod]
        public virtual async Task PutOneWithComputedFieldInRequestBody()
        {
            // Validate that a BadRequest exception is thrown for a PUT update when a computed field (here 'last_sold_on_date') is included in request body.
            string requestBody = @"
            {
                ""last_sold_on_date"": null
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/1",
                queryString: string.Empty,
                entityNameOrPath: _entityWithReadOnlyFields,
                sqlQuery: string.Empty,
                operationType: EntityActionOperation.Upsert,
                exceptionExpected: true,
                requestBody: requestBody,
                expectedErrorMessage: "Field 'last_sold_on_date' cannot be included in the request body.",
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
                );

            // Validate that a BadRequest exception is thrown for a PUT insert when a computed field (here 'last_sold_on_date') is included in request body.
            requestBody = @"
            {
                ""last_sold_on_date"": null
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/2",
                queryString: string.Empty,
                entityNameOrPath: _entityWithReadOnlyFields,
                sqlQuery: string.Empty,
                operationType: EntityActionOperation.Upsert,
                exceptionExpected: true,
                requestBody: requestBody,
                expectedErrorMessage: "Field 'last_sold_on_date' cannot be included in the request body.",
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
                );
        }
        #endregion
    }
}
