// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests
{
    /// <summary>
    /// Concurrent same-key PUT/PATCH upsert coverage for PostgreSQL.
    /// </summary>
    [TestClass, TestCategory(TestCategory.POSTGRESQL)]
    public class PostgreSqlUpsertConcurrencyTests : UpsertConcurrencyTestBase
    {
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.POSTGRESQL;
            await InitializeTestFixture();
        }

        protected override string GetRowCountQuery(int pieceId)
        {
            return "SELECT json_build_object('cnt', COUNT(*)) AS data " +
                $"FROM {_Composite_NonAutoGenPK_TableName} " +
                $"WHERE categoryid = 0 AND pieceid = {pieceId}";
        }
    }
}
