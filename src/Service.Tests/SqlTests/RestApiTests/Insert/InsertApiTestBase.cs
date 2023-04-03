// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests.Insert
{
    /// <summary>
    /// Test REST Apis validating expected results are obtained.
    /// </summary>
    [TestClass]
    public abstract class InsertApiTestBase : RestApiTestBase
    {
        #region Positive Tests
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
                entityNameOrPath: _integrationEntityName,
                sqlQuery: GetQuery(nameof(InsertOneTest)),
                operationType: Config.Operation.Insert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.Created,
                expectedLocationHeader: expectedLocationHeader
            );

            requestBody = @"
            {
                ""categoryid"": ""5"",
                ""pieceid"": ""2"",
                ""categoryName"":""Tales""
            }";

            expectedLocationHeader = $"categoryid/5/pieceid/2";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: null,
                queryString: null,
                entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                sqlQuery: GetQuery("InsertOneInCompositeNonAutoGenPKTest"),
                operationType: Config.Operation.Insert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.Created,
                expectedLocationHeader: expectedLocationHeader
            );
        }

        /// <summary>
        /// Tests insertion on simple/composite views.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public virtual async Task InsertOneInViewTest()
        {
            // Insert on simple view containing all fields from base table.
            string requestBody = @"
            {
                ""title"": ""My New Book"",
                ""publisher_id"": 1234
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: null,
                queryString: null,
                entityNameOrPath: _simple_all_books,
                sqlQuery: GetQuery("InsertOneInBooksViewAll"),
                operationType: Config.Operation.Insert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.Created
            );

            // Insertion on a simple view based on one table where not
            // all the fields from base table are selected in view.
            // The missing field has to be nullable or has default value.
            requestBody = @"
            {
                ""categoryid"": 4,
                ""pieceid"": 1,
                ""categoryName"": ""SciFi""
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: null,
                queryString: null,
                entityNameOrPath: _simple_subset_stocks,
                sqlQuery: GetQuery("InsertOneInStocksViewSelected"),
                operationType: Config.Operation.Insert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.Created
            );
        }

        /// <summary>
        /// Tests the InsertOne functionality with a REST POST request using
        /// unique unicode characters in the exposed names.
        /// </summary>
        [TestMethod]
        public virtual async Task InsertOneUniqueCharactersTest()
        {
            string requestBody = @"
            {
                ""┬─┬ノ( º _ ºノ)"": 2,
                ""始計"": ""new chapter 1 notes: "",
                ""作戰"": ""new chapter 2 notes: "",
                ""謀攻"": ""new chapter 3 notes: ""
            }";

            string expectedLocationHeader = $"┬─┬ノ( º _ ºノ)/2";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: null,
                queryString: null,
                entityNameOrPath: _integrationUniqueCharactersEntity,
                sqlQuery: GetQuery(nameof(InsertOneUniqueCharactersTest)),
                operationType: Config.Operation.Insert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.Created,
                expectedLocationHeader: expectedLocationHeader
            );
        }

        /// <summary>
        /// Tests the InsertOne functionality with a REST POST request
        /// where the entity has mapping defined for its columns.
        /// </summary>
        [TestMethod]
        public virtual async Task InsertOneWithMappingTest()
        {
            string requestBody = @"
            {
                ""treeId"" : 3,
                ""Scientific Name"": ""Cupressus Sempervirens"",
                ""United State's Region"": ""South East""
            }";

            string expectedLocationHeader = $"treeId/3";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: null,
                queryString: null,
                entityNameOrPath: _integrationMappingEntity,
                sqlQuery: GetQuery(nameof(InsertOneWithMappingTest)),
                operationType: Config.Operation.Insert,
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
                entityNameOrPath: _entityWithCompositePrimaryKey,
                sqlQuery: GetQuery(nameof(InsertOneInCompositeKeyTableTest)),
                operationType: Config.Operation.Insert,
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
                entityNameOrPath: _entityWithCompositePrimaryKey,
                sqlQuery: GetQuery("InsertOneInDefaultTestTable"),
                operationType: Config.Operation.Insert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.Created,
                expectedLocationHeader: expectedLocationHeader
            );
        }

        [TestMethod]
        public virtual async Task InsertOneWithNullFieldValue()
        {
            string requestBody = @"
            {
                ""categoryid"": ""3"",
                ""pieceid"": ""1"",
                ""piecesAvailable"": null,
                ""piecesRequired"": 1,
                ""categoryName"":""SciFi""
            }";

            string expectedLocationHeader = $"categoryid/3/pieceid/1";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: null,
                queryString: null,
                entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                sqlQuery: GetQuery("InsertOneWithNullFieldValue"),
                operationType: Config.Operation.Insert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.Created,
                expectedLocationHeader: expectedLocationHeader
            );
        }

        /// <summary>
        /// We try to Sql Inject through the body of an insert operation.
        /// If the insertion happens successfully we know the sql injection
        /// failed.
        /// </summary>
        [DataTestMethod]
        [DataRow(" UNION SELECT * FROM books/*", "InsertSqlInjectionQuery1")]
        [DataRow("; SELECT * FROM information_schema.tables/*", "InsertSqlInjectionQuery2")]
        [DataRow("value; SELECT * FROM v$version--", "InsertSqlInjectionQuery3")]
        [DataRow("id; DROP TABLE books;", "InsertSqlInjectionQuery4")]
        [DataRow(" ' UNION SELECT * FROM books/*", "InsertSqlInjectionQuery5")]
        public virtual async Task InsertOneWithSqlInjectionTest(string sqlInjection, string query)
        {
            string requestBody = @"
            {
                ""title"": """ + sqlInjection + @""",
                ""publisher_id"": 1234
            }";
            string expectedLocationHeader = $"id/{STARTING_ID_FOR_TEST_INSERTS}";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entityNameOrPath: _integrationEntityName,
                sqlQuery: GetQuery(query),
                operationType: Config.Operation.Insert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.Created,
                expectedLocationHeader: expectedLocationHeader
            );
        }

        #endregion

        #region Negative Tests

        /// <summary>
        /// Test to validate that an exception is encountered when the user specifies primary key route or query string for a POST request via REST.
        /// </summary>
        [TestMethod]
        public virtual async Task InsertOneWithPrimaryKeyOrQueryStringInURLTest()
        {
            // Validate that a POST request is not allowed to include a query string in the URL.
            string requestBody = @"
            {
                ""title"": ""My New Book"",
                ""publisher_id"": 1234
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=id eq 5001",
                entityNameOrPath: _integrationEntityName,
                sqlQuery: string.Empty,
                operationType: Config.Operation.Insert,
                requestBody: requestBody,
                exceptionExpected: true,
                expectedErrorMessage: RequestValidator.QUERY_STRING_INVALID_USAGE_ERR_MESSAGE,
                expectedStatusCode: HttpStatusCode.BadRequest
            );

            //Validate that a POST request is not allowed to include primary key in the URL.
            requestBody = @"
            {
                ""categoryid"": 0,
                ""pieceid"": 4,
                ""categoryName"": ""SciFi""
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "categoryid/0/pieceid/4",
                queryString: string.Empty,
                entityNameOrPath: _integrationEntityName,
                sqlQuery: string.Empty,
                operationType: Config.Operation.Insert,
                requestBody: requestBody,
                exceptionExpected: true,
                expectedErrorMessage: RequestValidator.PRIMARY_KEY_INVALID_USAGE_ERR_MESSAGE,
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
                entityNameOrPath: _integrationEntityName,
                sqlQuery: string.Empty,
                operationType: Config.Operation.Insert,
                requestBody: requestBody,
                exceptionExpected: true,
                expectedErrorMessage: RequestValidator.BATCH_MUTATION_UNSUPPORTED_ERR_MESSAGE,
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
                ""publisher_id"": [1234,4321]
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entityNameOrPath: _integrationEntityName,
                sqlQuery: string.Empty,
                operationType: Config.Operation.Insert,
                requestBody: requestBody,
                exceptionExpected: true,
                expectedErrorMessage: "Parameter \"[1234,4321]\" cannot be resolved as column \"publisher_id\" with type \"Int32\".",
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
                entityNameOrPath: _integrationEntityName,
                sqlQuery: string.Empty,
                operationType: Config.Operation.Insert,
                requestBody: requestBody,
                exceptionExpected: true,
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
                entityNameOrPath: _integrationEntityName,
                sqlQuery: string.Empty,
                operationType: Config.Operation.Insert,
                requestBody: requestBody,
                exceptionExpected: true,
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
                entityNameOrPath: _integration_NonAutoGenPK_EntityName,
                sqlQuery: string.Empty,
                operationType: Config.Operation.Insert,
                requestBody: requestBody,
                exceptionExpected: true,
                expectedErrorMessage: "Invalid request body. Missing field in body: id.",
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: "BadRequest"
            );
        }

        /// <summary>
        /// Tests that a cast failure of primary key value type results in HTTP 400 Bad Request.
        /// e.g. Attempt to cast a string '{}' to the 'publisher_id' column type of int will fail.
        /// </summary>
        [TestMethod]
        public async Task InsertWithUncastablePKValue()
        {
            string requestBody = @"
            {
                ""title"": ""BookTitle"",
                ""publisher_id"": ""StringFailsToCastToInt""
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entityNameOrPath: _integrationEntityName,
                sqlQuery: null,
                operationType: Config.Operation.Insert,
                requestBody: requestBody,
                exceptionExpected: true,
                expectedErrorMessage: "Parameter \"StringFailsToCastToInt\" cannot be resolved as column \"publisher_id\" with type \"Int32\".",
                expectedStatusCode: HttpStatusCode.BadRequest
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
                "\"issue_number\": 1234\n" +
            "}";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entityNameOrPath: _integration_NonAutoGenPK_EntityName,
                sqlQuery: string.Empty,
                operationType: Config.Operation.Insert,
                requestBody: requestBody,
                exceptionExpected: true,
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
                entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                sqlQuery: string.Empty,
                operationType: Config.Operation.Insert,
                requestBody: requestBody,
                exceptionExpected: true,
                expectedErrorMessage: "Invalid request body. Missing field in body: categoryName.",
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: "BadRequest"
            );
        }

        [TestMethod]
        public virtual async Task InsertOneWithNonNullableFieldAsNull()
        {
            string requestBody = @"
            {
                ""categoryid"": ""3"",
                ""pieceid"": ""1"",
                ""piecesAvailable"": 1,
                ""piecesRequired"": null,
                ""categoryName"":""Fantasy""
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                sqlQuery: string.Empty,
                operationType: Config.Operation.Insert,
                requestBody: requestBody,
                exceptionExpected: true,
                expectedErrorMessage: "Invalid value for field piecesRequired in request body.",
                expectedStatusCode: HttpStatusCode.BadRequest
            );

            requestBody = @"
            {
                ""categoryid"": ""3"",
                ""pieceid"": ""1"",
                ""piecesAvailable"": 1,
                ""piecesRequired"": 1,
                ""categoryName"":null
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                sqlQuery: string.Empty,
                operationType: Config.Operation.Insert,
                requestBody: requestBody,
                exceptionExpected: true,
                expectedErrorMessage: "Invalid value for field categoryName in request body.",
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        /// <summary>
        /// Verifies that we throw exception when field
        /// provided to insert is an exposed name that
        /// maps to a backing column name that does not
        /// exist in the table.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task InsertTestWithInvalidMapping()
        {
            string requestBody = @"
            {
                ""speciesid"" : 3,
                ""hazards"": ""black mold"",
                ""region"": ""Pacific North West""
            }";

            string expectedLocationHeader = $"speciedid/3";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entityNameOrPath: _integrationBrokenMappingEntity,
                sqlQuery: string.Empty,
                operationType: Config.Operation.Insert,
                exceptionExpected: true,
                requestBody: requestBody,
                expectedErrorMessage: "Invalid request body. Contained unexpected fields in body: hazards",
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: "BadRequest",
                expectedLocationHeader: expectedLocationHeader
                );
        }

        /// <summary>
        /// Test to verify that we throw exception for invalid/bad
        /// insert requests on views.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public virtual async Task InsertOneInViewBadRequestTest(
            string expectedErrorMessage,
            bool isExpectedErrorMsgSubstr = false)
        {
            // Request trying to modify fields from multiple base tables will fail .
            string requestBody = @"
            {
                ""name"": ""new publisher"",
                ""title"": ""New Book""
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entityNameOrPath: _composite_subset_bookPub,
                sqlQuery: string.Empty,
                operationType: Config.Operation.Insert,
                exceptionExpected: true,
                requestBody: requestBody,
                expectedErrorMessage: expectedErrorMessage,
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed.ToString(),
                isExpectedErrorMsgSubstr: isExpectedErrorMsgSubstr
                );
        }

        /// <summary>
        /// Test to validate failure of an insert operation which tries to insert a record
        /// that doesn't satisfy the database policy (@item.name ne 'New publisher')
        /// </summary>
        [TestMethod]
        public virtual async Task InsertOneFailingDatabasePolicy()
        {
            string requestBody = @"
            {
                ""name"": ""New publisher""
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entityNameOrPath: _foreignKeyEntityName,
                sqlQuery: string.Empty,
                operationType: Config.Operation.Insert,
                requestBody: requestBody,
                exceptionExpected: true,
                expectedStatusCode: HttpStatusCode.Forbidden,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed.ToString(),
                expectedErrorMessage: "Could not insert row with given values.",
                clientRoleHeader: "database_policy_tester"
            );
        }

        /// <summary>
        /// Test to validate failure of a request when one or more fields referenced in the database policy for create operation are not provided in the request body.
        /// </summary>
        [TestMethod]
        public virtual async Task InsertOneInTableWithFieldsInDbPolicyNotPresentInBody()
        {
            string requestBody = @"
            {
                ""id"": 18,
                ""category"":""book"",
                ""accessible_role"": ""Anonymous""
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entityNameOrPath: _entityWithSecurityPolicy,
                sqlQuery: string.Empty,
                operationType: Config.Operation.Insert,
                exceptionExpected: true,
                requestBody: requestBody,
                clientRoleHeader: "database_policy_tester",
                expectedErrorMessage: "One or more fields referenced by the database policy are not present in the request body.",
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
                );
        }

        /// <summary>
        /// Abstract method overridden in each of the child classes as each database has its own specific error message.
        /// Validates request failure (HTTP 400) when an invalid foreign key is provided with an insertion.
        /// </summary>
        public abstract Task InsertOneTestViolatingForeignKeyConstraint();

        /// <summary>
        /// Abstract method overridden in each of the child class as each database has its own specific error message.
        /// Validates conflict error (HTTP 409) is thrown when a user tries to insert data with duplicate key.
        /// </summary>
        public abstract Task InsertOneTestViolatingUniqueKeyConstraint();

        #endregion
    }
}
