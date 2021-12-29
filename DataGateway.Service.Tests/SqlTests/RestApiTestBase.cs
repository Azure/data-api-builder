using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
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
        public async virtual Task FindByIdTest()
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
        public async virtual Task FindByIdTestWithQueryStringFields()
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
        public async virtual Task FindTestWithQueryStringOneField()
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
        public async virtual Task FindTestWithQueryStringMultipleFields()
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
        public async virtual Task FindTestWithQueryStringAllFields()
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
        public async virtual Task FindTestWithPrimaryKeyContainingForeignKey()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/567/book_id/1",
                queryString: "?_f=id,content",
                entity: "reviews",
                sqlQuery: GetQuery(nameof(FindTestWithPrimaryKeyContainingForeignKey)),
                controller: _restController
            );
        }

        #endregion

        #region Negative Tests

        /// <summary>
        /// Tests the REST Api for FindById operation with a query string
        /// having invalid field names.
        /// </summary>
        [TestMethod]
        public async virtual Task FindByIdTestWithInvalidFields()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/567/book_id/1",
                queryString: "?_f=id,content",
                entity: _integrationTableName,
                sqlQuery: GetQuery(nameof(FindByIdTestWithInvalidFields)),
                controller: _restController,
                exception: true
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation with a query string that has an invalid field
        /// having invalid field names.
        /// </summary>
        [TestMethod]
        public async virtual Task FindTestWithInvalidFields()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?_f=id,null",
                entity: _integrationTableName,
                sqlQuery: GetQuery(nameof(FindTestWithInvalidFields)),
                controller: _restController,
                exception: true
            );
        }

        #endregion
    }
}
