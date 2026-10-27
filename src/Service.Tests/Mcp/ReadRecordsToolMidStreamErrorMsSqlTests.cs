// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Mcp.BuiltInTools;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;

namespace Azure.DataApiBuilder.Service.Tests.Mcp
{
    /// <summary>
    /// SQL Server can fail a query at read time (e.g. divide by zero in a computed column) while reporting
    /// HasRows == false, and only raises the error once the reader is read. These tests validate that the MCP
    /// read_records tool returns such errors instead of reporting an empty result.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class ReadRecordsToolMidStreamErrorMsSqlTests : McpToolTestBase
    {
        private const string FAILING_TABLE = "midstream_error_mcp_items";
        private const string EMPTY_TABLE = "midstream_error_mcp_empty";

        private const string CREATE_TABLES_QUERY =
            $"CREATE TABLE {FAILING_TABLE} (id int PRIMARY KEY, divisor int NOT NULL, ratio AS (100 / divisor)); " +
            $"INSERT {FAILING_TABLE} (id, divisor) VALUES (1, 1), (2, 2), (3, 0), (4, 4); " +
            $"CREATE TABLE {EMPTY_TABLE} (id int PRIMARY KEY);";

        [TestInitialize]
        public async Task TestInitialize()
        {
            DatabaseEngine = TestCategory.MSSQL;
            await InitializeTestFixture(
                customQueries: new List<string> { CREATE_TABLES_QUERY },
                customEntities: new List<string[]>
                {
                    new[] { FAILING_TABLE, FAILING_TABLE, string.Empty },
                    new[] { EMPTY_TABLE, EMPTY_TABLE, string.Empty }
                });
        }

        [TestCleanup]
        public async Task TestCleanup()
        {
            await _queryExecutor.ExecuteQueryAsync<object>(
                $"DROP TABLE IF EXISTS {FAILING_TABLE}; DROP TABLE IF EXISTS {EMPTY_TABLE};",
                parameters: null,
                dataReaderHandler: null,
                dataSourceName: string.Empty);
        }

        /// <summary>
        /// A runtime error raised while reading rows must be returned as an error result,
        /// both for a full read and when paginating (which reads one extra row).
        /// </summary>
        [DataTestMethod]
        [DataRow(null, DisplayName = "read_records without first")]
        [DataRow(2, DisplayName = "read_records with first")]
        public async Task ReadRecords_SurfacesDatabaseError(int? first)
        {
            // The computed column must be selected explicitly for the divide by zero to be evaluated.
            CallToolResult result = await ExecuteReadAsync(FAILING_TABLE, "id,ratio", first);

            AssertError(result);
            JsonElement error = ParseResultRoot(result).GetProperty("error");
            StringAssert.Contains(error.GetProperty("message").GetString(), "Divide by zero error encountered.");
        }

        /// <summary>
        /// A genuinely empty result must keep returning an empty list and not an error.
        /// </summary>
        [TestMethod]
        public async Task ReadRecords_OfEmptyTable_ReturnsEmptyList()
        {
            CallToolResult result = await ExecuteReadAsync(EMPTY_TABLE, "id", first: null);

            AssertSuccess(result, "ReadRecords for an empty table should succeed.");
            Assert.AreEqual(0, GetRecordsArray(ParseResultRoot(result)).GetArrayLength());
        }

        private static async Task<CallToolResult> ExecuteReadAsync(string entity, string select, int? first)
        {
            Dictionary<string, object?> args = new() { { "entity", entity }, { "select", select } };
            if (first != null)
            {
                args["first"] = first;
            }

            return await ExecuteToolAsync(new ReadRecordsTool(), BuildQueryServiceProvider(), args);
        }
    }
}
