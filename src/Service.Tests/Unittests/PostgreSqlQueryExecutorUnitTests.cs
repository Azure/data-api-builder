using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Core;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.DataApiBuilder.Service.Resolvers;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Npgsql;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass, TestCategory(TestCategory.POSTGRESQL)]
    public class PostgreSqlQueryExecutorUnitTests
    {
        /// <summary>
        /// Validates managed identity token issued ONLY when ManagedIdentityAccessToken is "USE DEFAULT"
        /// </summary>
        [DataTestMethod]
        [DataRow("Server =<>;Database=<>;username=<>;password=<>", null, false, false,
            DisplayName = "Managed identity is not used")]
        [DataRow("Server =<>;Database=<>;", "xxx", true, false,
            DisplayName = "Managed identity access token from config used " +
                "when ManagedIdentityAccessToken is not null and not \"USE DEFAULT\".")]
        [DataRow("Server =<>;Database=<>;", "USE DEFAULT", true, true,
            DisplayName = "Default managed identity access token used " +
                "when ManagedIdentityAccessToken is \"USE DEFAULT\".")]
        public async Task TestHandleManagedIdentityAccess(
            string connectionString,
            string managedIdentityAccessToken,
            bool expectManagedIdentityAccessToken,
            bool isDefaultAzureCredential)
        {
            RuntimeConfigProvider runtimeConfigProvider = TestHelper.GetRuntimeConfigProvider(TestCategory.POSTGRESQL);
            runtimeConfigProvider.Initialize(
                        JsonSerializer.Serialize(runtimeConfigProvider.GetRuntimeConfiguration()),
                        schema: null,
                        connectionString: connectionString,
                        accessToken: managedIdentityAccessToken);

            Mock<DbExceptionParser> dbExceptionParser = new(runtimeConfigProvider, new HashSet<string>());
            Mock<ILogger<PostgreSqlQueryExecutor>> queryExecutorLogger = new();
            PostgreSqlQueryExecutor postgreSqlQueryExecutor = new(runtimeConfigProvider, dbExceptionParser.Object, queryExecutorLogger.Object);

            const string DEFAULT_TOKEN = "Default access token";
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
                    postgreSqlQueryExecutor.AzureCredential = dacMock.Object;
                }
                else
                {
                    postgreSqlQueryExecutor = new(runtimeConfigProvider, dbExceptionParser.Object, queryExecutorLogger.Object);
                }
            }

            using NpgsqlConnection conn = new(connectionString);
            await postgreSqlQueryExecutor.SetManagedIdentityAccessTokenIfAnyAsync(conn);

            if (expectManagedIdentityAccessToken)
            {
                if (isDefaultAzureCredential)
                {
                    Assert.IsTrue(conn.ConnectionString.Contains(DEFAULT_TOKEN), "Password not set to default access token");
                }
                else
                {
                    Assert.IsTrue(conn.ConnectionString.Contains(managedIdentityAccessToken), $"Password not set to the config access token");
                }
            }
            else
            {
                // password is not changed to the access token
                Assert.AreEqual(connectionString, conn.ConnectionString);
            }
        }
    }
}
