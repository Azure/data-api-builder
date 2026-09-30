// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests
{
    /// <summary>
    /// Concurrent same-key PUT/PATCH upsert coverage for SQL Server.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class MsSqlUpsertConcurrencyTests : UpsertConcurrencyTestBase
    {
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.MSSQL;
            await InitializeTestFixture();
        }

        protected override string GetRowCountQuery(int pieceId)
        {
            return $"SELECT COUNT(*) AS [cnt] FROM {_Composite_NonAutoGenPK_TableName} " +
                $"WHERE [categoryid] = 0 AND [pieceid] = {pieceId} " +
                "FOR JSON PATH, WITHOUT_ARRAY_WRAPPER";
        }
    }
}
