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
        /// Test to validate that extraneous fields are allowed in request body when we operate in runtime.rest.request-body-strict = false.
        /// When PK fields are specified both in URI and in the request body, precedence is given to the values specified for the fields in the URI.
        /// This single test validates the functionality for PUT, PATCH and and POST requests.
        /// </summary>
        [TestMethod]
        public virtual async Task MutationsTestWithExtraneousFieldsInRequestBody()
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
                    operationType: EntityActionOperation.Insert,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.Created,
                    expectedLocationHeader: "categoryid/3/pieceid/1"
                );

            // Validate that when PK fields (here 'categoryid' and 'pieceid') are included in the request body for PUT operation,
            // they are just ignored when we do not operate in strict mode for REST. The values of the fields from the URL is used.
            requestBody = @"
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
                operationType: EntityActionOperation.Upsert,
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
                operationType: EntityActionOperation.Upsert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.OK
                );

            requestBody = @"
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
                    operationType: EntityActionOperation.UpsertIncremental,
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
                    operationType: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK
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
                operationType: EntityActionOperation.Upsert,
                requestBody: requestBody,
                exceptionExpected: true,
                expectedErrorMessage: "Primary key column: non_existing_field not found in the entity definition.",
                expectedStatusCode: HttpStatusCode.NotFound,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.EntityNotFound.ToString()
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
                    operationType: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    exceptionExpected: true,
                    expectedErrorMessage: "Primary key column: non_existing_field not found in the entity definition.",
                    expectedStatusCode: HttpStatusCode.NotFound,
                    expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.EntityNotFound.ToString()
                );

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "categoryid/1/non_existing_field/1",
                    queryString: null,
                    entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                    sqlQuery: null,
                    operationType: EntityActionOperation.Delete,
                    requestBody: requestBody,
                    exceptionExpected: true,
                    expectedErrorMessage: "Primary key column: non_existing_field not found in the entity definition.",
                    expectedStatusCode: HttpStatusCode.NotFound,
                    expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.EntityNotFound.ToString()
                );
        }
        #endregion Negative tests
    }
}
