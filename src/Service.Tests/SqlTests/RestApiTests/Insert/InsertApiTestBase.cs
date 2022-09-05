using System.Net;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
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
                operationType: Operation.Insert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.Created,
                expectedLocationHeader: expectedLocationHeader
            );

            requestBody = @"
            {
                ""categoryid"": ""5"",
                ""pieceid"": ""2"",
                ""categoryName"":""FairyTales""
            }";

            expectedLocationHeader = $"categoryid/5/pieceid/2";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: null,
                queryString: null,
                entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                sqlQuery: GetQuery("InsertOneInCompositeNonAutoGenPKTest"),
                operationType: Operation.Insert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.Created,
                expectedLocationHeader: expectedLocationHeader
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
                operationType: Operation.Insert,
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
                entityNameOrPath: _entityWithCompositePrimaryKey,
                sqlQuery: GetQuery(nameof(InsertOneInCompositeKeyTableTest)),
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
                entityNameOrPath: _entityWithCompositePrimaryKey,
                sqlQuery: GetQuery("InsertOneInDefaultTestTable"),
                operationType: Operation.Insert,
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
                operationType: Operation.Insert,
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
                operationType: Operation.Insert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.Created,
                expectedLocationHeader: expectedLocationHeader
            );
        }

        #endregion

        #region Negative Tests

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
                entityNameOrPath: _integrationEntityName,
                sqlQuery: string.Empty,
                operationType: Operation.Insert,
                requestBody: requestBody,
                exceptionExpected: true,
                expectedErrorMessage: RequestValidator.QUERY_STRING_INVALID_USAGE_ERR_MESSAGE,
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
                operationType: Operation.Insert,
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
                operationType: Operation.Insert,
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
                operationType: Operation.Insert,
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
                operationType: Operation.Insert,
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
                operationType: Operation.Insert,
                requestBody: requestBody,
                exceptionExpected: true,
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
                "\"issue_number\": 1234\n" +
            "}";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entityNameOrPath: _integration_NonAutoGenPK_EntityName,
                sqlQuery: string.Empty,
                operationType: Operation.Insert,
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
                operationType: Operation.Insert,
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
                operationType: Operation.Insert,
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
                operationType: Operation.Insert,
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
                operationType: Operation.Insert,
                exceptionExpected: true,
                requestBody: requestBody,
                expectedErrorMessage: "Invalid request body. Contained unexpected fields in body: hazards",
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: "BadRequest",
                expectedLocationHeader: expectedLocationHeader
                );
        }

        /// <summary>
        /// Test to validate that when we try to perform an insertion which has a foreign key dependency
        /// on another table, the request would fail and will be classified as a bad request.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public virtual async Task InsertOneTestViolatingForeignKeyConstraint()
        {
            string requestBody = @"
            {
                ""title"": ""My New Book"",
                ""publisher_id"": 12345
            }";

            // The expected error message is different depending on what database the test is
            // being executed upon.
            string expectedErrorMessage;
            if (this.GetType() == typeof(MsSqlInsertApiTests))
            {
                expectedErrorMessage = "The INSERT statement conflicted with the FOREIGN KEY constraint" +
                    " \"book_publisher_fk\". The conflict occurred in database \"master\", table \"dbo.publishers\"" +
                    ", column 'id'.";
            }
            else if(this.GetType() == typeof(MySqlInsertApiTests))
            {
                expectedErrorMessage = "Cannot add or update a child row: a foreign key constraint fails " +
                    "(`mysql`.`books`, CONSTRAINT `book_publisher_fk` FOREIGN KEY (`publisher_id`) REFERENCES" +
                    " `publishers` (`id`) ON DELETE CASCADE)";
            }
            else
            {
                expectedErrorMessage = "23503: insert or update on table \"books\" violates foreign key" +
                    " constraint \"book_publisher_fk\"";
            }

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entity: _integrationEntityName,
                sqlQuery: string.Empty,
                operationType: Operation.Insert,
                requestBody: requestBody,
                exceptionExpected: true,
                expectedErrorMessage: expectedErrorMessage,
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed.ToString()
            );
        }

        #endregion
    }
}
