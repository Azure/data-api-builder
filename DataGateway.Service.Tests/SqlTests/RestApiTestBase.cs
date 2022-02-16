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
        protected static readonly string _tableWithCompositePrimaryKey = "reviews";
        protected const int STARTING_ID_FOR_TEST_INSERTS = 5001;
        protected static readonly string _integration_NonAutoGenPK_TableName = "magazines";

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

        /// <summary>
        /// Tests the REST Api for Find operation with a single filter, executes
        /// a test for all of the comparison operators and the unary NOT operator.
        /// </summary>
        [TestMethod]
        public async Task FindTestsWithFilterQueryStringOneOpFilter()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=id eq 1",
                entity: _integrationTableName,
                sqlQuery: GetQuery("FindTestWithFilterQueryStringOneEqFilter"),
                controller: _restController);
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=2 eq id",
                entity: _integrationTableName,
                sqlQuery: GetQuery("FindTestWithFilterQueryStringValueFirstOneEqFilter"),
                controller: _restController);
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=id gt 3",
                entity: _integrationTableName,
                sqlQuery: GetQuery("FindTestWithFilterQueryOneGtFilter"),
                controller: _restController);
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=id ge 4",
                entity: _integrationTableName,
                sqlQuery: GetQuery("FindTestWithFilterQueryOneGeFilter"),
                controller: _restController);
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=id lt 5",
                entity: _integrationTableName,
                sqlQuery: GetQuery("FindTestWithFilterQueryOneLtFilter"),
                controller: _restController);
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=id le 4",
                entity: _integrationTableName,
                sqlQuery: GetQuery("FindTestWithFilterQueryOneLeFilter"),
                controller: _restController);
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=id ne 3",
                entity: _integrationTableName,
                sqlQuery: GetQuery("FindTestWithFilterQueryOneNeFilter"),
                controller: _restController);
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=not (id lt 2)",
                entity: _integrationTableName,
                sqlQuery: GetQuery("FindTestWithFilterQueryOneNotFilter"),
                controller: _restController);
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=not (title eq null)",
                entity: _integrationTableName,
                sqlQuery: GetQuery("FindTestWithFilterQueryOneRightNullEqFilter"),
                controller: _restController);
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=null ne title",
                entity: _integrationTableName,
                sqlQuery: GetQuery("FindTestWithFilterQueryOneLeftNullNeFilter"),
                controller: _restController
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation with two filters separated with AND
        /// comparisons connected with OR.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFilterQueryStringSingleAndFilter()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=id lt 3 and id gt 1",
                entity: _integrationTableName,
                sqlQuery: GetQuery(nameof(FindTestWithFilterQueryStringSingleAndFilter)),
                controller: _restController
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation with two filters separated with OR
        /// comparisons connected with OR.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFilterQueryStringSingleOrFilter()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=id lt 3 or id gt 4",
                entity: _integrationTableName,
                sqlQuery: GetQuery(nameof(FindTestWithFilterQueryStringSingleOrFilter)),
                controller: _restController
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation with a filter query string with multiple
        /// comparisons connected with AND.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFilterQueryStringMultipleAndFilters()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=id lt 4 and id gt 1 and title ne 'Awesome book'",
                entity: _integrationTableName,
                sqlQuery: GetQuery(nameof(FindTestWithFilterQueryStringMultipleAndFilters)),
                controller: _restController);

        }

        /// <summary>
        /// Tests the REST Api for Find operation with a filter query string with multiple
        /// comparisons connected with OR.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFilterQueryStringMultipleOrFilters()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=id eq 1 or id eq 2 or id eq 3",
                entity: _integrationTableName,
                sqlQuery: GetQuery(nameof(FindTestWithFilterQueryStringMultipleOrFilters)),
                controller: _restController
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation with a filter query string with multiple
        /// comparisons connected with AND and those comparisons connected with OR.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFilterQueryStringMultipleAndOrFilters()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=(id gt 2 and id lt 4) or (title eq 'Awesome book')",
                entity: _integrationTableName,
                sqlQuery: GetQuery(nameof(FindTestWithFilterQueryStringMultipleAndOrFilters)),
                controller: _restController
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation with a filter query string with multiple
        /// comparisons connected with AND OR and including NOT.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFilterQueryStringMultipleNotAndOrFilters()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=(not (id lt 3) or id lt 4) or not (title eq 'Awesome book')",
                entity: _integrationTableName,
                sqlQuery: GetQuery(nameof(FindTestWithFilterQueryStringMultipleNotAndOrFilters)),
                controller: _restController
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation where we compare one field
        /// to the bool returned from another comparison.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFilterQueryStringBoolResultFilter()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=id eq (publisher_id gt 1)",
                entity: _integrationTableName,
                sqlQuery: GetQuery(nameof(FindTestWithFilterQueryStringSingleAndFilter)),
                controller: _restController,
                exception: true,
                expectedErrorMessage: "A binary operator with incompatible types was detected. " +
                    "Found operand types 'Edm.Int64' and 'Edm.Boolean' for operator kind 'Equal'.",
                expectedStatusCode: 400
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

            string expectedLocationHeader = $"/id/{STARTING_ID_FOR_TEST_INSERTS}";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: null,
                queryString: null,
                entity: _integrationTableName,
                sqlQuery: GetQuery(nameof(InsertOneTest)),
                controller: _restController,
                operationType: Operation.Insert,
                requestBody: requestBody,
                expectedStatusCode: 201,
                expectedLocationHeader: expectedLocationHeader
            );
        }

        /// <summary>
        /// Tests InsertOne into a table that has a composite primary key
        /// with a REST POST request.
        /// </summary>
        [TestMethod]
        public virtual async Task InsertOneInCompositeKeyTableTest()
        {
            string requestBody = @"
            {
                ""book_id"": ""1"",
                ""content"": ""Amazing book!""
            }";

            string expectedLocationHeader = $"/book_id/1/id/{STARTING_ID_FOR_TEST_INSERTS}";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: null,
                queryString: null,
                entity: _tableWithCompositePrimaryKey,
                sqlQuery: GetQuery(nameof(InsertOneInCompositeKeyTableTest)),
                controller: _restController,
                operationType: Operation.Insert,
                requestBody: requestBody,
                expectedStatusCode: 201,
                expectedLocationHeader: expectedLocationHeader
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
                ""title"": ""Batman Returns"",
                ""issueNumber"": 1234
            }";

            string expectedLocationHeader = $"/id/{STARTING_ID_FOR_TEST_INSERTS}";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: $"id/{STARTING_ID_FOR_TEST_INSERTS}",
                    queryString: null,
                    entity: _integration_NonAutoGenPK_TableName,
                    sqlQuery: GetQuery(nameof(PutOne_Insert_Test)),
                    controller: _restController,
                    operationType: Operation.Upsert,
                    requestBody: requestBody,
                    expectedStatusCode: 201,
                    expectedLocationHeader: expectedLocationHeader
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
        /// Tests the PutOne functionality with a REST PUT request
        /// with item that does NOT exist, AND primary key defined is autogenerated in schema,
        /// which results in a BadRequest.
        /// sqlQuery represents the query used to get 'expected' result of zero items.
        /// </summary>
        /// <returns></returns>
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
                    queryString: null,
                    entity: _integrationTableName,
                    sqlQuery: GetQuery(nameof(PutOne_Insert_PKAutoGen_Test)),
                    controller: _restController,
                    operationType: Operation.Upsert,
                    requestBody: requestBody,
                    exception: true,
                    expectedErrorMessage: $"Could not perform the given mutation on entity books.",
                    expectedStatusCode: (int)HttpStatusCode.InternalServerError,
                    expectedSubStatusCode: DatagatewayException.SubStatusCodes.DatabaseOperationFailed.ToString()
                );
        }

        /// <summary>
        /// Tests the PutOne functionality with a REST PUT request
        /// with item that does NOT exist, AND primary key defined is autogenerated in schema,
        /// which results in a BadRequest.
        /// sqlQuery represents the query used to get 'expected' result of zero items.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public virtual async Task PutOne_Insert_BadReq_NonNullable_Test()
        {
            string requestBody = @"
            {
                ""title"": ""The Hobbit Returns to The Shire""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "id/1000",
                    queryString: null,
                    entity: _integrationTableName,
                    sqlQuery: GetQuery(nameof(PutOne_Insert_BadReq_NonNullable_Test)),
                    controller: _restController,
                    operationType: Operation.Upsert,
                    requestBody: requestBody,
                    exception: true,
                    expectedErrorMessage: $"Invalid request body. Either insufficient or unnecessary " +
                                            "values for fields supplied.",
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
