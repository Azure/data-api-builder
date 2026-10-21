// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.Exceptions;
using Humanizer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestBootstrapTests
{
    /// <summary>
    /// SQL Server can fail a query at read time (e.g. divide by zero in a computed column) while reporting
    /// HasRows == false, and only raises the error once the reader is read. These tests validate that such
    /// errors are returned to the caller instead of being reported as an empty result.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class MsSqlMidStreamErrorTests : SqlTestBase
    {
        private const string FAILING_TABLE = "midstream_error_items";
        private const string EMPTY_TABLE = "midstream_error_empty";

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
        /// A runtime error raised while reading rows must be returned as an error for REST requests,
        /// both for a full read and when paginating (which reads one extra row).
        /// </summary>
        [DataTestMethod]
        [DataRow("", DisplayName = "REST read without query string")]
        [DataRow("?$first=2", DisplayName = "REST read with $first")]
        public async Task RestReadSurfacesDatabaseError(string queryString)
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: queryString,
                entityNameOrPath: FAILING_TABLE,
                sqlQuery: string.Empty,
                exceptionExpected: true,
                expectedErrorMessage: "Divide by zero error encountered.",
                isExpectedErrorMsgSubstr: true,
                expectedStatusCode: HttpStatusCode.InternalServerError,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed.ToString());
        }

        /// <summary>
        /// A runtime error raised while reading rows must be returned as an error for GraphQL requests.
        /// </summary>
        [TestMethod]
        public async Task GraphQLReadSurfacesDatabaseError()
        {
            string query = $"{{ {FAILING_TABLE.Pluralize()} {{ items {{ id ratio }} }} }}";
            JsonElement result = await ExecuteGraphQLRequestAsync(
                query,
                queryName: FAILING_TABLE.Pluralize(),
                isAuthenticated: false,
                expectsError: true);

            SqlTestHelper.TestForErrorInGraphQLResponse(
                result.ToString(),
                statusCode: $"{DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed}");
        }

        /// <summary>
        /// A genuinely empty result must keep returning an empty list and not an error.
        /// </summary>
        [TestMethod]
        public async Task RestReadOfEmptyTableReturnsEmptyList()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entityNameOrPath: EMPTY_TABLE,
                sqlQuery: $"SELECT * FROM {EMPTY_TABLE} FOR JSON PATH, INCLUDE_NULL_VALUES");
        }
    }
}
