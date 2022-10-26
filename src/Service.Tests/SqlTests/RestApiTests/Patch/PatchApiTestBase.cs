using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Services;
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
            string expectedLocationHeader = $"categoryid/3/pieceid/1";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: expectedLocationHeader,
                    queryString: null,
                    entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                    sqlQuery: GetQuery("PatchOne_Insert_Nulled_Test"),
                    operationType: Operation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.Created,
                    expectedLocationHeader: expectedLocationHeader
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
                    operationType: Operation.UpsertIncremental,
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

            string expectedLocationHeader = $"┬─┬ノ( º _ ºノ)/2";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: $"┬─┬ノ( º _ ºノ)/2",
                    queryString: null,
                    entityNameOrPath: _integrationUniqueCharactersEntity,
                    sqlQuery: GetQuery(nameof(PatchOne_Insert_UniqueCharacters_Test)),
                    operationType: Operation.Upsert,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.Created,
                    expectedLocationHeader: expectedLocationHeader
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

            string expectedLocationHeader = $"id/2";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: $"id/2",
                    queryString: null,
                    entityNameOrPath: _integration_NonAutoGenPK_EntityName,
                    sqlQuery: GetQuery(nameof(PatchOne_Insert_NonAutoGenPK_Test)),
                    operationType: Operation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.Created,
                    expectedLocationHeader: expectedLocationHeader
                );

            requestBody = @"
            {
                ""categoryName"": ""FairyTales"",
                ""piecesAvailable"":""5"",
                ""piecesRequired"":""4""
            }";
            expectedLocationHeader = $"categoryid/4/pieceid/1";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: expectedLocationHeader,
                    queryString: null,
                    entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                    sqlQuery: GetQuery("PatchOne_Insert_CompositeNonAutoGenPK_Test"),
                    operationType: Operation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.Created,
                    expectedLocationHeader: expectedLocationHeader
                );

            requestBody = @"
            {
                ""categoryName"": """",
                ""piecesAvailable"":""5"",
                ""piecesRequired"":""4""
            }";
            expectedLocationHeader = $"categoryid/5/pieceid/1";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: expectedLocationHeader,
                    queryString: null,
                    entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                    sqlQuery: GetQuery("PatchOne_Insert_Empty_Test"),
                    operationType: Operation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.Created,
                    expectedLocationHeader: expectedLocationHeader
                );

            requestBody = @"
            {
                ""categoryName"": ""SciFi""
            }";
            expectedLocationHeader = $"categoryid/7/pieceid/1";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: expectedLocationHeader,
                    queryString: null,
                    entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                    sqlQuery: GetQuery("PatchOne_Insert_Default_Test"),
                    operationType: Operation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.Created,
                    expectedLocationHeader: expectedLocationHeader
                );

            // Entity with mapping defined for columns
            requestBody = @"
            {
                ""Scientific Name"": ""Quercus"",
                ""United State's Region"": ""South West""
            }";
            expectedLocationHeader = $"treeId/4";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: expectedLocationHeader,
                    queryString: null,
                    entityNameOrPath: _integrationMappingEntity,
                    sqlQuery: GetQuery("PatchOne_Insert_Mapping_Test"),
                    operationType: Operation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.Created,
                    expectedLocationHeader: expectedLocationHeader
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
                    operationType: Operation.UpsertIncremental,
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
                    operationType: Operation.UpsertIncremental,
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
                    operationType: Operation.UpsertIncremental,
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
                    operationType: Operation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK
                );
        }

        /// <summary>
        /// Tests the PatchOne functionality with a REST PUT request using
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
                    operationType: Operation.UpsertIncremental,
                    headers: new HeaderDictionary(headerDictionary),
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK
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
                    operationType: Operation.UpsertIncremental,
                    requestBody: requestBody,
                    exceptionExpected: true,
                    expectedErrorMessage: $"Cannot perform INSERT and could not find {_integrationEntityName} with primary key <id: 1000> to perform UPDATE on.",
                    expectedStatusCode: HttpStatusCode.NotFound,
                    expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.EntityNotFound.ToString()
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
                    operationType: Operation.UpsertIncremental,
                    requestBody: requestBody,
                    exceptionExpected: true,
                    expectedErrorMessage: $"Cannot perform INSERT and could not find {_integration_NonAutoGenPK_EntityName} with primary key <id: 1000> to perform UPDATE on.",
                    expectedStatusCode: HttpStatusCode.NotFound,
                    expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.EntityNotFound.ToString()
                );
        }

        /// <summary>
        /// Tests the PatchOne functionality with a REST PUT request using
        /// headers that include as a key "If-Match" with an item that does not exist,
        /// resulting in a DataApiBuilderException with status code of Precondition Failed.
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
                    primaryKeyRoute: "id/18",
                    queryString: string.Empty,
                    entityNameOrPath: _integrationEntityName,
                    sqlQuery: string.Empty,
                    operationType: Operation.UpsertIncremental,
                    headers: new HeaderDictionary(headerDictionary),
                    requestBody: requestBody,
                    exceptionExpected: true,
                    expectedErrorMessage: "No Update could be performed, record not found",
                    expectedStatusCode: HttpStatusCode.PreconditionFailed,
                    expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed.ToString()
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
                operationType: Operation.UpsertIncremental,
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
                operationType: Operation.UpsertIncremental,
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
                operationType: Operation.UpsertIncremental,
                requestBody: requestBody,
                exceptionExpected: true,
                expectedErrorMessage: "Invalid value for field categoryName in request body.",
                expectedStatusCode: HttpStatusCode.BadRequest
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
                operationType: Operation.UpsertIncremental,
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
                    operationType: Operation.UpsertIncremental,
                    requestBody: requestBody,
                    exceptionExpected: true,
                    expectedErrorMessage: RequestValidator.PRIMARY_KEY_NOT_PROVIDED_ERR_MESSAGE,
                    expectedStatusCode: HttpStatusCode.BadRequest
                    );
        }

        #endregion
    }
}
