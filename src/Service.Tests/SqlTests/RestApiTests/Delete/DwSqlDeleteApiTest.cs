// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests.Delete
{
    /// <summary>
    /// Test REST Apis validating expected results are obtained.
    /// </summary>
    [TestClass, TestCategory(TestCategory.DWSQL)]
    public class DwSqlDeleteApiTests : DeleteApiTestBase
    {
        private static Dictionary<string, string> _queryMap = new()
        {
            {
                "DeleteOneWithStoredProcedureTest",
                $"SELECT [id] FROM { _integrationTableName } " +
                $"WHERE id = 14"
            }
        };
        #region Test Fixture Setup

        /// <summary>
        /// Sets up test fixture for class, only to be run once per test run, as defined by
        /// MSTest decorator.
        /// </summary>
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.DWSQL;
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

        [TestMethod]
        public async Task DeleteOneInViewBadRequestTest()
        {
            string expectedErrorMessage = $"View or function '{_defaultSchemaName}.{_composite_subset_bookPub}' is not updatable " +
                                           "because the modification affects multiple base tables.";
            await base.DeleteOneInViewBadRequestTest(expectedErrorMessage);
        }

        /// <summary>
        /// Delete the last inserted row (row with max id) from books.
        /// Verify that the row doesn't exist anymore.
        /// </summary>
        [TestMethod]
        public async Task DeleteOneWithStoredProcedureTest()
        {
            // Delete one from stored-procedure based on books table.
            await SetupAndRunRestApiTest(
                    primaryKeyRoute: null,
                    queryString: null,
                    entityNameOrPath: _integrationProcedureDeleteOne_EntityName,
                    sqlQuery: GetQuery(nameof(DeleteOneWithStoredProcedureTest)),
                    operationType: EntityActionOperation.Execute,
                    requestBody: null,
                    expectedStatusCode: HttpStatusCode.NoContent,
                    expectJson: false
                );
        }

        #region RestApiTestBase Overrides

        public override string GetQuery(string key)
        {
            return _queryMap[key];
        }

        #endregion

    }
}
