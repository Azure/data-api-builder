using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests
{
    /// <summary>
    /// Test REST Apis validating expected results are obtained.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class MsSqlRestApiTests : RestApiTestBase
    {
        #region Test Fixture Setup

        /// <summary>
        /// Sets up test fixture for class, only to be run once per test run, as defined by
        /// MSTest decorator.
        /// </summary>
        /// <param name="context"></param>
        [ClassInitialize]
        public static async Task InitializeTestFixture(TestContext context)
        {
            await InitializeTestFixture(context, RestApiTestBase._integrationTableName, TestCategory.MSSQL);

            // Setup REST Components
            //
            _restService = new RestService(_queryEngine, _metadataStoreProvider);
            _restController = new RestController(_restService);
        }

        #endregion

        #region Positive Tests
        /// <summary>
        /// Tests the REST Api for FindById operation without a query string.
        /// </summary>
        [TestMethod]
        public async Task FindByIdTest()
        {
            await RestApiTestBase.FindByIdTest(_queryMap["MsSqlFindById"]);
        }

        /// <summary>
        /// Tests the REST Api for FindById operation with a query string
        /// including the field names.
        /// </summary>
        [TestMethod]
        public async Task FindByIdTestWithQueryStringFields()
        {
            await RestApiTestBase.FindByIdTestWithQueryStringFields(_queryMap["MsSqlFindByIdTestWithQueryStringFields"]);
        }

        /// <summary>
        /// Tests the REST Api for Find operation with a query string with 1 field
        /// including the field names.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithQueryStringOneField()
        {
            await RestApiTestBase.FindTestWithQueryStringOneField(_queryMap["MsSqlFindTestWithQueryStringOneField"]);
        }

        /// <summary>
        /// Tests the REST Api for Find operation with a query string with multiple fields
        /// including the field names. Only returns fields designated in the query string.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithQueryStringMultipleFields()
        {
            await RestApiTestBase.FindTestWithQueryStringMultipleFields(_queryMap["MsSqlFindTestWithQueryStringMultipleFields"]);
        }

        /// <summary>
        /// Tests the REST Api for Find operation with an empty query string
        /// including the field names.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithQueryStringAllFields()
        {
            await RestApiTestBase.FindTestWithQueryStringAllFields(_queryMap["MsSqlFindTestWithQueryStringAllFields"]);
        }

        [TestMethod]
        public async Task FindTestWithPrimaryKeyContainingForeignKey()
        {
            await RestApiTestBase.FindTestWithPrimaryKeyContainingForeignKey(_queryMap["MsSqlFindTestWithPrimaryKeyContainingForeignKey"]);
        }

        #endregion

        #region Negative Tests

        /// <summary>
        /// Tests the REST Api for FindById operation with a query string
        /// having invalid field names.
        /// </summary>
        [TestMethod]
        public async Task FindByIdTestWithInvalidFields()
        {
            await RestApiTestBase.FindByIdTestWithInvalidFields(_queryMap["MsSqlFindByIdTestWithInvalidFields"]);
        }

        /// <summary>
        /// Tests the REST Api for Find operation with a query string that has an invalid field
        /// having invalid field names.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithInvalidFields()
        {
            await RestApiTestBase.FindTestWithInvalidFields(_queryMap["MsSqlFindTestWithInvalidFields"]);
        }

        #endregion
    }
}
