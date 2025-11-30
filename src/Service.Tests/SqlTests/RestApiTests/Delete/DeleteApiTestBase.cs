// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Services;
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
                    entityNameOrPath: _integrationEntityName,
                    sqlQuery: null,
                    HttpMethod: EntityActionOperation.Delete,
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
                    entityNameOrPath: _integrationMappingEntity,
                    sqlQuery: null,
                    HttpMethod: EntityActionOperation.Delete,
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
                    primaryKeyRoute: "â”¬â”€â”¬ãƒŽ( Âº _ ÂºãƒŽ)/1",
                    queryString: null,
                    entityNameOrPath: _integrationUniqueCharactersEntity,
                    sqlQuery: null,
                    HttpMethod: EntityActionOperation.Delete,
                    requestBody: null,
                    expectedStatusCode: HttpStatusCode.NoContent
                );
        }

        /// <summary>
        /// Delete tests on views which contain fields from one base table
        /// should pass.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public virtual async Task DeleteOneInViewTest()
        {
            // Delete one from view based on books table.
            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "id/1",
                    queryString: null,
                    entityNameOrPath: _simple_all_books,
                    sqlQuery: null,
                    HttpMethod: EntityActionOperation.Delete,
                    requestBody: null,
                    expectedStatusCode: HttpStatusCode.NoContent
                );

            // Delete one from view based on stocks table.
            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "categoryid/1/pieceid/1",
                    queryString: null,
                    entityNameOrPath: _simple_subset_stocks,
                    sqlQuery: null,
                    HttpMethod: EntityActionOperation.Delete,
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
                    entityNameOrPath: _integrationEntityName,
                    sqlQuery: string.Empty,
                    HttpMethod: EntityActionOperation.Delete,
                    requestBody: string.Empty,
                    exceptionExpected: true,
                    expectedErrorMessage: "Could not find item with <id: 1000>",
                    expectedStatusCode: HttpStatusCode.NotFound,
                    expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.ItemNotFound.ToString()
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
                    entityNameOrPath: _integrationEntityName,
                    sqlQuery: string.Empty,
                    HttpMethod: EntityActionOperation.Delete,
                    requestBody: string.Empty,
                    exceptionExpected: true,
                    expectedErrorMessage: "The request is invalid since the primary keys: title requested were not found in the entity definition.",
                    expectedStatusCode: HttpStatusCode.BadRequest,
                    expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.InvalidIdentifierField.ToString()
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
                    entityNameOrPath: _integrationEntityName,
                    sqlQuery: string.Empty,
                    HttpMethod: EntityActionOperation.Delete,
                    requestBody: string.Empty,
                    exceptionExpected: true,
                    expectedErrorMessage: RequestValidator.PRIMARY_KEY_NOT_PROVIDED_ERR_MESSAGE,
                    expectedStatusCode: HttpStatusCode.BadRequest,
                    expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
                );
        }

        /// <summary>
        /// Tests that a cast failure of primary key value type results in HTTP 400 Bad Request.
        /// e.g. Attempt to cast a string '{}' to the 'id' column type of int will fail.
        /// </summary>
        [TestMethod]
        public async Task DeleteWithUncastablePKValue()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/{}",
                queryString: string.Empty,
                entityNameOrPath: _integrationEntityName,
                sqlQuery: string.Empty,
                HttpMethod: EntityActionOperation.Delete,
                requestBody: string.Empty,
                exceptionExpected: true,
                expectedErrorMessage: "Parameter \"{}\" cannot be resolved as column \"id\" with type \"Int32\".",
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
                );
        }

        /// <summary>
        /// DeleteWithSqlInjectionTest attempts to inject a SQL statement
        /// through the primary key route of a delete operation.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
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
                    entityNameOrPath: _integrationEntityName,
                    sqlQuery: string.Empty,
                    HttpMethod: EntityActionOperation.Delete,
                    requestBody: string.Empty,
                    exceptionExpected: true,
                    expectedErrorMessage: message,
                    expectedStatusCode: HttpStatusCode.BadRequest,
                    expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
                );
        }

        /// <summary>
        /// Delete tests on views which contain fields from multiple
        /// base tables should fail.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public virtual async Task DeleteOneInViewBadRequestTest(
            string expectedErrorMessage,
            bool isExpectedErrorMsgSubstr = false)
        {
            // Delete one from view based on books,publishers table.
            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "id/1/pub_id/1234",
                    queryString: null,
                    entityNameOrPath: _composite_subset_bookPub,
                    sqlQuery: null,
                    HttpMethod: EntityActionOperation.Delete,
                    requestBody: null,
                    exceptionExpected: true,
                    expectedErrorMessage: expectedErrorMessage,
                    expectedStatusCode: HttpStatusCode.BadRequest,
                    expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed.ToString(),
                    isExpectedErrorMsgSubstr: isExpectedErrorMsgSubstr
                );
            ;
        }

        #endregion
    }
}
