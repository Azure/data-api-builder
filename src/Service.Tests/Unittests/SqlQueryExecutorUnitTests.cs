using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Core;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Resolvers;
using Azure.DataApiBuilder.Service.Tests.SqlTests;
using Azure.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class SqlQueryExecutorUnitTests
    {
        private static int _semaphoreTimedOutErrorCode = 121; // Error code for semaphore timeout in MsSql.
        /// <summary>
        /// Validates managed identity token issued ONLY when connection string does not specify
        /// User, Password, and Authentication method.
        /// </summary>
        [DataTestMethod]
        [DataRow("Server =<>;Database=<>;User=xyz;", false, false,
            DisplayName = "No managed identity access token when connection string specifies User only.")]
        [DataRow("Server =<>;Database=<>;Password=xyz;", false, false,
            DisplayName = "No managed identity access token when connection string specifies Password only.")]
        [DataRow("Server =<>;Database=<>;Authentication=Active Directory Integrated;", false, false,
            DisplayName = "No managed identity access token when connection string specifies Authentication method only.")]
        [DataRow("Server =<>;Database=<>;User=xyz;Password=xxx", false, false,
            DisplayName = "No managed identity access token when connection string specifies both User and Password.")]
        [DataRow("Server =<>;Database=<>;UID=xyz;Pwd=xxx", false, false,
            DisplayName = "No managed identity access token when connection string specifies Uid and Pwd.")]
        [DataRow("Server =<>;Database=<>;User=xyz;Authentication=Active Directory Service Principal", false, false,
            DisplayName = "No managed identity access when connection string specifies both User and Authentication method.")]
        [DataRow("Server =<>;Database=<>;Password=xxx;Authentication=Active Directory Password;", false, false,
            DisplayName = "No managed identity access token when connection string specifies both Password and Authentication method.")]
        [DataRow("Server =<>;Database=<>;User=xyz;Password=xxx;Authentication=SqlPassword", false, false,
            DisplayName = "No managed identity access token when connection string specifies User, Password and Authentication method.")]
        [DataRow("Server =<>;Database=<>;Trusted_Connection=yes", false, false,
            DisplayName = "No managed identity access token when connection string specifies Trusted Connection.")]
        [DataRow("Server =<>;Database=<>;Integrated Security=true", false, false,
            DisplayName = "No managed identity access token when connection string specifies Integrated Security.")]
        [DataRow("Server =<>;Database=<>;", true, false,
            DisplayName = "Managed identity access token from config used " +
                "when connection string specifies none of User, Password and Authentication method.")]
        [DataRow("Server =<>;Database=<>;", true, true,
            DisplayName = "Default managed identity access token used " +
                "when connection string specifies none of User, Password and Authentication method")]
        public async Task TestHandleManagedIdentityAccess(
            string connectionString,
            bool expectManagedIdentityAccessToken,
            bool isDefaultAzureCredential)
        {
            RuntimeConfigProvider runtimeConfigProvider = TestHelper.GetRuntimeConfigProvider(TestCategory.MSSQL);
            runtimeConfigProvider.GetRuntimeConfiguration().ConnectionString = connectionString;
            Mock<DbExceptionParser> dbExceptionParser = new(runtimeConfigProvider, new HashSet<string>());
            Mock<ILogger<MsSqlQueryExecutor>> queryExecutorLogger = new();
            MsSqlQueryExecutor msSqlQueryExecutor = new(runtimeConfigProvider, dbExceptionParser.Object, queryExecutorLogger.Object);

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
                    msSqlQueryExecutor.AzureCredential = dacMock.Object;
                }
                else
                {
                    runtimeConfigProvider.Initialize(
                        JsonSerializer.Serialize(runtimeConfigProvider.GetRuntimeConfiguration()),
                        schema: null,
                        connectionString: connectionString,
                        accessToken: CONFIG_TOKEN);
                    msSqlQueryExecutor = new(runtimeConfigProvider, dbExceptionParser.Object, queryExecutorLogger.Object);
                }
            }

            using SqlConnection conn = new(connectionString);
            await msSqlQueryExecutor.SetManagedIdentityAccessTokenIfAnyAsync(conn);

            if (expectManagedIdentityAccessToken)
            {
                if (isDefaultAzureCredential)
                {
                    Assert.AreEqual(expected: DEFAULT_TOKEN, actual: conn.AccessToken);
                }
                else
                {
                    Assert.AreEqual(expected: CONFIG_TOKEN, actual: conn.AccessToken);
                }
            }
            else
            {
                Assert.AreEqual(expected: default, actual: conn.AccessToken);
            }
        }

        /// <summary>
        /// Test to validate that the maximum number of retries are being made to execute the query against the database
        /// when the database keeps returning a transient error.
        /// </summary>
        [TestMethod, TestCategory(TestCategory.MSSQL)]
        public async Task TestRetryPolicyExhaustingMaxAttempts()
        {
            int maxRetries = 5;
            int maxAttempts = maxRetries + 1; // because of the original attempt to execute the query in addition to retries.
            RuntimeConfigProvider runtimeConfigProvider = TestHelper.GetRuntimeConfigProvider(TestCategory.MSSQL);
            Mock<ILogger<QueryExecutor<SqlConnection>>> queryExecutorLogger = new();
            DbExceptionParser dbExceptionParser = new MsSqlDbExceptionParser(runtimeConfigProvider);
            Mock<MsSqlQueryExecutor> queryExecutor = new(runtimeConfigProvider, dbExceptionParser, queryExecutorLogger.Object);

            // Mock the ExecuteQueryAgainstDbAsync to throw a transient exception.
            queryExecutor.Setup(x => x.ExecuteQueryAgainstDbAsync(
                It.IsAny<SqlConnection>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, object>>()))
            .Throws(SqlTestHelper.CreateSqlException(_semaphoreTimedOutErrorCode));

            // Call the actual ExecuteQueryAsync method.
            queryExecutor.Setup(x => x.ExecuteQueryAsync(
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, object>>())).CallBase();

            DataApiBuilderException ex = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(async () =>
            {
                await queryExecutor.Object.ExecuteQueryAsync(sqltext: string.Empty, parameters: new Dictionary<string, object>());
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
        [TestMethod, TestCategory(TestCategory.MSSQL)]
        public async Task TestRetryPolicySuccessfullyExecutingQueryAfterNAttempts()
        {
            RuntimeConfigProvider runtimeConfigProvider = TestHelper.GetRuntimeConfigProvider(TestCategory.MSSQL);
            Mock<ILogger<QueryExecutor<SqlConnection>>> queryExecutorLogger = new();
            DbExceptionParser dbExceptionParser = new MsSqlDbExceptionParser(runtimeConfigProvider);
            Mock<MsSqlQueryExecutor> queryExecutor = new(runtimeConfigProvider, dbExceptionParser, queryExecutorLogger.Object);

            // Mock the ExecuteQueryAgainstDbAsync to throw a transient exception.
            queryExecutor.SetupSequence(x => x.ExecuteQueryAgainstDbAsync(
                It.IsAny<SqlConnection>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, object>>()))
            .Throws(SqlTestHelper.CreateSqlException(_semaphoreTimedOutErrorCode))
            .Throws(SqlTestHelper.CreateSqlException(_semaphoreTimedOutErrorCode))
            .CallBase();

            // Call the actual ExecuteQueryAsync method.
            queryExecutor.Setup(x => x.ExecuteQueryAsync(
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, object>>())).CallBase();

            string sqltext = "SELECT * from books";
            await queryExecutor.Object.ExecuteQueryAsync(sqltext: sqltext, parameters: new Dictionary<string, object>());
            // For each attempt logger is invoked twice. The query executes successfully in in 1st retry .i.e. 2nd attempt of execution.
            Assert.AreEqual(2 * 2, queryExecutorLogger.Invocations.Count);
        }
    }
}
