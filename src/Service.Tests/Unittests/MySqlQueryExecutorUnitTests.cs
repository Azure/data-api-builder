// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Core;
using Azure.DataApiBuilder.Service.Configurations;
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
        [DataRow("Server =<>;Database=<>;User=xyz;", true, false,
            DisplayName = "Managed identity access token from config used when connection string specifies User but not the Password.")]
        [DataRow("Server =<>;Database=<>;User=xyz;", true, true,
            DisplayName = "Managed identity access token from Default Azure Credential used when connection string specifies User but not the Password.")]
        public async Task TestHandleManagedIdentityAccess(
            string connectionString,
            bool expectManagedIdentityAccessToken,
            bool isDefaultAzureCredential)
        {
            RuntimeConfigProvider runtimeConfigProvider = TestHelper.GetRuntimeConfigProvider(TestCategory.MYSQL);
            runtimeConfigProvider.GetRuntimeConfiguration().ConnectionString = connectionString;
            Mock<DbExceptionParser> dbExceptionParser = new(runtimeConfigProvider);
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
                    await runtimeConfigProvider.Initialize(
                        JsonSerializer.Serialize(runtimeConfigProvider.GetRuntimeConfiguration()),
                        schema: null,
                        connectionString: connectionString,
                        accessToken: CONFIG_TOKEN);
                    mySqlQueryExecutor = new(runtimeConfigProvider, dbExceptionParser.Object, queryExecutorLogger.Object);
                }
            }

            using MySqlConnection conn = new(connectionString);
            await mySqlQueryExecutor.SetManagedIdentityAccessTokenIfAnyAsync(conn, context: null);
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
    }
}
