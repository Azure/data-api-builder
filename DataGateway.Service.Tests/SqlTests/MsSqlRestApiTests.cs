using System.Collections.Generic;
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

        protected static Dictionary<string, string> _queryMap = new()
        {
            {
                "FindByIdTest",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE id = 2 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "MsSqlFindByIdTestWithQueryStringFields",
                $"SELECT[id], [title] FROM { _integrationTableName } " +
                $"WHERE id = 1 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "MsSqlFindTestWithQueryStringOneField",
                $"SELECT [id] FROM { _integrationTableName } " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "MsSqlFindTestWithQueryStringMultipleFields",
                $"SELECT [id], [title] FROM { _integrationTableName } " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "MsSqlFindTestWithQueryStringAllFields",
                $"SELECT * FROM { _integrationTableName } " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "MsSqlFindTestWithPrimaryKeyContainingForeignKey",
                $"SELECT [id], [content] FROM reviews " +
                $"WHERE id = 567 AND book_id = 1 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "MsSqlFindByIdTestWithInvalidFields",
                $"SELECT [id], [name], [type] FROM { _integrationTableName } " +
                $"WHERE id = 1 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "MsSqlFindTestWithInvalidFields",
                $"SELECT [id], [name], [type] FROM { _integrationTableName } " +
                $"WHERE id = 1 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            }
        };
        #region Test Fixture Setup

        /// <summary>
        /// Sets up test fixture for class, only to be run once per test run, as defined by
        /// MSTest decorator.
        /// </summary>
        /// <param name="context"></param>
        [ClassInitialize]
        public static async void InitializeTestFixture(TestContext context)
        {
            await InitializeTestFixture(context, RestApiTestBase._integrationTableName, TestCategory.MSSQL);

            // Setup REST Components
            //
            _restService = new RestService(_queryEngine, _metadataStoreProvider);
            _restController = new RestController(_restService);
        }

        #endregion

        public override string GetQuery(string key)
        {
            return _queryMap[key];
        }

        #region Positive Tests
        /// <summary>
        /// Tests the REST Api for FindById operation without a query string.
        /// </summary>
        [TestMethod]
        public override void FindByIdTest()
        {
            base.FindByIdTest();
        }

        /// <summary>
        /// Tests the REST Api for FindById operation with a query string
        /// including the field names.
        /// </summary>
        [TestMethod]
        public override void FindByIdTestWithQueryStringFields()
        {
            base.FindByIdTestWithQueryStringFields();
        }

        /// <summary>
        /// Tests the REST Api for Find operation with a query string with 1 field
        /// including the field names.
        /// </summary>
        [TestMethod]
        public override void FindTestWithQueryStringOneField()
        {
            base.FindTestWithQueryStringOneField();
        }

        /// <summary>
        /// Tests the REST Api for Find operation with a query string with multiple fields
        /// including the field names. Only returns fields designated in the query string.
        /// </summary>
        [TestMethod]
        public override void FindTestWithQueryStringMultipleFields()
        {
            base.FindTestWithQueryStringMultipleFields();
        }

        /// <summary>
        /// Tests the REST Api for Find operation with an empty query string
        /// including the field names.
        /// </summary>
        [TestMethod]
        public override void FindTestWithQueryStringAllFields()
        {
            base.FindTestWithQueryStringAllFields();
        }

        [TestMethod]
        public override void FindTestWithPrimaryKeyContainingForeignKey()
        {
            base.FindTestWithPrimaryKeyContainingForeignKey();
        }

        #endregion

        #region Negative Tests

        /// <summary>
        /// Tests the REST Api for FindById operation with a query string
        /// having invalid field names.
        /// </summary>
        [TestMethod]
        public override void FindByIdTestWithInvalidFields()
        {
            base.FindByIdTestWithInvalidFields();
        }

        /// <summary>
        /// Tests the REST Api for Find operation with a query string that has an invalid field
        /// having invalid field names.
        /// </summary>
        [TestMethod]
        public override void FindTestWithInvalidFields()
        {
            base.FindTestWithInvalidFields();
        }

        #endregion
    }
}
