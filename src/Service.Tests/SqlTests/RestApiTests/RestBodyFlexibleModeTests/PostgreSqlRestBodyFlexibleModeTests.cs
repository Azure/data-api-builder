// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests
{
    /// <summary>
    /// Class containing integration tests for PostgreSql- to validate scenarios when we operate in flexible mode for REST request body,
    /// i.e. we allow extraneous fields to be present in the request body.
    /// </summary>
    [TestClass, TestCategory(TestCategory.POSTGRESQL)]
    public class PostgreSqlRestBodyFlexibleModeTests : RestBodyFlexibleModeTests
    {
        private static Dictionary<string, string> _queryMap = new()
        {
            {
                "InsertOneWithNonExistingFieldInRequestBody",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable"", ""piecesRequired""
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 3 AND pieceid = 1 AND ""categoryName"" = 'SciFi'
                            AND ""piecesAvailable"" is NULL AND ""piecesRequired"" = 1
                    ) AS subq
                "
            },
            {
                "PutOneWithPKFieldsInRequestBody",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable"", ""piecesRequired""
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 2 AND pieceid = 1 AND ""categoryName"" = 'SciFi'
                            AND ""piecesAvailable"" = 10 AND ""piecesRequired"" = 5
                    ) AS subq
                "
            },
            {
                "PutOneWithNonExistingFieldInRequestBody",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable"", ""piecesRequired""
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 2 AND pieceid = 1 AND ""categoryName"" = 'SciFi'
                            AND ""piecesAvailable"" = 10 AND ""piecesRequired"" = 5
                    ) AS subq
                "
            },
            {
                "PatchOneWithPKFieldsInRequestBody",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable"", ""piecesRequired""
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 1 AND pieceid = 1 AND ""categoryName"" = 'SciFi'
                            AND ""piecesAvailable"" is NULL AND ""piecesRequired"" = 0
                    ) AS subq
                "
            },
            {
                "PatchOneWithNonExistingFieldInRequestBody",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable"", ""piecesRequired""
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 1 AND pieceid = 1 AND ""categoryName"" = 'SciFi'
                            AND ""piecesAvailable"" is NULL AND ""piecesRequired"" = 0
                    ) AS subq
                "
            }
        };

        [ClassInitialize]
        public static async Task SetupDatabaseAsync(TestContext TestContext)
        {
            DatabaseEngine = TestCategory.POSTGRESQL;
            await InitializeTestFixture(context: null, isRestBodyFlexible: true);
        }

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
