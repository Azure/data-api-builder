using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.DataApiBuilder.Service.Resolvers;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.Unittests
{
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class SqlQueryExecutorUnitTests
    {
        [DataTestMethod]
        [DataRow("Server =<>;Databases =<>;User=xyz;", false,
            "No managed identity access token when connection string specifies User only.")]
        [DataRow("Server =<>;Databases =<>;Password=xyz;", false,
            "No managed identity access token when connection string specifies Password only.")]
        [DataRow("Server =<>;Databases =<>;Authentication=Active Directory Integrated;", false,
            "No managed identity access token when connection string specifies Authentication method only.")]
        [DataRow("Server =<>;Databases =<>;User=xyz;Password=xxx", false,
            "No managed identity access token when connection string specifies both User and Password.")]
        [DataRow("Server =<>;Databases =<>;UID=xyz;Pwd", false,
            "No managed identity access token when connection string specifies Uid and Pwd.")]
        [DataRow("Server =<>;Databases =<>;User=xyz;Authentication=Active Directory Service Principal", false,
            "No managed identity access when connection string specifies both User and Authentication method.")]
        [DataRow("Server =<>;Databases =<>;Password=xxx;Authentication=Active Directory Password;", false,
            "No managed identity access token when connection string specifies both Password and Authentication method.")]
        [DataRow("Server =<>;Databases =<>;User=xyz;Password=xxx;Authentication=SqlPassword", false,
            "No managed identity access token when connection string specifies User, Password and Authentication method.")]
        [DataRow("Server =<>;Databases =<>;", true,
            "Managed identity access token used when connection string specifies none of User, Password and Authentication method.")]
        public async Task TestHandleManagedIdentityAccess(
            string connectionString,
            bool expectManagedIdentityAccessToken,
            bool isDefaultAzureCredential)
        {
            RuntimeConfigProvider runtimeConfigProvider = TestHelper.GetRuntimeConfigProvider(TestCategory.MSSQL);
            DbExceptionParser dbExceptionParser = new(runtimeConfigProvider);
            Mock<ILogger<MsSqlQueryExecutor>> queryExecutorLogger = new();

            MsSqlQueryExecutor msSqlQueryExecutor = new(runtimeConfigProvider, dbExceptionParser, queryExecutorLogger.Object);
            SqlConnection conn = new(connectionString);
            await msSqlQueryExecutor.HandleManagedIdentityAccessIfAnyAsync(conn);

            if (!expectManagedIdentityAccessToken)
            {
                Assert.Equals(default, conn.AccessToken);
            }
            else
            {
                // 2 cases:
                // a. Mock managedIdentityAccessToken via runtime config provider
                // b. Mock DefaultAzureCredential
            }

        }
    }
}
