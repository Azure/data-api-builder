using System;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Core;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.DataApiBuilder.Service.Resolvers;
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
            DbExceptionParser dbExceptionParser = new(runtimeConfigProvider);
            Mock<ILogger<MsSqlQueryExecutor>> queryExecutorLogger = new();
            MsSqlQueryExecutor msSqlQueryExecutor = new(runtimeConfigProvider, dbExceptionParser, queryExecutorLogger.Object);

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
                    msSqlQueryExecutor = new(runtimeConfigProvider, dbExceptionParser, queryExecutorLogger.Object);
                }
            }

            using SqlConnection conn = new(connectionString);
            await msSqlQueryExecutor.HandleManagedIdentityAccessIfAnyAsync(conn);

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
    }
}
