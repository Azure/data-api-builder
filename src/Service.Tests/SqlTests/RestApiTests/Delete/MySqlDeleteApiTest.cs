// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests.Delete
{
    [TestClass, TestCategory(TestCategory.MYSQL)]
    public class MySqlDeleteApiTests : DeleteApiTestBase
    {
        protected static Dictionary<string, string> _queryMap = new()
        {
            {
                "DeleteOneTest",
                @"
                    SELECT JSON_OBJECT('id', id) AS data
                    FROM (
                        SELECT id
                        FROM " + _integrationTableName + @"
                        WHERE id = 5
                    ) AS subq
                "
            }
        };

        [TestMethod]
        [Ignore]
        public void DeleteOneInViewBadRequestTest()
        {
            throw new NotImplementedException();
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
            DatabaseEngine = TestCategory.MYSQL;
            await InitializeTestFixture();
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

        public override string GetQuery(string key)
        {
            return _queryMap[key];
        }
    }
}
