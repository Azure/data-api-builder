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

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests.Patch
{
    /// <summary>
    /// Test PATCH REST Api validating expected results are obtained.
    /// </summary>
    [TestClass]
    public abstract class PatchApiTestBase : RestApiTestBase
    {
        #region Positive Tests

        /// <summary>
        /// Tests the PatchOne functionality with a REST PATCH request
        /// with a nullable column specified as NULL.
        /// The test should pass successfully for update as well as insert.
        /// </summary>
        [TestMethod]
        public virtual async Task PatchOne_Nulled_Test()
        {
            // Performs a successful PATCH insert when a nullable column
            // is specified as null in the request body.
            string requestBody = @"
            {
                ""categoryName"": ""SciFi"",
                ""piecesAvailable"": null,
                ""piecesRequired"": 4
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: $"categoryid/3/pieceid/1",
                    queryString: null,
                    entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                    sqlQuery: GetQuery("PatchOne_Insert_Nulled_Test"),
                    operationType: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.Created,
                    expectedLocationHeader: string.Empty
                );

            // Performs a successful PATCH update when a nullable column
            // is specified as null in the request body.
            requestBody = @"
            {
                ""piecesAvailable"": null
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "categoryid/1/pieceid/1",
                    queryString: null,
                    entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                    sqlQuery: GetQuery("PatchOne_Update_Nulled_Test"),
                    operationType: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK
                );

        }

        /// <summary>
        /// Tests REST PatchOne which results in an insert.
        /// URI Path: PK of record that does not exist.
        /// Req Body: Valid Parameters.
        /// Expects: 201 Created where sqlQuery validates insert.
        /// </summary>
        [TestMethod]
        public virtual async Task PatchOne_Insert_UniqueCharacters_Test()
        {
            string requestBody = @"
            {
                ""始計"": ""Revised Chapter 1 notes: "",
                ""作戰"": ""Revised Chapter 2 notes: "",
                ""謀攻"": ""Revised Chapter 3 notes: ""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: $"┬─┬ノ( º _ ºノ)/2",
                    queryString: null,
                    entityNameOrPath: _integrationUniqueCharactersEntity,
                    sqlQuery: GetQuery(nameof(PatchOne_Insert_UniqueCharacters_Test)),
                    operationType: EntityActionOperation.Upsert,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.Created,
                    expectedLocationHeader: string.Empty
                );
        }

        /// <summary>
        /// Tests REST PatchOne which results in an insert.
        /// URI Path: PK of record that does not exist.
        /// Req Body: Valid Parameters.
        /// Expects: 201 Created where sqlQuery validates insert.
        /// </summary>
        [TestMethod]
        public virtual async Task PatchOne_Insert_NonAutoGenPK_Test()
        {
            string requestBody = @"
            {
                ""title"": ""Batman Begins"",
                ""issue_number"": 1234
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: $"id/2",
                    queryString: null,
                    entityNameOrPath: _integration_NonAutoGenPK_EntityName,
                    sqlQuery: GetQuery(nameof(PatchOne_Insert_NonAutoGenPK_Test)),
                    operationType: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.Created,
                    expectedLocationHeader: string.Empty
                );

            requestBody = @"
            {
                ""categoryName"": ""Tales"",
                ""piecesAvailable"":""5"",
                ""piecesRequired"":""4""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: $"categoryid/4/pieceid/1",
                    queryString: null,
                    entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                    sqlQuery: GetQuery("PatchOne_Insert_CompositeNonAutoGenPK_Test"),
                    operationType: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.Created,
                    expectedLocationHeader: string.Empty
                );

            requestBody = @"
            {
                ""categoryName"": """",
                ""piecesAvailable"":""5"",
                ""piecesRequired"":""4""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: $"categoryid/5/pieceid/1",
                    queryString: null,
                    entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                    sqlQuery: GetQuery("PatchOne_Insert_Empty_Test"),
                    operationType: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.Created,
                    expectedLocationHeader: string.Empty
                );

            requestBody = @"
            {
                ""categoryName"": ""SciFi""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: $"categoryid/7/pieceid/1",
                    queryString: null,
                    entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                    sqlQuery: GetQuery("PatchOne_Insert_Default_Test"),
                    operationType: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.Created,
                    expectedLocationHeader: string.Empty
                );

            // Entity with mapping defined for columns
            requestBody = @"
            {
                ""Scientific Name"": ""Quercus"",
                ""United State's Region"": ""South West""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: $"treeId/4",
                    queryString: null,
                    entityNameOrPath: _integrationMappingEntity,
                    sqlQuery: GetQuery("PatchOne_Insert_Mapping_Test"),
                    operationType: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.Created,
                    expectedLocationHeader: string.Empty
                );
        }

        /// <summary>
        /// Tests to validate that request PATCH requests modifying fields
        /// from one base table in view and resolving to insert operation
        /// execute successfully.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public virtual async Task PatchOneInsertInViewTest()
        {
            // PATCH insert on simple view based on stocks table.
            string requestBody = @"
            {
               ""categoryName"": ""SciFi""
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "categoryid/4/pieceid/1",
                queryString: null,
                entityNameOrPath: _simple_subset_stocks,
                sqlQuery: GetQuery("PatchOneInsertInStocksViewSelected"),
                operationType: EntityActionOperation.UpsertIncremental,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.Created,
                expectedLocationHeader: string.Empty
                );
        }
        /// <summary>
        /// Tests REST PatchOne which results in incremental update
        /// URI Path: PK of existing record.
        /// Req Body: Valid Parameter with intended update.
        /// Expects: 200 OK where sqlQuery validates update.
        /// </summary>
        [TestMethod]
        public virtual async Task PatchOne_Update_Test()
        {
            string requestBody = @"
            {
                ""title"": ""Heart of Darkness""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "id/8",
                    queryString: null,
                    entityNameOrPath: _integrationEntityName,
                    sqlQuery: GetQuery(nameof(PatchOne_Update_Test)),
                    operationType: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK
                );

            requestBody = @"
            {
                ""content"": ""That's a great book""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "id/567/book_id/1",
                    queryString: null,
                    entityNameOrPath: _entityWithCompositePrimaryKey,
                    sqlQuery: GetQuery("PatchOne_Update_Default_Test"),
                    operationType: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK
                );

            requestBody = @"
            {
                ""piecesAvailable"": ""10""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "categoryid/1/pieceid/1",
                    queryString: null,
                    entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                    sqlQuery: GetQuery("PatchOne_Update_CompositeNonAutoGenPK_Test"),
                    operationType: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK
                );

            requestBody = @"
            {
                ""categoryName"": """"

            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "categoryid/1/pieceid/1",
                    queryString: null,
                    entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                    sqlQuery: GetQuery("PatchOne_Update_Empty_Test"),
                    operationType: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK
                );
        }

        /// <summary>
        /// Test to validate successful execution of a request when a computed field is missing from the request body.
        /// </summary>
        [TestMethod]
        public virtual async Task PatchOneWithComputedFieldMissingFromRequestBody()
        {
            // Validate successful execution of a PATCH update when a computed field (here 'last_sold_on_date')
            // is missing from the request body. Successful execution of the PATCH request confirms that we did not
            // attempt to NULL out the 'last_sold_on_update' field.
            string requestBody = @"
            {
                ""book_name"": ""New book"",
                ""copies_sold"": 50
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: $"id/1",
                    queryString: null,
                    entityNameOrPath: _entityWithReadOnlyFields,
                    sqlQuery: GetQuery("PatchOneUpdateWithComputedFieldMissingFromRequestBody"),
                    operationType: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK
                );

            // Validate successful execution of a PATCH insert when a computed field (here 'last_sold_on_date')
            // is missing from the request body. Successful execution of the PATCH request confirms that we did not
            // attempt to NULL out the 'last_sold_on_update' field.
            requestBody = @"
            {
                ""book_name"": ""New book"",
                ""copies_sold"": 50
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: $"id/2",
                    queryString: null,
                    entityNameOrPath: _entityWithReadOnlyFields,
                    sqlQuery: GetQuery("PatchOneInsertWithComputedFieldMissingFromRequestBody"),
                    operationType: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.Created,
                    expectedLocationHeader: string.Empty
                );
        }

        /// <summary>
        /// Tests that the PATCH updates can only update the rows which are accessible after applying the
        /// security policy which uses data from session context.
        /// </summary>
        [TestMethod]
        public virtual Task PatchOneUpdateTestOnTableWithSecurityPolicy()
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Tests successful execution of PATCH update requests on views
        /// when requests try to modify fields belonging to one base table
        /// in the view.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public virtual async Task PatchOneUpdateViewTest()
        {
            // PATCH update on simple view based on stocks table.
            string requestBody = @"
            {
                ""categoryName"": ""Historical""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "categoryid/2/pieceid/1",
                    queryString: null,
                    entityNameOrPath: _simple_subset_stocks,
                    sqlQuery: GetQuery("PatchOneUpdateStocksViewSelected"),
                    operationType: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK
                );
        }
        /// <summary>
        /// Tests the PatchOne functionality with a REST PATCH request using
        /// headers that include as a key "If-Match" with an item that does exist,
        /// resulting in an update occuring. Verify update with Find.
        /// </summary>
        [TestMethod]
        public virtual async Task PatchOne_Update_IfMatchHeaders_Test()
        {
            Dictionary<string, StringValues> headerDictionary = new();
            headerDictionary.Add("If-Match", "*");
            string requestBody = @"
            {
                ""title"": ""The Hobbit Returns to The Shire"",
                ""publisher_id"": 1234
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "id/1",
                    queryString: null,
                    entityNameOrPath: _integrationEntityName,
                    sqlQuery: GetQuery(nameof(PatchOne_Update_IfMatchHeaders_Test)),
                    operationType: EntityActionOperation.UpsertIncremental,
                    headers: new HeaderDictionary(headerDictionary),
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK
                );
        }

        /// <summary>
        /// Test to validate successful execution of PATCH operation which satisfies the database policy for the update operation it resolves into.
        /// </summary>
        [TestMethod]
        public virtual async Task PatchOneUpdateWithDatabasePolicy()
        {
            // PATCH operation resolves to update because we have a record present for given PK.
            // Since the database policy for update operation ("@item.pieceid ne 1") is satisfied by the operation, it executes successfully.
            string requestBody = @"
            {
                ""piecesAvailable"": 4
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "categoryid/100/pieceid/99",
                    queryString: null,
                    entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                    sqlQuery: GetQuery("PatchOneUpdateWithDatabasePolicy"),
                    operationType: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK,
                    clientRoleHeader: "database_policy_tester"
                );
        }

        /// <summary>
        /// Test to validate successful execution of PATCH operation which satisfies the database policy for the insert operation it resolves into.
        /// </summary>
        [TestMethod]
        public virtual async Task PatchOneInsertWithDatabasePolicy()
        {
            // PATCH operation resolves to insert because we don't have a record present for given PK.
            // Since the database policy for insert operation ("@item.pieceid ne 6 and @item.piecesAvailable gt 0") is satisfied by the operation, it executes successfully.
            string requestBody = @"
            {
                ""piecesAvailable"": 4,
                ""categoryName"": ""SciFi""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "categoryid/0/pieceid/7",
                    queryString: null,
                    entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                    sqlQuery: GetQuery("PatchOneInsertWithDatabasePolicy"),
                    operationType: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.Created,
                    clientRoleHeader: "database_policy_tester",
                    expectedLocationHeader: string.Empty
                );
        }

        /// <summary>
        /// Tests that for a successful PATCH API request, the response returned takes into account that no read action is configured for the role.
        /// URI Path: PK of existing record.
        /// Req Body: Valid Parameter with intended update.
        /// Expects:
        /// Status: 200 OK since the PATCH operation results in an update
        /// Response Body: Empty because the role policy_tester_noread has no read action configured.
        /// </summary>
        [TestMethod]
        public virtual async Task PatchOne_Update_NoReadTest()
        {
            string requestBody = @"
            {
                ""title"": ""Heart of Darkness""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "id/8",
                    queryString: null,
                    entityNameOrPath: _integrationEntityName,
                    sqlQuery: GetQuery(nameof(PatchOne_Update_NoReadTest)),
                    operationType: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK,
                    clientRoleHeader: "test_role_with_noread"
                );
        }

        /// <summary>
        /// Tests that for a successful PATCH API request, the response returned takes into account the include and exclude fields configured for the read action.
        /// URI Path: PK of existing record.
        /// Req Body: Valid Parameter with intended update.
        /// Expects:
        /// Status: 200 OK as PATCH operation results in an update operation.
        /// Response Body: Contains only the id, title fields as publisher_id field is excluded in the read configuration.
        /// </summary>
        [TestMethod]
        public virtual async Task Patch_Update_WithExcludeFieldsTest()
        {
            string requestBody = @"
            {
                ""title"": ""Heart of Darkness""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "id/8",
                    queryString: null,
                    entityNameOrPath: _integrationEntityName,
                    sqlQuery: GetQuery(nameof(Patch_Update_WithExcludeFieldsTest)),
                    operationType: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK,
                    clientRoleHeader: "test_role_with_excluded_fields"
                );
        }

        /// <summary>
        /// Tests that for a successful PATCH API request, the response returned takes into account the database policy configured for the read action.
        /// URI Path: PK of existing record.
        /// Req Body: Valid Parameter with intended update.
        /// Expects:
        /// Status: 200 OK
        /// Response Body: Empty. The read action for the role used in this test has a database policy
        /// defined which states that title cannot be equal to Test. Since, this test updates the title
        /// to Test the response must be empty.
        /// </summary>
        [TestMethod]
        public virtual async Task Patch_Update_WithReadDatabasePolicyTest()
        {
            string requestBody = @"
            {
                ""title"": ""Test""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "id/8",
                    queryString: null,
                    entityNameOrPath: _integrationEntityName,
                    sqlQuery: GetQuery(nameof(PatchOne_Update_NoReadTest)),
                    operationType: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK,
                    clientRoleHeader: "test_role_with_policy_excluded_fields"
                );
        }

        /// <summary>
        /// Tests that for a successful PATCH API request, the response returned takes into account the database policy configured for the read action.
        /// URI Path: PK of existing record.
        /// Req Body: Valid Parameter with intended update.
        /// Expects:
        /// Status: 200 OK
        /// Response Body: Non-Empty and does not contain the publisher_id field. The read action for the role used in this test has a database policy
        /// defined which states that title cannot be equal to Test. Since, this test updates the title
        /// to a different the response must be non-empty. Also, since the role excludes the publisher_id field, the repsonse should not
        /// contain publisher_id field.
        /// </summary>
        [TestMethod]
        public virtual async Task Patch_Update_WithReadDatabasePolicyUnsatisfiedTest()
        {
            string requestBody = @"
            {
                ""title"": ""Heart of Darkness""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "id/8",
                    queryString: null,
                    entityNameOrPath: _integrationEntityName,
                    sqlQuery: GetQuery(nameof(Patch_Update_WithExcludeFieldsTest)),
                    operationType: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK,
                    clientRoleHeader: "test_role_with_policy_excluded_fields"
                );
        }

        /// <summary>
        /// Test to validate that for a PATCH API request that results in a successful insert operation,
        /// the response returned takes into account that no read action is configured for the role.
        /// URI Path: Contains a Non-existent PK.
        /// Req Body: Valid Parameter with intended insert data.
        /// Expects:
        /// Status: 201 Created since the PATCH results in an insert operation
        /// Response Body: Empty because the role policy_tester_noread has no read action configured.
        /// </summary>
        [TestMethod]
        public virtual async Task PatchInsert_NoReadTest()
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
                    sqlQuery: GetQuery("PatchInsert_NoReadTest"),
                    operationType: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.Created,
                    clientRoleHeader: "test_role_with_noread",
                    expectedLocationHeader: string.Empty
                );
        }

        /// <summary>
        /// Tests that for a PATCH API request that results in a successful insert operation,
        /// the response returned takes into account the include and exclude fields configured for the read action.
        /// URI Path: Contains a non-existent PK.
        /// Req Body: Valid Parameter with intended update.
        /// Expects:
        /// Status: 201 Created as PATCH results in an insert operation.
        /// Response Body: Does not contain the categoryName field as it is excluded in the read configuration.
        /// </summary>
        [TestMethod]
        public virtual async Task Patch_Insert_WithExcludeFieldsTest()
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
                   sqlQuery: GetQuery("Patch_Insert_WithExcludeFieldsTest"),
                   operationType: EntityActionOperation.UpsertIncremental,
                   requestBody: requestBody,
                   expectedStatusCode: HttpStatusCode.Created,
                   clientRoleHeader: "test_role_with_excluded_fields",
                   expectedLocationHeader: string.Empty
               );
        }

        /// <summary>
        /// Tests that for a PATCH API request that results in a successful insert operation,
        /// the response returned takes into account the database policy configured for the read action.
        /// URI Path: Contains a non-existent PK.
        /// Req Body: Valid Parameter with intended update.
        /// Expects:
        /// Status: 201 Created as PATCH results in an insert operation.
        /// Response Body: Empty. The database policy configured for the read action states that piecesAvailable cannot be 0.
        /// Since, the PATCH request inserts a record with piecesAvailable = 0, the response must be empty.
        /// </summary>
        [TestMethod]
        public virtual async Task Patch_Insert_WithReadDatabasePolicyTest()
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
                   sqlQuery: GetQuery("PatchInsert_NoReadTest"),
                   operationType: EntityActionOperation.UpsertIncremental,
                   requestBody: requestBody,
                   expectedStatusCode: HttpStatusCode.Created,
                   clientRoleHeader: "test_role_with_policy_excluded_fields",
                   expectedLocationHeader: string.Empty
               );
        }

        /// <summary>
        /// Tests that for a PATCH API request that results in a successful insert operation,
        /// the response returned takes into account the database policy and the include/exlude fields
        /// configured for the read action.
        /// URI Path: Contains a non-existent PK.
        /// Req Body: Valid Parameter with intended update.
        /// Expects:
        /// Status: 201 Created as PATCH results in an insert operation.
        /// Response Body: Non-empty and should not contain the categoryName. The database policy configured for the read action states that piecesAvailable cannot be 0.
        /// But, the PATCH request inserts a record with piecesAvailable = 4, so the policy is unsatisfied. Hence, the response should be non-empty.
        /// The policy also excludes the categoryName field, so the response should not contain the categoryName field.
        /// </summary>
        [TestMethod]
        public virtual async Task Patch_Insert_WithReadDatabasePolicyUnsatisfiedTest()
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
                   sqlQuery: GetQuery("Patch_Insert_WithExcludeFieldsTest"),
                   operationType: EntityActionOperation.UpsertIncremental,
                   requestBody: requestBody,
                   expectedStatusCode: HttpStatusCode.Created,
                   clientRoleHeader: "test_role_with_policy_excluded_fields",
                   expectedLocationHeader: string.Empty
               );
        }

        #endregion

        #region Negative Tests

        /// <summary>
        /// Tests REST PatchOne which results in insert.
        /// URI Path: PK of record that does not exist, Schema PK is autogenerated.
        /// Req Body: Valid Parameters.
        /// Expects: 500 Server error (Not 400 since we don't catch DB specific Identity() insert errors).
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public virtual async Task PatchOne_Insert_PKAutoGen_Test()
        {
            string requestBody = @"
            {
                ""title"": ""The Hobbit Returns to The Shire""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "id/1000",
                    queryString: null,
                    entityNameOrPath: _integrationEntityName,
                    sqlQuery: null,
                    operationType: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    exceptionExpected: true,
                    expectedErrorMessage: $"Cannot perform INSERT and could not find {_integrationEntityName} with primary key <id: 1000> to perform UPDATE on.",
                    expectedStatusCode: HttpStatusCode.NotFound,
                    expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.ItemNotFound.ToString()
                );
        }

        /// <summary>
        /// Tests REST PatchOne which results in insert
        /// URI Path: PK of record that does not exist.
        /// Req Body: Missing non-nullable parameters.
        /// Expects: BadRequest, so no sqlQuery used since req does not touch DB.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public virtual async Task PatchOne_Insert_WithoutNonNullableField_Test()
        {
            string requestBody = @"
            {
                ""issue_number"": ""1234""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "id/1000",
                    queryString: null,
                    entityNameOrPath: _integration_NonAutoGenPK_EntityName,
                    sqlQuery: null,
                    operationType: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    exceptionExpected: true,
                    expectedErrorMessage: $"Cannot perform INSERT and could not find {_integration_NonAutoGenPK_EntityName} with primary key <id: 1000> to perform UPDATE on.",
                    expectedStatusCode: HttpStatusCode.NotFound,
                    expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.ItemNotFound.ToString()
                );
        }

        /// <summary>
        /// Tests the PatchOne functionality with a REST PATCH request using
        /// headers that include as a key "If-Match" with an item that does not exist,
        /// resulting in a DataApiBuilderException with 404 status code with  .
        /// </summary>
        [TestMethod]
        public virtual async Task PatchOne_Update_IfMatchHeaders_NoUpdatePerformed_Test()
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
                    operationType: EntityActionOperation.UpsertIncremental,
                    headers: new HeaderDictionary(headerDictionary),
                    requestBody: requestBody,
                    exceptionExpected: true,
                    expectedErrorMessage: "No Update could be performed, record not found",
                    expectedStatusCode: HttpStatusCode.NotFound,
                    expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.EntityNotFound.ToString()
                );
        }

        /// <summary>
        /// Verifies that we throw exception when field
        /// provided to upsert is an exposed name that
        /// maps to a backing column name that does not
        /// exist in the table.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task PatchTestWithInvalidMapping()
        {
            string requestBody = @"
            {
                ""hazards"": ""black mold"",
                ""region"": ""Pacific North West""
            }";

            string expectedLocationHeader = $"speciedid/3";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: expectedLocationHeader,
                queryString: string.Empty,
                entityNameOrPath: _integrationBrokenMappingEntity,
                sqlQuery: string.Empty,
                operationType: EntityActionOperation.UpsertIncremental,
                exceptionExpected: true,
                requestBody: requestBody,
                expectedErrorMessage: "Invalid request body. Either insufficient or extra fields supplied.",
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: "BadRequest",
                expectedLocationHeader: expectedLocationHeader
                );
        }

        /// <summary>
        /// Tests the Patch functionality with a REST PATCH request
        /// with the request body having null value for non-nullable column
        /// We expect a failure and so no sql query is provided.
        /// </summary>
        [TestMethod]
        public virtual async Task PatchOneWithNonNullableFieldAsNull()
        {
            //Negative test case for Patch resulting in a failed update
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
                operationType: EntityActionOperation.UpsertIncremental,
                requestBody: requestBody,
                exceptionExpected: true,
                expectedErrorMessage: "Invalid value for field categoryName in request body.",
                expectedStatusCode: HttpStatusCode.BadRequest
            );

            //Negative test case for Patch resulting in a failed insert
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
                operationType: EntityActionOperation.UpsertIncremental,
                requestBody: requestBody,
                exceptionExpected: true,
                expectedErrorMessage: "Invalid value for field categoryName in request body.",
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        /// <summary>
        /// Verifies that we throw an exception when an extraneous field that does not map to a backing column in the table
        /// is provided in the request body for a PATCH operation. This test validates the behavior of rest.request-body-strict when it is:
        /// 1. Included in runtime config (and set to true)
        /// 2. Excluded from runtime config(defaults to true)
        /// </summary>
        [TestMethod]
        public async Task PatchOneTestWithExtraneousFieldsInRequestBody()
        {
            // Non-existing field 'hazards' included in the request body for a table with non-autogenerated PK.
            string requestBody = @"
            {
                ""hazards"": ""black mold""
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "speciesid/3",
                queryString: string.Empty,
                entityNameOrPath: _integrationBrokenMappingEntity,
                sqlQuery: string.Empty,
                operationType: EntityActionOperation.UpsertIncremental,
                exceptionExpected: true,
                requestBody: requestBody,
                expectedErrorMessage: "Invalid request body. Either insufficient or extra fields supplied.",
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
                );
        }

        /// <summary>
        /// Tests that a cast failure of primary key value type results in HTTP 400 Bad Request.
        /// e.g. Attempt to cast a string '{}' to the 'publisher_id' column type of int will fail.
        /// </summary>
        [TestMethod]
        public async Task PatchWithUncastablePKValue()
        {
            string requestBody = @"
            {
                ""publisher_id"": ""StringFailsToCastToInt""
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/1",
                queryString: string.Empty,
                entityNameOrPath: _integrationEntityName,
                sqlQuery: null,
                operationType: EntityActionOperation.UpsertIncremental,
                requestBody: requestBody,
                exceptionExpected: true,
                expectedErrorMessage: "Parameter \"StringFailsToCastToInt\" cannot be resolved as column \"publisher_id\" with type \"Int32\".",
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        /// <summary>
        /// Tests the Patch functionality with a REST PATCH request
        /// without a primary key route. We expect a failure and so
        /// no sql query is provided.
        /// </summary>
        [TestMethod]
        public virtual async Task PatchWithNoPrimaryKeyRouteTest()
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
                    operationType: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    exceptionExpected: true,
                    expectedErrorMessage: RequestValidator.PRIMARY_KEY_NOT_PROVIDED_ERR_MESSAGE,
                    expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        /// <summary>
        /// Test to validate failure of PATCH operation failing to satisfy the database policy for the update operation.
        /// (because a record exists for given PK).
        /// </summary>
        [TestMethod]
        public virtual async Task PatchOneUpdateWithUnsatisfiedDatabasePolicy()
        {
            // PATCH operation resolves to update because we have a record present for given PK.
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
                    operationType: EntityActionOperation.UpsertIncremental,
                    entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
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
        /// Test to validate failure of PATCH operation failing to satisfy the database policy for the update operation.
        /// (because no record exists for given PK).
        /// </summary>
        [TestMethod]
        public virtual async Task PatchOneInsertWithUnsatisfiedDatabasePolicy()
        {
            // PATCH operation resolves to insert because we don't have a record present for given PK.
            // However, the insert fails to execute successfully because the database policy ("@item.pieceid ne 6 and @item.piecesAvailable gt 6")
            // for insert operation is not satisfied.
            string requestBody = @"
            {
                ""categoryName"": ""SciFi"",
                ""piecesRequired"": 5,
                ""piecesAvailable"": 2
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "categoryid/0/pieceid/6",
                    queryString: null,
                    entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                    sqlQuery: string.Empty,
                    operationType: EntityActionOperation.Upsert,
                    requestBody: requestBody,
                    exceptionExpected: true,
                    expectedErrorMessage: DataApiBuilderException.AUTHORIZATION_FAILURE,
                    expectedStatusCode: HttpStatusCode.Forbidden,
                    expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.DatabasePolicyFailure.ToString(),
                    clientRoleHeader: "database_policy_tester"
                    );
        }

        /// <summary>
        /// Test to verify that we throw exception for invalid/bad
        /// PATCH requests on views.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public virtual async Task PatchOneViewBadRequestTest(
            string expectedErrorMessage,
            bool isExpectedErrorMsgSubstr = false)
        {
            // PATCH update trying to modify fields from multiple base table
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
                operationType: EntityActionOperation.UpsertIncremental,
                requestBody: requestBody,
                exceptionExpected: true,
                expectedErrorMessage: expectedErrorMessage,
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed.ToString(),
                isExpectedErrorMsgSubstr: true
            );
        }

        /// <summary>
        /// Test to validate that whenever a computed field is included in the request body, we throw a BadRequest exception
        /// as it is not allowed to provide value (to insert/update) for a computed field.
        /// </summary>
        [TestMethod]
        public virtual async Task PatchOneWithComputedFieldInRequestBody()
        {
            // Validate that a BadRequest exception is thrown for a PATCH update when a computed field is included in request body.
            string requestBody = @"
            {
                ""last_sold_on_date"": null
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/1",
                queryString: string.Empty,
                entityNameOrPath: _entityWithReadOnlyFields,
                sqlQuery: string.Empty,
                operationType: EntityActionOperation.UpsertIncremental,
                exceptionExpected: true,
                requestBody: requestBody,
                expectedErrorMessage: "Field 'last_sold_on_date' cannot be included in the request body.",
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
                );

            // Validate that a BadRequest exception is thrown for a PATCH insert when a computed field is included in request body.
            requestBody = @"
            {
                ""last_sold_on_date"": null
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/2",
                queryString: string.Empty,
                entityNameOrPath: _entityWithReadOnlyFields,
                sqlQuery: string.Empty,
                operationType: EntityActionOperation.UpsertIncremental,
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
