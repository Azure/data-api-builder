using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Services;
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
                    operationType: Operation.Upsert,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK
                );

            requestBody = @"
            {
                ""content"": ""Good book to read""
            }";

            string expectedLocationHeader = $"book_id/1/id/568";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: expectedLocationHeader,
                queryString: null,
                entityNameOrPath: _entityWithCompositePrimaryKey,
                sqlQuery: GetQuery("PutOne_Update_Default_Test"),
                operationType: Operation.Upsert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.OK
                );

            requestBody = @"
            {
               ""categoryName"":""SciFi"",
               ""piecesAvailable"":""10"",
               ""piecesRequired"":""5""
            }";

            expectedLocationHeader = $"categoryid/2/pieceid/1";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: expectedLocationHeader,
                queryString: null,
                entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                sqlQuery: GetQuery("PutOne_Update_CompositeNonAutoGenPK_Test"),
                operationType: Operation.Upsert,
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

            expectedLocationHeader = $"categoryid/1/pieceid/1";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: expectedLocationHeader,
                queryString: null,
                entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                sqlQuery: GetQuery("PutOne_Update_NullOutMissingField_Test"),
                operationType: Operation.Upsert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.OK
            );

            requestBody = @"
            {
               ""categoryName"":"""",
               ""piecesAvailable"":""2"",
               ""piecesRequired"":""3""
            }";

            expectedLocationHeader = $"categoryid/2/pieceid/1";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: expectedLocationHeader,
                queryString: null,
                entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                sqlQuery: GetQuery("PutOne_Update_Empty_Test"),
                operationType: Operation.Upsert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.OK
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
                    entityNameOrPath: _integrationEntityName,
                    sqlQuery: GetQuery(nameof(PutOne_Update_IfMatchHeaders_Test)),
                    operationType: Operation.Upsert,
                    headers: new HeaderDictionary(headerDictionary),
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK
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

            string expectedLocationHeader = $"id/{STARTING_ID_FOR_TEST_INSERTS}";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: expectedLocationHeader,
                    queryString: null,
                    entityNameOrPath: _integration_NonAutoGenPK_EntityName,
                    sqlQuery: GetQuery(nameof(PutOne_Insert_Test)),
                    operationType: Operation.Upsert,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.Created,
                    expectedLocationHeader: expectedLocationHeader
                );

            // It should result in a successful insert,
            // where the nullable field 'issue_number' is properly left alone by the query validation methods.
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
                entityNameOrPath: _integration_NonAutoGenPK_EntityName,
                sqlQuery: GetQuery("PutOne_Insert_Nullable_Test"),
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
                ""title"": ""Star Trek"",
                ""categoryName"" : ""Suspense""
            }";

            expectedLocationHeader = $"id/{STARTING_ID_FOR_TEST_INSERTS}";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: expectedLocationHeader,
                queryString: null,
                entityNameOrPath: _integration_AutoGenNonPK_EntityName,
                sqlQuery: GetQuery("PutOne_Insert_AutoGenNonPK_Test"),
                operationType: Operation.Upsert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.Created,
                expectedLocationHeader: expectedLocationHeader
                );

            requestBody = @"
            {
               ""categoryName"":""SciFi"",
               ""piecesAvailable"":""2"",
               ""piecesRequired"":""1""
            }";

            expectedLocationHeader = $"categoryid/3/pieceid/1";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: expectedLocationHeader,
                queryString: null,
                entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                sqlQuery: GetQuery("PutOne_Insert_CompositeNonAutoGenPK_Test"),
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
                entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                sqlQuery: GetQuery("PutOne_Insert_Default_Test"),
                operationType: Operation.Upsert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.Created,
                expectedLocationHeader: expectedLocationHeader
                );

            requestBody = @"
            {
               ""categoryName"":"""",
               ""piecesAvailable"":""2"",
               ""piecesRequired"":""3""
            }";

            expectedLocationHeader = $"categoryid/4/pieceid/1";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: expectedLocationHeader,
                queryString: null,
                entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                sqlQuery: GetQuery("PutOne_Insert_Empty_Test"),
                operationType: Operation.Upsert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.Created,
                expectedLocationHeader: expectedLocationHeader
                );
        }

        /// <summary>
        /// Tests successful execution of PUT insert requests which try to
        /// modify fields belonging to one base table in the view.
        /// </summary>
        /// <returns></returns>
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
                operationType: Operation.Upsert,
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
            string expectedLocationHeader = $"categoryid/4/pieceid/1";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: expectedLocationHeader,
                    queryString: null,
                    entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                    sqlQuery: GetQuery("PutOne_Insert_Nulled_Test"),
                    operationType: Operation.Upsert,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.Created,
                    expectedLocationHeader: expectedLocationHeader
                );

            // Performs a successful PUT update when a nullable column
            // is specified as null in the request body.
            requestBody = @"
            {
               ""categoryName"":""FairyTales"",
               ""piecesAvailable"":null,
               ""piecesRequired"":""4""
            }";

            expectedLocationHeader = $"categoryid/2/pieceid/1";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: expectedLocationHeader,
                queryString: null,
                entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                sqlQuery: GetQuery("PutOne_Update_Nulled_Test"),
                operationType: Operation.Upsert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.OK
                );
        }

        /// <summary>
        /// Tests successful execution of PUT update requests which try to
        /// modify fields belonging to one base table in the view.
        /// </summary>
        /// <returns></returns>
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
                    operationType: Operation.Upsert,
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
                    operationType: Operation.Upsert,
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
                    operationType: Operation.Upsert,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK
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
                operationType: Operation.Upsert,
                requestBody: requestBody,
                exceptionExpected: true,
                expectedErrorMessage: "Invalid request body. Missing field in body: categoryName.",
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: "BadRequest"
            );
        }

        [TestMethod]
        public virtual async Task PutOneUpdateNonNullableDefaultFieldMissingFromJsonBodyTest()
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
                operationType: Operation.Upsert,
                requestBody: requestBody,
                exceptionExpected: true,
                expectedErrorMessage: expectedErrorMessage,
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: $"{DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed}"
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
                    entityNameOrPath: _integrationEntityName,
                    sqlQuery: string.Empty,
                    operationType: Operation.Upsert,
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
                    entityNameOrPath: _integrationEntityName,
                    sqlQuery: string.Empty,
                    operationType: Operation.Upsert,
                    requestBody: requestBody,
                    exceptionExpected: true,
                    expectedErrorMessage: $"Cannot perform INSERT and could not find {_integrationEntityName} with primary key <id: 1000> to perform UPDATE on.",
                    expectedStatusCode: HttpStatusCode.NotFound,
                    expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.EntityNotFound.ToString()
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
                    operationType: Operation.Upsert,
                    requestBody: requestBody,
                    exceptionExpected: true,
                    expectedErrorMessage: $"Cannot perform INSERT and could not find {_entityWithCompositePrimaryKey} with primary key <id: 5002, book_id: 1> to perform UPDATE on.",
                    expectedStatusCode: HttpStatusCode.NotFound,
                    expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.EntityNotFound.ToString()
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
                    entityNameOrPath: _integrationEntityName,
                    sqlQuery: string.Empty,
                    operationType: Operation.Upsert,
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
                    operationType: Operation.Upsert,
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
                operationType: Operation.Upsert,
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
                    primaryKeyRoute: "id/18",
                    queryString: string.Empty,
                    entityNameOrPath: _integrationEntityName,
                    sqlQuery: string.Empty,
                    operationType: Operation.Upsert,
                    headers: new HeaderDictionary(headerDictionary),
                    requestBody: requestBody,
                    exceptionExpected: true,
                    expectedErrorMessage: "No Update could be performed, record not found",
                    expectedStatusCode: HttpStatusCode.PreconditionFailed,
                    expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed.ToString()
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
                    operationType: Operation.Upsert,
                    requestBody: requestBody,
                    exceptionExpected: true,
                    expectedErrorMessage: RequestValidator.PRIMARY_KEY_NOT_PROVIDED_ERR_MESSAGE,
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
                operationType: Operation.Upsert,
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
                operationType: Operation.Upsert,
                requestBody: requestBody,
                exceptionExpected: true,
                expectedErrorMessage: "Invalid value for field categoryName in request body.",
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        /// <summary>
        /// Test to verify that we throw exception for invalid/bad
        /// PUT requests on views.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public virtual async Task PutOneInViewBadRequest()
        {
            // PUT update trying to modify fields from multiple base table
            // will result in error.
            string requestBody = @"
            {
                ""categoryName"": ""SciFi"",
                ""is_wholesale_price"": false
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "categoryid/1/pieceid/1/phase/instant2",
                queryString: string.Empty,
                entityNameOrPath: _composite_subset_stocksPrice,
                sqlQuery: string.Empty,
                operationType: Operation.Upsert,
                requestBody: requestBody,
                exceptionExpected: true,
                expectedErrorMessage: $"View or function '{_defaultSchemaName}.{_composite_subset_stocksPrice}' is not updatable " +
                "because the modification affects multiple base tables.",
                expectedStatusCode: HttpStatusCode.InternalServerError,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed.ToString()
            );

            requestBody = @"
            {
               ""is_wholesale_price"": true
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "categoryid/0/pieceid/1/instance/instant3",
                queryString: null,
                entityNameOrPath: _composite_subset_stocksPrice,
                sqlQuery: string.Empty,
                operationType: Operation.Upsert,
                requestBody: requestBody,
                exceptionExpected: true,
                expectedErrorMessage: "Primary key column: instance not found in the entity definition.",
                expectedStatusCode: HttpStatusCode.NotFound,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.EntityNotFound.ToString()
                );

            // Put update on composite view resolving to stocks_price table.
            requestBody = @"
            {
                ""is_wholesale_price"": true
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "categoryid/1/pieceid/1/phase/instant2",
                    queryString: null,
                    entityNameOrPath: _composite_subset_stocksPrice,
                    sqlQuery: string.Empty,
                    operationType: Operation.Upsert,
                    requestBody: requestBody,
                    exceptionExpected: true,
                    expectedErrorMessage: $"View or function '{_defaultSchemaName}.{_composite_subset_stocksPrice}' is not updatable " +
                    "because the modification affects multiple base tables.",
                    expectedStatusCode: HttpStatusCode.InternalServerError,
                    expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed.ToString()
                );
        }
        #endregion
    }
}
