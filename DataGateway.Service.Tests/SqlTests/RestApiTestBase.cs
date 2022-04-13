using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using System.Web;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Resolvers;
using Azure.DataGateway.Service.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
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
        protected static readonly string _integration_AutoGenNonPK_TableName = "comics";
        protected static readonly string _Composite_NonAutoGenPK = "stocks";

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
                queryString: "?$f=id,title",
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
                queryString: "?$f=id",
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
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        [TestMethod]
        public async Task FindTestWithPrimaryKeyContainingForeignKey()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/567/book_id/1",
                queryString: "?$f=id,content",
                entity: _tableWithCompositePrimaryKey,
                sqlQuery: GetQuery(nameof(FindTestWithPrimaryKeyContainingForeignKey)),
                controller: _restController
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation using $first to
        /// limit the number of records returned with a single primary
        /// key in the table.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFirstSingleKeyPagination()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$first=1",
                entity: _integrationTableName,
                sqlQuery: GetQuery(nameof(FindTestWithFirstSingleKeyPagination)),
                controller: _restController,
                expectedAfterQueryString: $"&$after={HttpUtility.UrlEncode(SqlPaginationUtil.Base64Encode("{\"id\":1}"))}",
                paginated: true
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation using $first to
        /// limit the number of records returned with multiple column
        /// primary key in the table.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFirstMultiKeyPagination()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$first=1",
                entity: _tableWithCompositePrimaryKey,
                sqlQuery: GetQuery(nameof(FindTestWithFirstMultiKeyPagination)),
                controller: _restController,
                expectedAfterQueryString: $"&$after={HttpUtility.UrlEncode(SqlPaginationUtil.Base64Encode("{\"book_id\":1,\"id\":567}"))}",
                paginated: true
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation using $after to
        /// get the desired page with a single column primary key.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithAfterSingleKeyPagination()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: $"?$after={HttpUtility.UrlEncode(SqlPaginationUtil.Base64Encode("{\"id\":7}"))}",
                entity: _integrationTableName,
                sqlQuery: GetQuery(nameof(FindTestWithAfterSingleKeyPagination)),
                controller: _restController
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation using $after to
        /// get the desired page with a multi column primary key.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithAfterMultiKeyPagination()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: $"?$after={HttpUtility.UrlEncode(SqlPaginationUtil.Base64Encode("{\"book_id\":1,\"id\":567}"))}",
                entity: _tableWithCompositePrimaryKey,
                sqlQuery: GetQuery(nameof(FindTestWithAfterMultiKeyPagination)),
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

            string expectedLocationHeader = $"id/{STARTING_ID_FOR_TEST_INSERTS}";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: null,
                queryString: null,
                entity: _integrationTableName,
                sqlQuery: GetQuery(nameof(InsertOneTest)),
                controller: _restController,
                operationType: Operation.Insert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.Created,
                expectedLocationHeader: expectedLocationHeader
            );

            requestBody = @"
            {
                ""categoryid"": ""5"",
                ""pieceid"": ""2"",
                ""categoryName"":""Thriller""
            }";

            expectedLocationHeader = $"categoryid/5/pieceid/2";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: null,
                queryString: null,
                entity: _Composite_NonAutoGenPK,
                sqlQuery: GetQuery("InsertOneInCompositeNonAutoGenPKTest"),
                controller: _restController,
                operationType: Operation.Insert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.Created,
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

            string expectedLocationHeader = $"book_id/1/id/{STARTING_ID_FOR_TEST_INSERTS}";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: null,
                queryString: null,
                entity: _tableWithCompositePrimaryKey,
                sqlQuery: GetQuery(nameof(InsertOneInCompositeKeyTableTest)),
                controller: _restController,
                operationType: Operation.Insert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.Created,
                expectedLocationHeader: expectedLocationHeader
            );

            requestBody = @"
            {
                 ""book_id"": ""2""
            }";

            expectedLocationHeader = $"book_id/2/id/{STARTING_ID_FOR_TEST_INSERTS + 1}";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: null,
                queryString: null,
                entity: _tableWithCompositePrimaryKey,
                sqlQuery: GetQuery("InsertOneInDefaultTestTable"),
                controller: _restController,
                operationType: Operation.Insert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.Created,
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
                    expectedStatusCode: HttpStatusCode.NoContent
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
                    expectedStatusCode: HttpStatusCode.NoContent
                );

            requestBody = @"
            {
                ""content"": ""Good book to read""
            }";

            string expectedLocationHeader = $"book_id/1/id/568";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: expectedLocationHeader,
                queryString: null,
                entity: _tableWithCompositePrimaryKey,
                sqlQuery: GetQuery("PutOne_Update_Default_Test"),
                controller: _restController,
                operationType: Operation.Upsert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.NoContent,
                expectedLocationHeader: expectedLocationHeader
                );

            requestBody = @"
            {
               ""categoryName"":""History"",
               ""piecesAvailable"":""10"",
               ""piecesRequired"":""5""
            }";

            expectedLocationHeader = $"categoryid/2/pieceid/1";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: expectedLocationHeader,
                queryString: null,
                entity: _Composite_NonAutoGenPK,
                sqlQuery: GetQuery("PutOne_Update_CompositeNonAutoGenPK_Test"),
                controller: _restController,
                operationType: Operation.Upsert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.NoContent,
                expectedLocationHeader: expectedLocationHeader
                );
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
                    entity: _integrationTableName,
                    sqlQuery: string.Empty,
                    controller: _restController,
                    operationType: Operation.Upsert,
                    headers: new HeaderDictionary(headerDictionary),
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.NoContent
                );
            await SetupAndRunRestApiTest(
                  primaryKeyRoute: "id/1",
                  queryString: "?$filter=title eq 'The Return of the King'",
                  entity: _integrationTableName,
                  sqlQuery: GetQuery("PutOne_Update_IfMatchHeaders_Test_Confirm_Update"),
                  controller: _restController,
                  operationType: Operation.Find,
                  expectedStatusCode: HttpStatusCode.OK);
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
                ""issueNumber"": 1234
            }";

            string expectedLocationHeader = $"id/{STARTING_ID_FOR_TEST_INSERTS}";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: expectedLocationHeader,
                    queryString: null,
                    entity: _integration_NonAutoGenPK_TableName,
                    sqlQuery: GetQuery(nameof(PutOne_Insert_Test)),
                    controller: _restController,
                    operationType: Operation.Upsert,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.Created,
                    expectedLocationHeader: expectedLocationHeader
                );

            // It should result in a successful insert,
            // where the nullable field 'issueNumber' is properly left alone by the query validation methods.
            // The request body doesn't contain this field that neither has a default
            // nor is autogenerated.
            requestBody = @"
            {
                ""title"": ""Times""
            }";

            expectedLocationHeader = $"id/{STARTING_ID_FOR_TEST_INSERTS + 1}";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: expectedLocationHeader,
                queryString: null,
                entity: _integration_NonAutoGenPK_TableName,
                sqlQuery: GetQuery("PutOne_Insert_Nullable_Test"),
                controller: _restController,
                operationType: Operation.Upsert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.Created,
                expectedLocationHeader: expectedLocationHeader
                );

            // It should result in a successful insert,
            // where the autogen'd field 'volume' is properly populated by the db.
            // The request body doesn't contain this non-nullable, non primary key
            // that is autogenerated.
            requestBody = @"
            {
                ""title"": ""Star Trek""
            }";

            expectedLocationHeader = $"id/{STARTING_ID_FOR_TEST_INSERTS}";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: expectedLocationHeader,
                queryString: null,
                entity: _integration_AutoGenNonPK_TableName,
                sqlQuery: GetQuery("PutOne_Insert_AutoGenNonPK_Test"),
                controller: _restController,
                operationType: Operation.Upsert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.Created,
                expectedLocationHeader: expectedLocationHeader
                );

            requestBody = @"
            {
               ""categoryName"":""comics"",
               ""piecesAvailable"":""2"",
               ""piecesRequired"":""1""
            }";

            expectedLocationHeader = $"categoryid/3/pieceid/1";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: expectedLocationHeader,
                queryString: null,
                entity: _Composite_NonAutoGenPK,
                sqlQuery: GetQuery("PutOne_Insert_CompositeNonAutoGenPK_Test"),
                controller: _restController,
                operationType: Operation.Upsert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.Created,
                expectedLocationHeader: expectedLocationHeader
                );

            requestBody = @"
            {
               ""categoryName"":""SciFi""
            }";

            expectedLocationHeader = $"categoryid/8/pieceid/1";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: expectedLocationHeader,
                queryString: null,
                entity: _Composite_NonAutoGenPK,
                sqlQuery: GetQuery("PutOne_Insert_Default_Test"),
                controller: _restController,
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
                ""issueNumber"": 1234
            }";

            string expectedLocationHeader = $"id/2";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: $"id/2",
                    queryString: null,
                    entity: _integration_NonAutoGenPK_TableName,
                    sqlQuery: GetQuery(nameof(PatchOne_Insert_NonAutoGenPK_Test)),
                    controller: _restController,
                    operationType: Operation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.Created,
                    expectedLocationHeader: expectedLocationHeader
                );

            requestBody = @"
            {
                ""categoryName"": ""Suspense"",
                ""piecesAvailable"":""5"",
                ""piecesRequired"":""4""
            }";
            expectedLocationHeader = $"categoryid/4/pieceid/1";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: expectedLocationHeader,
                    queryString: null,
                    entity: _Composite_NonAutoGenPK,
                    sqlQuery: GetQuery("PatchOne_Insert_CompositeNonAutoGenPK_Test"),
                    controller: _restController,
                    operationType: Operation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.Created,
                    expectedLocationHeader: expectedLocationHeader
                );

            requestBody = @"
            {
                ""categoryName"": ""Drama""
            }";
            expectedLocationHeader = $"categoryid/7/pieceid/1";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: expectedLocationHeader,
                    queryString: null,
                    entity: _Composite_NonAutoGenPK,
                    sqlQuery: GetQuery("PatchOne_Insert_Default_Test"),
                    controller: _restController,
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
        /// Expects: 201 Created where sqlQuery validates update.
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
                    entity: _integrationTableName,
                    sqlQuery: GetQuery(nameof(PatchOne_Update_Test)),
                    controller: _restController,
                    operationType: Operation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.NoContent
                );

            requestBody = @"
            {
                ""content"": ""That's a great book""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "id/567/book_id/1",
                    queryString: null,
                    entity: _tableWithCompositePrimaryKey,
                    sqlQuery: GetQuery("PatchOne_Update_Default_Test"),
                    controller: _restController,
                    operationType: Operation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.NoContent
                );

            requestBody = @"
            {
                ""piecesAvailable"": ""10""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "categoryid/1/pieceid/1",
                    queryString: null,
                    entity: _Composite_NonAutoGenPK,
                    sqlQuery: GetQuery("PatchOne_Update_CompositeNonAutoGenPK_Test"),
                    controller: _restController,
                    operationType: Operation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.NoContent
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
                    entity: _integrationTableName,
                    sqlQuery: string.Empty,
                    controller: _restController,
                    operationType: Operation.UpsertIncremental,
                    headers: new HeaderDictionary(headerDictionary),
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.NoContent
                );

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "id/1",
                    queryString: "?$filter=title eq 'The Hobbit Returns to The Shire' and publisher_id eq 1234",
                    entity: _integrationTableName,
                    sqlQuery: GetQuery("PatchOne_Update_IfMatchHeaders_Test_Confirm_Update"),
                    controller: _restController,
                    operationType: Operation.Find
                );
        }

        #endregion

        #region Negative Tests

        /// <summary>
        /// Tests the REST Api for Find operation using $first=0
        /// to request 0 records, which should throw a DataGateway
        /// Exception.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFirstZeroSingleKeyPagination()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$first=0",
                entity: _integrationTableName,
                sqlQuery: string.Empty,
                controller: _restController,
                exception: true,
                expectedErrorMessage: "Invalid number of items requested, $first must be an integer greater than 0. Actual value: 0",
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation using a keyword
        /// of 'startswith' which is not currently supported, verify
        /// we throw an internal server error.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithUnsupportedStartsWithFilterKeyword()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=startswith(title, 'Awesome') eq true",
                entity: _integrationTableName,
                sqlQuery: string.Empty,
                controller: _restController,
                exception: true,
                expectedErrorMessage: "While processing your request the server ran into an unexpected error.",
                expectedStatusCode: HttpStatusCode.InternalServerError,
                expectedSubStatusCode: "UnexpectedError"
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operations using keywords
        /// of which is not currently supported, verify
        /// we throw an internal server error.
        /// </summary>
        [DataTestMethod]
        [DataRow("endswith", "(title, 'book')", "eq true")]
        [DataRow("indexof", "(title, 'Awe')", "eq 0")]
        [DataRow("length", "(title)", "gt 5")]
        public async Task FindTestWithUnsupportedFilterKeywords(string keyword, string value, string compareTo)
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: $"?$filter={keyword}{value} {compareTo}",
                entity: _integrationTableName,
                sqlQuery: string.Empty,
                controller: _restController,
                exception: true,
                expectedErrorMessage: "While processing your request the server ran into an unexpected error.",
                expectedStatusCode: HttpStatusCode.InternalServerError,
                expectedSubStatusCode: "UnexpectedError"
            );
        }

        /// <summary>
        /// Tests the InsertOne functionality with disallowed URL composition: contains Query String.
        /// </summary>
        [TestMethod]
        public virtual async Task InsertOneWithInvalidQueryStringTest()
        {
            string requestBody = @"
            {
                ""title"": ""My New Book"",
                ""publisher_id"": 1234
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?/id/5001",
                entity: _integrationTableName,
                sqlQuery: string.Empty,
                controller: _restController,
                operationType: Operation.Insert,
                requestBody: requestBody,
                exception: true,
                expectedErrorMessage: "Query string for POST requests is an invalid url.",
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        /// <summary>
        /// Tests the InsertOne functionality with disallowed request composition: array in request body.
        /// </summary>
        [TestMethod]
        public virtual async Task InsertOneWithInvalidArrayJsonBodyTest()
        {
            string requestBody = @"
            [{
                ""title"": ""My New Book"",
                ""publisher_id"": 1234
            }]";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entity: _integrationTableName,
                sqlQuery: string.Empty,
                controller: _restController,
                operationType: Operation.Insert,
                requestBody: requestBody,
                exception: true,
                expectedErrorMessage: "Mutation operation on many instances of an entity in a single request are not yet supported.",
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: "BadRequest"
            );
        }

        /// <summary>
        /// Tests the InsertOne functionality with a request body containing values that do not match the value type defined in the schema.
        /// </summary>
        [TestMethod]
        public virtual async Task InsertOneWithInvalidTypeInJsonBodyTest()
        {
            string requestBody = @"
            {
                ""title"": [""My New Book"", ""Another new Book"", {""author"": ""unknown""}],
                ""publisher_id"": [1234, 4321]
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entity: _integrationTableName,
                sqlQuery: string.Empty,
                controller: _restController,
                operationType: Operation.Insert,
                requestBody: requestBody,
                exception: true,
                expectedErrorMessage: "Parameter \"[1234, 4321]\" cannot be resolved as column \"publisher_id\" with type \"Int64\".",
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: "BadRequest"
            );
        }

        /// <summary>
        /// Tests the InsertOne functionality with no valid fields in the request body.
        /// </summary>
        [TestMethod]
        public virtual async Task InsertOneWithNoValidFieldInJsonBodyTest()
        {
            string requestBody = @"
            {}";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entity: _integrationTableName,
                sqlQuery: string.Empty,
                controller: _restController,
                operationType: Operation.Insert,
                requestBody: requestBody,
                exception: true,
                expectedErrorMessage: "Invalid request body. Missing field in body: title.",
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        /// <summary>
        /// Tests the InsertOne functionality with an invalid field in the request body:
        /// Primary Key in the request body for table with Autogenerated PK.
        /// </summary>
        [TestMethod]
        public virtual async Task InsertOneWithAutoGeneratedPrimaryKeyInJsonBodyTest()
        {
            string requestBody = @"
            {
                ""id"": " + STARTING_ID_FOR_TEST_INSERTS +
            "}";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entity: _integrationTableName,
                sqlQuery: string.Empty,
                controller: _restController,
                operationType: Operation.Insert,
                requestBody: requestBody,
                exception: true,
                expectedErrorMessage: "Invalid request body. Field not allowed in body: id.",
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        /// <summary>
        /// Tests the InsertOne functionality with a missing field from the request body:
        /// Missing non auto generated Primary Key in Json Body.
        /// </summary>
        [TestMethod]
        public virtual async Task InsertOneWithNonAutoGeneratedPrimaryKeyMissingInJsonBodyTest()
        {
            string requestBody = @"
            {
                ""title"": ""My New Book""
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entity: _integration_NonAutoGenPK_TableName,
                sqlQuery: string.Empty,
                controller: _restController,
                operationType: Operation.Insert,
                requestBody: requestBody,
                exception: true,
                expectedErrorMessage: "Invalid request body. Missing field in body: id.",
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: "BadRequest"
            );
        }

        /// <summary>
        /// Tests the InsertOne functionality with a missing field in the request body:
        /// A non-nullable field in the Json Body is missing.
        /// </summary>
        [TestMethod]
        public virtual async Task InsertOneWithNonNullableFieldMissingInJsonBodyTest()
        {
            string requestBody = @"
            {
                ""id"": " + STARTING_ID_FOR_TEST_INSERTS + ",\n" +
                "\"issueNumber\": 1234\n" +
            "}";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entity: _integration_NonAutoGenPK_TableName,
                sqlQuery: string.Empty,
                controller: _restController,
                operationType: Operation.Insert,
                requestBody: requestBody,
                exception: true,
                expectedErrorMessage: "Invalid request body. Missing field in body: title.",
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: "BadRequest"
            );

            requestBody = @"
            {
                ""categoryid"":""6"",
                ""pieceid"":""1""
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: null,
                queryString: string.Empty,
                entity: _Composite_NonAutoGenPK,
                sqlQuery: string.Empty,
                controller: _restController,
                operationType: Operation.Insert,
                requestBody: requestBody,
                exception: true,
                expectedErrorMessage: "Invalid request body. Missing field in body: categoryName.",
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: "BadRequest"
            );
        }

        [TestMethod]
        public virtual async Task PutOneWithNonNullableFieldMissingInJsonBodyTest()
        {
            string requestBody = @"
            {
                ""piecesRequired"":""6""
            }";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: "categoryid/1/pieceid/1",
                queryString: string.Empty,
                entity: _Composite_NonAutoGenPK,
                sqlQuery: string.Empty,
                controller: _restController,
                operationType: Operation.Upsert,
                requestBody: requestBody,
                exception: true,
                expectedErrorMessage: "Invalid request body. Missing field in body: categoryName.",
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: "BadRequest"
            );
        }

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
                    entity: _integrationTableName,
                    sqlQuery: null,
                    controller: _restController,
                    operationType: Operation.UpsertIncremental,
                    requestBody: requestBody,
                    exception: true,
                    expectedErrorMessage: $"Cannot perform INSERT and could not find books with primary key <id: 1000> to perform UPDATE on.",
                    expectedStatusCode: HttpStatusCode.NotFound,
                    expectedSubStatusCode: DataGatewayException.SubStatusCodes.EntityNotFound.ToString()
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
                ""issueNumber"": ""1234""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "id/1000",
                    queryString: null,
                    entity: _integration_NonAutoGenPK_TableName,
                    sqlQuery: null,
                    controller: _restController,
                    operationType: Operation.UpsertIncremental,
                    requestBody: requestBody,
                    exception: true,
                    expectedErrorMessage: "Cannot perform INSERT and could not find magazines with primary key <id: 1000> to perform UPDATE on.",
                    expectedStatusCode: HttpStatusCode.NotFound,
                    expectedSubStatusCode: DataGatewayException.SubStatusCodes.EntityNotFound.ToString()
                );
        }

        /// <summary>
        /// Tests the PatchOne functionality with a REST PUT request using
        /// headers that include as a key "If-Match" with an item that does not exist,
        /// resulting in a DataGatewayException with status code of Precondition Failed.
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
                    entity: _integrationTableName,
                    sqlQuery: string.Empty,
                    controller: _restController,
                    operationType: Operation.UpsertIncremental,
                    headers: new HeaderDictionary(headerDictionary),
                    requestBody: requestBody,
                    exception: true,
                    expectedErrorMessage: "No Update could be performed, record not found",
                    expectedStatusCode: HttpStatusCode.PreconditionFailed,
                    expectedSubStatusCode: DataGatewayException.SubStatusCodes.DatabaseOperationFailed.ToString()
                );
        }

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
                    queryString: string.Empty,
                    entity: _integrationTableName,
                    sqlQuery: string.Empty,
                    controller: _restController,
                    operationType: Operation.Upsert,
                    requestBody: requestBody,
                    exception: true,
                    expectedErrorMessage: "Invalid request body. Missing field in body: publisher_id.",
                    expectedStatusCode: HttpStatusCode.BadRequest,
                    expectedSubStatusCode: DataGatewayException.SubStatusCodes.BadRequest.ToString()
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
                    queryString: string.Empty,
                    entity: _integrationTableName,
                    sqlQuery: string.Empty,
                    controller: _restController,
                    operationType: Operation.Upsert,
                    requestBody: requestBody,
                    exception: true,
                    expectedErrorMessage: $"Cannot perform INSERT and could not find books with primary key <id: 1000> to perform UPDATE on.",
                    expectedStatusCode: HttpStatusCode.NotFound,
                    expectedSubStatusCode: DataGatewayException.SubStatusCodes.EntityNotFound.ToString()
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
                    entity: _tableWithCompositePrimaryKey,
                    sqlQuery: string.Empty,
                    controller: _restController,
                    operationType: Operation.Upsert,
                    requestBody: requestBody,
                    exception: true,
                    expectedErrorMessage: $"Cannot perform INSERT and could not find reviews with primary key <id: 5002, book_id: 1> to perform UPDATE on.",
                    expectedStatusCode: HttpStatusCode.NotFound,
                    expectedSubStatusCode: DataGatewayException.SubStatusCodes.EntityNotFound.ToString()
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
                    queryString: string.Empty,
                    entity: _integrationTableName,
                    sqlQuery: string.Empty,
                    controller: _restController,
                    operationType: Operation.Upsert,
                    requestBody: requestBody,
                    exception: true,
                    expectedErrorMessage: $"Invalid request body. Missing field in body: publisher_id.",
                    expectedStatusCode: HttpStatusCode.BadRequest,
                    expectedSubStatusCode: DataGatewayException.SubStatusCodes.BadRequest.ToString()
                );

            requestBody = @"
            {
                ""piecesAvailable"": ""7""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "categoryid/1/pieceid/1",
                    queryString: string.Empty,
                    entity: _Composite_NonAutoGenPK,
                    sqlQuery: string.Empty,
                    controller: _restController,
                    operationType: Operation.Upsert,
                    requestBody: requestBody,
                    exception: true,
                    expectedErrorMessage: $"Invalid request body. Missing field in body: categoryName.",
                    expectedStatusCode: HttpStatusCode.BadRequest,
                    expectedSubStatusCode: DataGatewayException.SubStatusCodes.BadRequest.ToString()
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
                entity: _integration_AutoGenNonPK_TableName,
                sqlQuery: string.Empty,
                controller: _restController,
                operationType: Operation.Upsert,
                requestBody: requestBody,
                expectedErrorMessage: @"Invalid request body. Either insufficient or extra fields supplied.",
                expectedStatusCode: HttpStatusCode.BadRequest
                );
        }

        /// <summary>
        /// Tests the PutOne functionality with a REST PUT request using
        /// headers that include as a key "If-Match" with an item that does not exist,
        /// resulting in a DataGatewayException with status code of Precondition Failed.
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
                    primaryKeyRoute: "id/18",
                    queryString: string.Empty,
                    entity: _integrationTableName,
                    sqlQuery: string.Empty,
                    controller: _restController,
                    operationType: Operation.Upsert,
                    headers: new HeaderDictionary(headerDictionary),
                    requestBody: requestBody,
                    exception: true,
                    expectedErrorMessage: "No Update could be performed, record not found",
                    expectedStatusCode: HttpStatusCode.PreconditionFailed,
                    expectedSubStatusCode: DataGatewayException.SubStatusCodes.DatabaseOperationFailed.ToString()
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
                ""issueNumber"": 1234
            }";

            string expectedLocationHeader = $"id/{STARTING_ID_FOR_TEST_INSERTS}";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: string.Empty,
                    queryString: null,
                    entity: _integration_NonAutoGenPK_TableName,
                    sqlQuery: string.Empty,
                    controller: _restController,
                    operationType: Operation.Upsert,
                    requestBody: requestBody,
                    exception: true,
                    expectedErrorMessage: "Primary Key for UPSERT requests is required.",
                    expectedStatusCode: HttpStatusCode.BadRequest,
                    expectedLocationHeader: expectedLocationHeader
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
                    queryString: string.Empty,
                    entity: _integrationTableName,
                    sqlQuery: string.Empty,
                    controller: _restController,
                    operationType: Operation.Delete,
                    requestBody: string.Empty,
                    exception: true,
                    expectedErrorMessage: "Not Found",
                    expectedStatusCode: HttpStatusCode.NotFound,
                    expectedSubStatusCode: DataGatewayException.SubStatusCodes.EntityNotFound.ToString()
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
                    queryString: string.Empty,
                    entity: _integrationTableName,
                    sqlQuery: string.Empty,
                    controller: _restController,
                    operationType: Operation.Delete,
                    requestBody: string.Empty,
                    exception: true,
                    expectedErrorMessage: "The request is invalid since the primary keys: title requested were not found in the entity definition.",
                    expectedStatusCode: HttpStatusCode.BadRequest,
                    expectedSubStatusCode: DataGatewayException.SubStatusCodes.BadRequest.ToString()
                );
        }

        /// <summary>
        /// DeleteWithoutPrimaryKey attempts to operate on a single entity but with
        /// no primary key route. No sqlQuery value is provided as this request
        /// should fail prior to querying the database.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task DeleteWithOutPrimaryKeyTest()
        {
            await SetupAndRunRestApiTest(
                    primaryKeyRoute: string.Empty,
                    queryString: string.Empty,
                    entity: _integrationTableName,
                    sqlQuery: string.Empty,
                    controller: _restController,
                    operationType: Operation.Delete,
                    requestBody: string.Empty,
                    exception: true,
                    expectedErrorMessage: "Primary Key for DELETE requests is required.",
                    expectedStatusCode: HttpStatusCode.BadRequest,
                    expectedSubStatusCode: DataGatewayException.SubStatusCodes.BadRequest.ToString()
                );
        }

        /// <summary>
        /// Tests the REST Api for FindById operation with an invalid Primary Key Route.
        /// </summary>
        [TestMethod]
        public async Task FindByIdTestInvalidPrimaryKeyRoute()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/",
                queryString: string.Empty,
                entity: _integrationTableName,
                sqlQuery: string.Empty,
                controller: _restController,
                exception: true,
                expectedErrorMessage: "The request is invalid since it contains a primary key with no value specified.",
                expectedStatusCode: HttpStatusCode.BadRequest
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
                queryString: "?$f=id,content",
                entity: _integrationTableName,
                sqlQuery: string.Empty,
                controller: _restController,
                exception: true,
                expectedErrorMessage: "Invalid Column name requested: content",
                expectedStatusCode: HttpStatusCode.BadRequest
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
                queryString: "?$f=id,null",
                entity: _integrationTableName,
                sqlQuery: string.Empty,
                controller: _restController,
                exception: true,
                expectedErrorMessage: RestController.SERVER_ERROR,
                expectedStatusCode: HttpStatusCode.InternalServerError,
                expectedSubStatusCode: DataGatewayException.SubStatusCodes.UnexpectedError.ToString()
            );
        }

        /// <summary>
        /// Tests the REST Api for the correct error condition format when
        /// a DataGatewayException is thrown
        /// </summary>
        [TestMethod]
        public async Task RestDataGatewayExceptionErrorConditionFormat()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$f=id,content",
                entity: _integrationTableName,
                sqlQuery: string.Empty,
                controller: _restController,
                exception: true,
                expectedErrorMessage: "Invalid Column name requested: content",
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        #endregion
    }
}
