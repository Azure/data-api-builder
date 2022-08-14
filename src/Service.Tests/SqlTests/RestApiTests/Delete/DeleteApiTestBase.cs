using System.Net;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests
{
    /// <summary>
    /// Test DELETE REST Api validating expected results are obtained.
    /// </summary>
    [TestClass]
    public abstract class DeleteApiTestBase : RestApiTestBase
    {
        #region Positive Tests

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
                    entity: _integrationEntityName,
                    sqlQuery: null,
                    controller: _restController,
                    operationType: Operation.Delete,
                    requestBody: null,
                    expectedStatusCode: HttpStatusCode.NoContent
                );
        }

        /// <summary>
        /// Operates on a single entity with mapping defined
        /// for its columns, and with target object identified in the
        /// primaryKeyRoute. No requestBody is used for this type of
        /// request. sqlQuery is not used because we are confirming the
        /// NoContent result of a successful delete operation.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task DeleteOneMappingTest()
        {
            //expected status code 204
            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "treeId/1",
                    queryString: null,
                    entity: _integrationMappingEntity,
                    sqlQuery: null,
                    controller: _restController,
                    operationType: Operation.Delete,
                    requestBody: null,
                    expectedStatusCode: HttpStatusCode.NoContent
                );
        }

        /// <summary>
        /// Operates on a single entity with mapping defined
        /// for its columns using unique unicode character in the exposed
        /// name, and with target object identified in the
        /// primaryKeyRoute. No requestBody is used for this type of
        /// request. sqlQuery is not used because we are confirming the
        /// NoContent result of a successful delete operation.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task DeleteOneUniqueCharacterTest()
        {
            //expected status code 204
            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "┬─┬ノ( º _ ºノ)/1",
                    queryString: null,
                    entity: _integrationUniqueCharactersEntity,
                    sqlQuery: null,
                    controller: _restController,
                    operationType: Operation.Delete,
                    requestBody: null,
                    expectedStatusCode: HttpStatusCode.NoContent
                );
        }
        #endregion

        #region Negative Tests

        /// <summary>
        /// DeleteNonExistent operates on a single entity with target object
        /// identified in the primaryKeyRoute.
        /// sqlQuery represents the query used to get 'expected' result of zero items.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task DeleteNonExistentTest()
        {
            //expected status code 404
            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "id/1000",
                    queryString: string.Empty,
                    entity: _integrationEntityName,
                    sqlQuery: string.Empty,
                    controller: _restController,
                    operationType: Operation.Delete,
                    requestBody: string.Empty,
                    exception: true,
                    expectedErrorMessage: "Not Found",
                    expectedStatusCode: HttpStatusCode.NotFound,
                    expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.EntityNotFound.ToString()
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
        {
            //expected status code 404
            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "title/7",
                    queryString: string.Empty,
                    entity: _integrationEntityName,
                    sqlQuery: string.Empty,
                    controller: _restController,
                    operationType: Operation.Delete,
                    requestBody: string.Empty,
                    exception: true,
                    expectedErrorMessage: "The request is invalid since the primary keys: title requested were not found in the entity definition.",
                    expectedStatusCode: HttpStatusCode.NotFound,
                    expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.EntityNotFound.ToString()
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
                    entity: _integrationEntityName,
                    sqlQuery: string.Empty,
                    controller: _restController,
                    operationType: Operation.Delete,
                    requestBody: string.Empty,
                    exception: true,
                    expectedErrorMessage: "Primary Key for DELETE requests is required.",
                    expectedStatusCode: HttpStatusCode.BadRequest,
                    expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
                );
        }

        /// <summary>
        /// DeleteWithSqlInjectionTest attempts to inject a SQL statement
        /// through the primary key route of a delete operation.
        /// </summary>
        /// <returns></returns>
        [DataTestMethod]
        [DataRow("/UNION SELECT * FROM books/*",
            "Primary key column(s) provided do not match DB schema.")]
        [DataRow("/OR 1=1/*",
            "Primary key column(s) provided do not match DB schema.")]
        [DataRow("; SELECT * FROM information_schema.tables/*",
            "Support for url template with implicit primary key field names is not yet added.")]
        [DataRow("/value; DROP TABLE authors;",
            "Support for url template with implicit primary key field names is not yet added.")]
        public async Task DeleteWithSqlInjectionTest(string sqlInjection, string message)
        {
            //expected status code 400
            await SetupAndRunRestApiTest(
                    primaryKeyRoute: $"id/1{sqlInjection}",
                    queryString: string.Empty,
                    entity: _integrationEntityName,
                    sqlQuery: string.Empty,
                    controller: _restController,
                    operationType: Operation.Delete,
                    requestBody: string.Empty,
                    exception: true,
                    expectedErrorMessage: message,
                    expectedStatusCode: HttpStatusCode.BadRequest,
                    expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
                );
        }

        #endregion
    }
}
