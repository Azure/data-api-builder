// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests
{
    /// <summary>
    /// Regression coverage for the MySQL upsert (PUT/PATCH) path when the connection is opened with
    /// UseAffectedRows=true (changed-row semantics). In that mode, an authorized idempotent update -
    /// one whose submitted values equal the row's current values - reports ROW_COUNT() = 0. The upsert
    /// authorization decision must therefore be based on whether a row matched the primary key + update
    /// policy, not on the number of changed rows; otherwise an authorized idempotent PUT/PATCH would be
    /// incorrectly rejected with 403 DatabasePolicyFailure.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MYSQL)]
    public class MySqlUpsertUseAffectedRowsTests : RestApiTestBase
    {
        protected static Dictionary<string, string> _queryMap = new()
        {
            {
                // Row (100, 99) as seeded: categoryName 'Historical', piecesAvailable 0, piecesRequired 0.
                "IdempotentUpdate_ExistingRow",
                @"
                    SELECT JSON_OBJECT('categoryid', categoryid, 'pieceid', pieceid, 'categoryName', categoryName,
                                        'piecesAvailable',piecesAvailable,'piecesRequired',piecesRequired) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable,piecesRequired
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 100 AND pieceid = 99 AND categoryName ='Historical' AND piecesAvailable = 0
                        AND piecesRequired = 0 AND pieceid != 1
                    ) AS subq
                "
            }
        };

        #region Test Fixture Setup

        /// <summary>
        /// Sets up the test fixture once per class, opening the MySQL connection with UseAffectedRows=true
        /// so the tests exercise changed-row semantics.
        /// </summary>
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.MYSQL;
            await InitializeTestFixture(connectionStringOptions: "UseAffectedRows=true");
        }

        /// <summary>
        /// Runs after every test to reset the database state.
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

        /// <summary>
        /// An authorized PUT that resubmits the row's existing values (idempotent) must succeed with 200,
        /// even under UseAffectedRows=true where ROW_COUNT() would be 0. The row (100,99) satisfies the
        /// update policy "@item.pieceid ne 1".
        /// </summary>
        [TestMethod]
        public async Task PutOneIdempotentUpdateWithDatabasePolicyUsingAffectedRows()
        {
            string requestBody = @"
            {
                ""categoryName"": ""Historical"",
                ""piecesAvailable"": 0,
                ""piecesRequired"": 0
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "categoryid/100/pieceid/99",
                    queryString: null,
                    entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                    sqlQuery: GetQuery("IdempotentUpdate_ExistingRow"),
                    operationType: EntityActionOperation.Upsert,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK,
                    clientRoleHeader: "database_policy_tester"
                );
        }

        /// <summary>
        /// An authorized PATCH that resubmits a field's existing value (idempotent) must succeed with 200,
        /// even under UseAffectedRows=true where ROW_COUNT() would be 0.
        /// </summary>
        [TestMethod]
        public async Task PatchOneIdempotentUpdateWithDatabasePolicyUsingAffectedRows()
        {
            string requestBody = @"
            {
                ""piecesAvailable"": 0
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "categoryid/100/pieceid/99",
                    queryString: null,
                    entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                    sqlQuery: GetQuery("IdempotentUpdate_ExistingRow"),
                    operationType: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK,
                    clientRoleHeader: "database_policy_tester"
                );
        }

        /// <summary>
        /// An unauthorized update (targeting a row that violates the update policy "@item.pieceid ne 1")
        /// must still be rejected with 403 under UseAffectedRows=true - the changed-row semantics must not
        /// weaken policy enforcement.
        /// </summary>
        [TestMethod]
        public async Task PatchOneUnauthorizedUpdateIsBlockedUsingAffectedRows()
        {
            string requestBody = @"
            {
                ""categoryName"": ""SciFi"",
                ""piecesRequired"": 5,
                ""piecesAvailable"": 2
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "categoryid/0/pieceid/1",
                    queryString: null,
                    entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                    operationType: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    sqlQuery: string.Empty,
                    exceptionExpected: true,
                    expectedErrorMessage: DataApiBuilderException.AUTHORIZATION_FAILURE,
                    expectedStatusCode: HttpStatusCode.Forbidden,
                    expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.DatabasePolicyFailure.ToString(),
                    clientRoleHeader: "database_policy_tester"
                );

            // Verify the row was not modified: it must still match its original seed values
            // (categoryName='', piecesAvailable=0, piecesRequired=0).
            string unchangedRow = await GetDatabaseResultAsync(
                "SELECT JSON_OBJECT('categoryid', categoryid, 'pieceid', pieceid, 'categoryName', categoryName, " +
                "'piecesAvailable', piecesAvailable, 'piecesRequired', piecesRequired) AS data " +
                "FROM " + _Composite_NonAutoGenPK_TableName + " " +
                "WHERE categoryid = 0 AND pieceid = 1 AND categoryName = '' AND piecesAvailable = 0 AND piecesRequired = 0");

            Assert.AreNotEqual(
                "[]",
                unchangedRow,
                "The row (categoryid=0, pieceid=1) must remain unmodified after a PATCH blocked by the update policy.");
        }
    }
}
