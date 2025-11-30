// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests
{
    /// <summary>
    /// Class containing integration tests to validate scenarios when we operate in non-strict mode for REST request body,
    /// i.e. we allow extraneous fields to be present in the request body.
    /// </summary>
    public abstract class RestBodyNonStrictModeTests : RestApiTestBase
    {
        #region Positive tests

        /// <summary>
        /// Test to validate that extraneous fields are allowed in request body for POST operation when we operate in runtime.rest.request-body-strict = false.
        /// </summary>
        [TestMethod]
        public virtual async Task InsertOneWithExtraneousFieldsInRequestBody()
        {
            // Validate that when a field which does not map to any column (here 'non_existing_field')
            // in the underlying table is included in the request body for POST operation,
            // it is just ignored when we do not operate in strict mode for REST.
            string requestBody = @"
            {
                ""categoryid"": ""3"",
                ""pieceid"": ""1"",
                ""piecesAvailable"": null,
                ""piecesRequired"": 1,
                ""categoryName"":""SciFi"",
                ""non_existing_field"": 5
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: null,
                    queryString: null,
                    entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                    sqlQuery: GetQuery("InsertOneWithExtraneousFieldsInRequestBody"),
                    HttpMethod: EntityActionOperation.Insert,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.Created,
                    expectedLocationHeader: "categoryid/3/pieceid/1"
                );

            requestBody = @"
            {
                ""id"": 2,
                ""book_name"": ""Harry Potter"",
                ""copies_sold"": 50,
                ""last_sold_on_date"": ""2023-09-13 17:37:20""
            }";

            string expectedLocationHeader = $"id/2";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: null,
                queryString: null,
                entityNameOrPath: _entityWithReadOnlyFields,
                sqlQuery: GetQuery("InsertOneWithReadOnlyFieldsInRequestBody"),
                HttpMethod: EntityActionOperation.Insert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.Created,
                expectedLocationHeader: expectedLocationHeader
            );
        }

        /// <summary>
        /// Test to validate that extraneous fields are allowed in request body for PUT operation when we operate in runtime.rest.request-body-strict = false.
        /// When PK fields are specified both in URI and in the request body, the values specified for the fields in the URI are honored,
        /// and the values specified in the request body are ignored.
        /// </summary>
        [TestMethod]
        public virtual async Task PutOneWithExtraneousFieldsInRequestBody()
        {
            // Validate that when PK fields (here 'categoryid' and 'pieceid') are included in the request body for PUT operation,
            // they are just ignored when we do not operate in strict mode for REST. The values of the fields from the URL is used.
            string requestBody = @"
            {
               ""categoryName"":""SciFi"",
               ""piecesAvailable"":""10"",
               ""piecesRequired"":""5"",
               ""piecied"": 5,
               ""categoryid"": 4
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "categoryid/2/pieceid/1",
                queryString: null,
                entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                sqlQuery: GetQuery("PutOneWithExtraneousFieldsInRequestBody"),
                HttpMethod: EntityActionOperation.Upsert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.OK
                );

            // Validate that when fields which do not map to any column (here 'non_existing_field')
            // in the underlying table are included in the request body for PUT operation,
            // they are just ignored when we do not operate in strict mode for REST.
            requestBody = @"
            {
               ""categoryName"":""SciFi"",
               ""piecesAvailable"":""10"",
               ""piecesRequired"":""5"",
               ""non_existing_field"": ""5""
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "categoryid/2/pieceid/1",
                queryString: null,
                entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                sqlQuery: GetQuery("PutOneWithExtraneousFieldsInRequestBody"),
                HttpMethod: EntityActionOperation.Upsert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.OK
                );

            // Validate that when a computed field (here 'last_sold_on_date') is included the request body for PUT update operation,
            // it is just ignored when we do not operate in strict mode for REST. Successful execution of the PUT request confirms
            // that we did not attempt to update the value of 'last_sold_on_update' field.
            requestBody = @"
            {
                ""book_name"": ""New book"",
                ""copies_sold"": 101,
                ""last_sold_on"": null,
                ""last_sold_on_date"": ""2023-09-12 05:30:30""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "id/1",
                    queryString: null,
                    entityNameOrPath: _entityWithReadOnlyFields,
                    sqlQuery: GetQuery("PutOneUpdateWithComputedFieldInRequestBody"),
                    HttpMethod: EntityActionOperation.Upsert,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK
                );

            // Validate that when a computed field (here 'last_sold_on_date') is included the request body for PUT insert operation,
            // it is just ignored when we do not operate in strict mode for REST. Successful execution of the PUT request confirms
            // that we did not attempt to insert a value for 'last_sold_on_update' field.
            requestBody = @"
            {
                ""book_name"": ""New book"",
                ""copies_sold"": 101,
                ""last_sold_on_date"": ""2023-09-13 17:37:20""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "id/2",
                    queryString: null,
                    entityNameOrPath: _entityWithReadOnlyFields,
                    sqlQuery: GetQuery("PutOneInsertWithComputedFieldInRequestBody"),
                    HttpMethod: EntityActionOperation.Upsert,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.Created,
                    expectedLocationHeader: string.Empty
                );
        }

        /// <summary>
        /// Test to validate that extraneous fields are allowed in request body for PATCH operation when we operate in runtime.rest.request-body-strict = false.
        /// When PK fields are specified both in URI and in the request body, the values specified for the fields in the URI are honored,
        /// and the values specified in the request body are ignored.
        /// </summary>
        [TestMethod]
        public virtual async Task PatchOneWithExtraneousFieldsInRequestBody()
        {
            string requestBody = @"
            {
                ""piecesAvailable"": null,
                ""piecied"": 5,
                ""categoryid"": 4
            }";

            // Validate that when PK fields (here 'categoryid' and 'pieceid') are included in the request body for PATCH operation,
            // they are just ignored when we do not operate in strict mode for REST. The values of the fields from the URL is used.
            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "categoryid/1/pieceid/1",
                    queryString: null,
                    entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                    sqlQuery: GetQuery("PatchOneWithExtraneousFieldsInRequestBody"),
                    HttpMethod: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK
                );

            requestBody = @"
            {
                ""piecesAvailable"": null,
                ""non_existing_field"": ""5""
            }";

            // Validate that when fields which do not map to any column (here 'non_existing_field')
            // in the underlying table are included in the request body for PATCH operation,
            // they are just ignored when we do not operate in strict mode for REST.
            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "categoryid/1/pieceid/1",
                    queryString: null,
                    entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                    sqlQuery: GetQuery("PatchOneWithExtraneousFieldsInRequestBody"),
                    HttpMethod: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK
                );

            // Validate that when a computed field (here 'last_sold_on_update') is included the request body for PATCH update operation,
            // it is just ignored when we do not operate in strict mode for REST. Successful execution of the PATCH request confirms
            // that we did not attempt to update the value of 'last_sold_on_update' field.
            requestBody = @"
            {
                ""book_name"": ""New book"",
                ""copies_sold"": 50,
                ""last_sold_on_date"": ""2023-09-13 17:37:20""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: $"id/1",
                    queryString: null,
                    entityNameOrPath: _entityWithReadOnlyFields,
                    sqlQuery: GetQuery("PatchOneUpdateWithComputedFieldInRequestBody"),
                    HttpMethod: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK
                );

            // Validate that when a computed field (here 'last_sold_on_date') is included the request body for PATCH insert operation,
            // it is just ignored when we do not operate in strict mode for REST. Successful execution of the PATCH request confirms
            // that we did not attempt to insert a value for 'last_sold_on_update' field.
            requestBody = @"
            {
                ""book_name"": ""New book"",
                ""copies_sold"": 50,
                ""last_sold_on_date"": ""2023-09-13 17:37:20""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: $"id/3",
                    queryString: null,
                    entityNameOrPath: _entityWithReadOnlyFields,
                    sqlQuery: GetQuery("PatchOneInsertWithComputedFieldInRequestBody"),
                    HttpMethod: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.Created,
                    expectedLocationHeader: string.Empty
                );
        }
        #endregion Positive tests

        #region Negative tests

        /// <summary>
        /// Test to validate that extraneous fields are not allowed in primary key even when we operate in runtime.rest.request-body-strict = false.
        /// Since primary keys are allowed for PUT/PATCH/DELETE mutations, we need to test only for those operations. 
        /// </summary>
        [TestMethod]
        public virtual async Task MutationsTestWithExtraneousFieldsInPrimaryKey()
        {
            string requestBody = @"
            {
               ""categoryName"":""SciFi"",
               ""piecesAvailable"":""10"",
               ""piecesRequired"":""5""
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "categoryid/2/non_existing_field/1",
                queryString: null,
                entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                sqlQuery: null,
                HttpMethod: EntityActionOperation.Upsert,
                requestBody: requestBody,
                exceptionExpected: true,
                expectedErrorMessage: "Primary key column: non_existing_field not found in the entity definition.",
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.InvalidIdentifierField.ToString()
                );

            requestBody = @"
            {
                ""piecesAvailable"": null
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "categoryid/1/non_existing_field/1",
                    queryString: null,
                    entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                    sqlQuery: null,
                    HttpMethod: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    exceptionExpected: true,
                    expectedErrorMessage: "Primary key column: non_existing_field not found in the entity definition.",
                    expectedStatusCode: HttpStatusCode.BadRequest,
                    expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.InvalidIdentifierField.ToString()
                );

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "categoryid/1/non_existing_field/1",
                    queryString: null,
                    entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                    sqlQuery: null,
                    HttpMethod: EntityActionOperation.Delete,
                    requestBody: requestBody,
                    exceptionExpected: true,
                    expectedErrorMessage: "Primary key column: non_existing_field not found in the entity definition.",
                    expectedStatusCode: HttpStatusCode.BadRequest,
                    expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.InvalidIdentifierField.ToString()
                );
        }
        #endregion Negative tests
    }
}
