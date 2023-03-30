// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests.Delete
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

        [TestMethod]
        public async Task DeleteOneInViewBadRequestTest()
        {
            string expectedErrorMessage = $"55000: cannot delete from view \"{_composite_subset_bookPub}\"";
            await base.DeleteOneInViewBadRequestTest(
                expectedErrorMessage,
                isExpectedErrorMsgSubstr: true);
        }

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
