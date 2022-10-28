using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Core;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Resolvers;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using MySqlConnector;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass, TestCategory(TestCategory.MYSQL)]
    public class MySqlQueryExecutorUnitTests
    {
        /// <summary>
        /// Validates managed identity token issued ONLY when connection string does not specify
        /// User, Password, and Authentication method.
        /// </summary>
        [DataTestMethod]
        [DataRow("Server =<>;Database=<>;User=xyz;Password=xxx", false, false,
            DisplayName = "No managed identity access token when connection string specifies both User and Password.")]
        [DataRow("Server =<>;Database=<>;User=xyz;Password=xxx;", false, false,
            DisplayName = "No managed identity access token when connection string specifies User, Password.")]
        [DataRow("Server =<>;Database=<>;", true, false,
            DisplayName = "Managed identity access token from config used when connection string specifies none of User, Password.")]
        [DataRow("Server =<>;Database=<>;", true, true,
            DisplayName = "Default managed identity access token used when connection string specifies none of User, Password.")]
        public async Task TestHandleManagedIdentityAccess(
            string connectionString,
            bool expectManagedIdentityAccessToken,
            bool isDefaultAzureCredential)
        {
            RuntimeConfigProvider runtimeConfigProvider = TestHelper.GetRuntimeConfigProvider(TestCategory.MYSQL);
            runtimeConfigProvider.GetRuntimeConfiguration().ConnectionString = connectionString;
            Mock<DbExceptionParser> dbExceptionParser = new(runtimeConfigProvider, new HashSet<string>());
            Mock<ILogger<MySqlQueryExecutor>> queryExecutorLogger = new();
            MySqlQueryExecutor mySqlQueryExecutor = new(runtimeConfigProvider, dbExceptionParser.Object, queryExecutorLogger.Object);

            const string DEFAULT_TOKEN = "Default access token";
            const string CONFIG_TOKEN = "Configuration controller access token";
            AccessToken testValidToken = new(accessToken: DEFAULT_TOKEN, expiresOn: DateTimeOffset.MaxValue);
            if (expectManagedIdentityAccessToken)
            {
                if (isDefaultAzureCredential)
                {
                    Mock<DefaultAzureCredential> dacMock = new();
                    dacMock
                        .Setup(m => m.GetTokenAsync(It.IsAny<TokenRequestContext>(),
                            It.IsAny<System.Threading.CancellationToken>()))
                        .Returns(ValueTask.FromResult(testValidToken));
                    mySqlQueryExecutor.AzureCredential = dacMock.Object;
                }
                else
                {
                    runtimeConfigProvider.Initialize(
                        JsonSerializer.Serialize(runtimeConfigProvider.GetRuntimeConfiguration()),
                        schema: null,
                        connectionString: connectionString,
                        accessToken: CONFIG_TOKEN);
                    mySqlQueryExecutor = new(runtimeConfigProvider, dbExceptionParser.Object, queryExecutorLogger.Object);
                }
            }

            using MySqlConnection conn = new(connectionString);
            await mySqlQueryExecutor.SetManagedIdentityAccessTokenIfAnyAsync(conn);
            MySqlConnectionStringBuilder my = new(conn.ConnectionString);

            if (expectManagedIdentityAccessToken)
            {
                if (isDefaultAzureCredential)
                {
                    Assert.AreEqual(expected: DEFAULT_TOKEN, actual: my.Password);
                }
                else
                {
                    Assert.AreEqual(expected: CONFIG_TOKEN, actual: my.Password);
                }
            }
            else
            {
                Assert.AreEqual(expected: "xxx", actual: my.Password);
            }
        }

        /// <summary>
        /// Test to validate that when a query successfully executes within the allowed number of retries, a result is returned
        /// and no further retries occur.
        /// </summary>
        [TestMethod, TestCategory(TestCategory.MYSQL)]
        public async Task TestRetryPolicyExhaustingMaxAttempts()
        {
            int maxRetries = 5;
            int maxAttempts = maxRetries + 1; // 1 represents the original attempt to execute the query in addition to retries.
            RuntimeConfigProvider runtimeConfigProvider = TestHelper.GetRuntimeConfigProvider(TestCategory.MYSQL);
            Mock<ILogger<QueryExecutor<MySqlConnection>>> queryExecutorLogger = new();
            DbExceptionParser dbExceptionParser = new MySqlDbExceptionParser(runtimeConfigProvider);
            Mock<MySqlQueryExecutor> queryExecutor = new(runtimeConfigProvider, dbExceptionParser, queryExecutorLogger.Object);

            // Mock the ExecuteQueryAgainstDbAsync to throw a transient exception.
            //queryExecutor.Setup(x => x.ExecuteQueryAgainstDbAsync(
            //    It.IsAny<MySqlConnection>(),
            //    It.IsAny<string>(),
            //    It.IsAny<IDictionary<string, object>>(),
            //    It.IsAny<Func<DbDataReader, List<string>, Task<object>>>(),
            //    It.IsAny<List<string>>()))
            //.Throws(SqlTestHelper.CreateSqlException(ERRORCODE_SEMAPHORE_TIMEOUT));

            // Call the actual ExecuteQueryAsync method.
            queryExecutor.Setup(x => x.ExecuteQueryAsync(
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, object>>(),
                It.IsAny<Func<DbDataReader, List<string>, Task<object>>>(),
                It.IsAny<List<string>>())).CallBase();

            DataApiBuilderException ex = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(async () =>
            {
                await queryExecutor.Object.ExecuteQueryAsync<object>(
                    sqltext: string.Empty,
                    parameters: new Dictionary<string, object>(),
                    dataReaderHandler: null,
                    args: null);
            });

            Assert.AreEqual(HttpStatusCode.InternalServerError, ex.StatusCode);

            // For each attempt logger is invoked twice. Currently we have hardcoded the number of attempts.
            // Once we have number of retry attempts specified in config, we will make it dynamic.
            Assert.AreEqual(2 * maxAttempts, queryExecutorLogger.Invocations.Count);
        }

        /// <summary>
        /// Test to validate that when a query succcessfully executes within allowed number of retries, we get back the result
        /// without giving anymore retries.
        /// </summary>
        [TestMethod, TestCategory(TestCategory.MYSQL)]
        public async Task TestRetryPolicySuccessfullyExecutingQueryAfterNAttempts()
        {
            RuntimeConfigProvider runtimeConfigProvider = TestHelper.GetRuntimeConfigProvider(TestCategory.MYSQL);
            Mock<ILogger<QueryExecutor<MySqlConnection>>> queryExecutorLogger = new();
            DbExceptionParser dbExceptionParser = new MySqlDbExceptionParser(runtimeConfigProvider);
            Mock<MySqlQueryExecutor> queryExecutor = new(runtimeConfigProvider, dbExceptionParser, queryExecutorLogger.Object);

            // Mock the ExecuteQueryAgainstDbAsync to throw a transient exception.
            //queryExecutor.SetupSequence(x => x.ExecuteQueryAgainstDbAsync(
            //    It.IsAny<MySqlConnection>(),
            //    It.IsAny<string>(),
            //    It.IsAny<IDictionary<string, object>>(),
            //    It.IsAny<Func<DbDataReader, List<string>, Task<object>>>(),
            //    It.IsAny<List<string>>()))
            //.Throws(SqlTestHelper.CreateSqlException(ERRORCODE_SEMAPHORE_TIMEOUT))
            //.Throws(SqlTestHelper.CreateSqlException(ERRORCODE_SEMAPHORE_TIMEOUT))
            //.CallBase();

            // Call the actual ExecuteQueryAsync method.
            queryExecutor.Setup(x => x.ExecuteQueryAsync(
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, object>>(),
                It.IsAny<Func<DbDataReader, List<string>, Task<object>>>(),
                It.IsAny<List<string>>())).CallBase();

            string sqltext = "SELECT * from books";

            await queryExecutor.Object.ExecuteQueryAsync<object>(
                    sqltext: sqltext,
                    parameters: new Dictionary<string, object>(),
                    dataReaderHandler: null,
                    args: null);

            // For each attempt logger is invoked twice. The query executes successfully in in 1st retry .i.e. 2nd attempt of execution.
            // An additional information log is added when the query executes successfully in a retry attempt.
            Assert.AreEqual(2 * 2 + 1, queryExecutorLogger.Invocations.Count);
        }
    }
}
