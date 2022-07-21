using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests.RestApiTests.Delete
{
    /// <summary>
    /// Test REST Apis validating expected results are obtained.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class MsSqlDeleteApiTests : DeleteApiTestBase
    {
        protected static string DEFAULT_SCHEMA = "dbo";
        private static Dictionary<string, string> _queryMap = new()
        {
            {
                "DeleteOneTest",
                // This query is used to confirm that the item no longer exists, not the
                // actual delete query.
                $"SELECT [id] FROM { _integrationTableName } " +
                $"WHERE id = 5 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            }
        };
        #region Test Fixture Setup

        /// <summary>
        /// Sets up test fixture for class, only to be run once per test run, as defined by
        /// MSTest decorator.
        /// </summary>
        /// <param name="context"></param>
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.MSSQL;
            await InitializeTestFixture(context);
            // Setup REST Components
            _restService = new RestService(_queryEngine,
                _mutationEngine,
                _sqlMetadataProvider,
                _httpContextAccessor.Object,
                _authorizationService.Object,
                _authorizationResolver,
                _runtimeConfigProvider);
            _restController = new RestController(_restService);
        }

        /// <summary>
        /// Runs after every test to reset the database state
        /// </summary>
        [TestCleanup]
        public async Task TestCleanup()
        {
            await ResetDbStateAsync();
        }

        #endregion

        #region RestApiTestBase Overrides

        public override string GetDefaultSchema()
        {
            return DEFAULT_SCHEMA;
        }

        /// <summary>
        /// We include a '.' for the Edm Model
        /// schema to allow both MsSql/PostgreSql
        /// and MySql to share code. MySql does not
        /// include a '.' but MsSql does so
        /// we must include here.
        /// </summary>
        /// <returns></returns>
        public override string GetDefaultSchemaForEdmModel()
        {
            return $"{DEFAULT_SCHEMA}.";
        }

        public override string GetQuery(string key)
        {
            return _queryMap[key];
        }

        /// <summary>
        /// We have 1 test that is named
        /// PutOneUpdateNonNullableDefaultFieldMissingFromJsonBodyTest
        /// which will have Db specific error messages.
        /// We return the mssql specific message here.
        /// </summary>
        /// <returns></returns>
        public override string GetUniqueDbErrorMessage()
        {
            return "Cannot insert the value NULL into column 'piecesRequired', " +
                   "table 'master.dbo.stocks'; column does not allow nulls. UPDATE fails.";
        }

        #endregion

        #region Private helpers

        /// <summary>
        /// Helper function uses reflection to invoke
        /// private methods from outside class.
        /// Expects async method returning Task.
        /// </summary>
        class PrivateObject
        {
            private readonly object _classToInvoke;
            public PrivateObject(object classToInvoke)
            {
                _classToInvoke = classToInvoke;
            }

            public Task<IActionResult> Invoke(string privateMethodName, params object[] privateMethodArgs)
            {
                MethodInfo methodInfo = _classToInvoke.GetType().GetMethod(privateMethodName, BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (methodInfo is null)
                {
                    throw new System.Exception($"{privateMethodName} not found in class '{_classToInvoke.GetType()}'");
                }

                return (Task<IActionResult>)methodInfo.Invoke(_classToInvoke, privateMethodArgs);
            }
        }

        #endregion
    }
}
