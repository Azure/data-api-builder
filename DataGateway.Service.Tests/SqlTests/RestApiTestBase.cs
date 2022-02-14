using System.Net;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests
{
    /// <summary>
    /// Test REST Apis validating expected results are obtained.
    /// </summary>
    [TestClass]
    public abstract class RestApiTestBase : SqlTestBase
    {
        protected static RestService _restService;
        protected static RestController _restController;
        protected static readonly string _integrationTableName = "books";

        public abstract string GetQuery(string key);

        #region Positive Tests
        /// <summary>
        /// Tests the REST Api for FindById operation without a query string.
        /// </summary>
        [TestMethod]
        public async Task FindByIdTest()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/2",
                queryString: string.Empty,
                entity: _integrationTableName,
                sqlQuery: GetQuery(nameof(FindByIdTest)),
                controller: _restController
            );
        }

        /// <summary>
        /// Tests the REST Api for FindById operation with a query string
        /// including the field names.
        /// </summary>
        [TestMethod]
        public async Task FindByIdTestWithQueryStringFields()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/1",
                queryString: "?_f=id,title",
                entity: _integrationTableName,
                sqlQuery: GetQuery(nameof(FindByIdTestWithQueryStringFields)),
                controller: _restController
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation with a query string with 1 field
        /// including the field names.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithQueryStringOneField()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?_f=id",
                entity: _integrationTableName,
                sqlQuery: GetQuery(nameof(FindTestWithQueryStringOneField)),
                controller: _restController);

        }

        /// <summary>
        /// Tests the REST Api for Find operation with a query string with multiple fields
        /// including the field names. Only returns fields designated in the query string.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithQueryStringMultipleFields()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?_f=id,title",
                entity: _integrationTableName,
                sqlQuery: GetQuery(nameof(FindTestWithQueryStringMultipleFields)),
                controller: _restController
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation with an empty query string
        /// including the field names.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithQueryStringAllFields()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entity: _integrationTableName,
                sqlQuery: GetQuery(nameof(FindTestWithQueryStringAllFields)),
                controller: _restController
            );
        }

        [TestMethod]
        public async Task FindTestWithPrimaryKeyContainingForeignKey()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/567/book_id/1",
                queryString: "?_f=id,content",
                entity: "reviews",
                sqlQuery: GetQuery(nameof(FindTestWithPrimaryKeyContainingForeignKey)),
                controller: _restController
            );
        }

        /// <summary>
        /// Tests the InsertOne functionality with a REST POST request.
        /// </summary>
        [TestMethod]
        public virtual async Task InsertOneTest()
        {
            string requestBody = @"
            {
                ""title"": ""My New Book"",
                ""publisher_id"": 1234
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: null,
                    queryString: null,
                    entity: _integrationTableName,
                    sqlQuery: GetQuery(nameof(InsertOneTest)),
                    controller: _restController,
                    operationType: Operation.Insert,
                    requestBody: requestBody,
                    expectedStatusCode: 201
                );
        }

        /// <summary>
        /// DeleteOne operates on a single entity with target object
        /// identified in the primaryKeyRoute. No requestBody is used
        /// for this type of request.
        /// sqlQuery is not used because we are confirming the NoContent result
        /// of a successful delete operation.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task DeleteOneTest()
        {
            //expected status code 204
            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "id/5",
                    queryString: null,
                    entity: _integrationTableName,
                    sqlQuery: null,
                    controller: _restController,
                    operationType: Operation.Delete,
                    requestBody: null,
                    expectedStatusCode: (int)HttpStatusCode.NoContent
                );
        }

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
                    entity: _integrationTableName,
                    sqlQuery: GetQuery(nameof(PutOne_Update_Test)),
                    controller: _restController,
                    operationType: Operation.Upsert,
                    requestBody: requestBody,
                    expectedStatusCode: 204
                );
        }

        /// <summary>
        /// Tests the PutOne functionality with a REST PUT request
        /// with item that does NOT exist, results in insert.
        /// </summary>
        [TestMethod]
        public virtual async Task PutOne_Insert_Test()
        {
            string requestBody = @"
            {
                ""title"": ""The Hobbit Returns to The Shire"",
                ""publisher_id"": 1234
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "id/1000",
                    queryString: null,
                    entity: _integrationTableName,
                    sqlQuery: GetQuery(nameof(PutOne_Insert_Test)),
                    controller: _restController,
                    operationType: Operation.Upsert,
                    requestBody: requestBody,
                    expectedStatusCode: 201
                );
        }

        #endregion

        #region Negative Tests
        /// <summary>
        /// Tests the PutOne functionality with a REST PUT request
        /// with item that does NOT exist, AND parameters incorrectly match schema, results in BadRequest.
        /// sqlQuery represents the query used to get 'expected' result of zero items.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public virtual async Task PutOne_Insert_BadReq_Test()
        {
            string requestBody = @"
            {
                ""title"": ""The Hobbit Returns to The Shire""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "id/7",
                    queryString: null,
                    entity: _integrationTableName,
                    sqlQuery: GetQuery(nameof(PutOne_Insert_BadReq_Test)),
                    controller: _restController,
                    operationType: Operation.Upsert,
                    requestBody: requestBody,
                    exception: true,
                    expectedErrorMessage: "Invalid request body. Either insufficient or unnecessary values for fields supplied.",
                    expectedStatusCode: (int)HttpStatusCode.BadRequest,
                    expectedSubStatusCode: DatagatewayException.SubStatusCodes.BadRequest.ToString()
                );
        }

        /// <summary>
        /// DeleteNonExistent operates on a single entity with target object
        /// identified in the primaryKeyRoute.
        /// sqlQuery represents the query used to get 'expected' result of zero items.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task DeleteNonExistentTest()
        {//expected status code 404
            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "id/1000",
                    queryString: null,
                    entity: _integrationTableName,
                    sqlQuery: GetQuery(nameof(DeleteNonExistentTest)),
                    controller: _restController,
                    operationType: Operation.Delete,
                    requestBody: null,
                    exception: true,
                    expectedErrorMessage: "Not Found",
                    expectedStatusCode: (int)HttpStatusCode.NotFound,
                    expectedSubStatusCode: DatagatewayException.SubStatusCodes.EntityNotFound.ToString()
                );
        }

        /// <summary>
        /// DeleteWithInvalidPrimaryKey operates on a single entity with target object
        /// identified in the primaryKeyRoute. No sqlQuery value is provided as this request
        /// should fail prior to querying the database.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task DeleteWithInvalidPrimaryKeyTest()
        {//expected status code 404
            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "title/7",
                    queryString: null,
                    entity: _integrationTableName,
                    sqlQuery: null,
                    controller: _restController,
                    operationType: Operation.Delete,
                    requestBody: null,
                    exception: true,
                    expectedErrorMessage: "The request is invalid since the primary keys: title requested were not found in the entity definition.",
                    expectedStatusCode: (int)HttpStatusCode.BadRequest,
                    expectedSubStatusCode: DatagatewayException.SubStatusCodes.BadRequest.ToString()
                );
        }

        /// <summary>
        /// Tests the REST Api for FindById operation with a query string
        /// having invalid field names.
        /// </summary>
        [TestMethod]
        public async Task FindByIdTestWithInvalidFields()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/5671",
                queryString: "?_f=id,content",
                entity: _integrationTableName,
                sqlQuery: GetQuery(nameof(FindByIdTestWithInvalidFields)),
                controller: _restController,
                exception: true,
                expectedErrorMessage: "Invalid Column name requested: content",
                expectedStatusCode: 400
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation with a query string that has an invalid field
        /// having invalid field names.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithInvalidFields()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?_f=id,null",
                entity: _integrationTableName,
                sqlQuery: GetQuery(nameof(FindTestWithInvalidFields)),
                controller: _restController,
                exception: true,
                expectedErrorMessage: RestController.SERVER_ERROR,
                expectedStatusCode: (int)HttpStatusCode.InternalServerError,
                expectedSubStatusCode: DatagatewayException.SubStatusCodes.UnexpectedError.ToString()
            );
        }

        /// <summary>
        /// Tests the REST Api for the correct error condition format when
        /// a DatagatewayException is thrown
        /// </summary>
        [TestMethod]
        public async Task RestDatagatewayExceptionErrorConditionFormat()
        {
            string msSqlQuery = string.Empty;

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?_f=id,content",
                entity: _integrationTableName,
                sqlQuery: msSqlQuery,
                controller: _restController,
                exception: true,
                expectedErrorMessage: "Invalid Column name requested: content",
                expectedStatusCode: 400
            );
        }

        #endregion
    }
}
