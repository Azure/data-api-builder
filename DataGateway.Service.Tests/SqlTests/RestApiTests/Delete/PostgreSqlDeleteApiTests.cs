using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests.RestApiTests.Delete
{
    [TestClass, TestCategory(TestCategory.POSTGRESQL)]
    public class PostgreSqlDeleteApiTests : DeleteApiTestBase
    {
        protected static Dictionary<string, string> _queryMap = new()
        {
            {
                "DeleteOneTest",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id
                        FROM " + _integrationTableName + @"
                        WHERE id = 5
                    ) AS subq
                "
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
            DatabaseEngine = TestCategory.POSTGRESQL;
            await InitializeTestFixture(context);
            _restService = new RestService(_queryEngine,
                _mutationEngine,
                _sqlMetadataProvider,
                _httpContextAccessor.Object,
                _authorizationService.Object,
                _authorizationResolver,
                _runtimeConfigProvider);
            _restController = new RestController(_restService,
                                                 _restControllerLogger);
        }

        #endregion

        [TestCleanup]
        public async Task TestCleanup()
        {
            await ResetDbStateAsync();
        }

        public override string GetQuery(string key)
        {
            return _queryMap[key];
        }
    }
}
