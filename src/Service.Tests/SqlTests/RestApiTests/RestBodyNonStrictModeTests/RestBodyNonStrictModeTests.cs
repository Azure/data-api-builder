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
        /// Test to validate that extraneous fields are allowed in request body when we operate in rest.request-body-strict = false.
        /// </summary>
        [TestMethod]
        public virtual async Task MutationsTestWithExtraneousFieldsInRequestBody()
        {
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
                    sqlQuery: GetQuery("InsertOneWithNonExistingFieldInRequestBody"),
                    operationType: EntityActionOperation.Insert,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.Created,
                    expectedLocationHeader: "categoryid/3/pieceid/1"
                );

            requestBody = @"
            {
               ""categoryName"":""SciFi"",
               ""piecesAvailable"":""10"",
               ""piecesRequired"":""4"",
               ""non_existing_field"": 5
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "categoryid/2/pieceid/1",
                queryString: null,
                entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                sqlQuery: GetQuery("PutOneWithNonExistingFieldInRequestBody"),
                operationType: EntityActionOperation.Upsert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.OK
                );

            requestBody = @"
            {
                ""piecesAvailable"": null,
                ""non_existing_field"": ""5""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "categoryid/1/pieceid/1",
                    queryString: null,
                    entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                    sqlQuery: GetQuery("PatchOneWithNonExistingFieldInRequestBody"),
                    operationType: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK
                );
        }
        #endregion Positive tests

        #region Negative tests

        /// <summary>
        /// Test to validate that extraneous fields are not allowed in primary key even when we operate in rest.request-body-strict = false.
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
        }
        #endregion Negative tests
    }
}
