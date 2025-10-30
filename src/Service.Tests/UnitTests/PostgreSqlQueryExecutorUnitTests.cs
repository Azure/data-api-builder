// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.Core;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Npgsql;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass, TestCategory(TestCategory.POSTGRESQL)]
    public class PostgreSqlQueryExecutorUnitTests
    {
        [TestInitialize]
        public void TestInitialize()
        {
            TestHelper.SetupDatabaseEnvironment(TestCategory.POSTGRESQL);
        }

        [TestCleanup]
        public void TestCleanup()
        {
            TestHelper.UnsetAllDABEnvironmentVariables();
        }

        /// <summary>
        /// Validates managed identity token issued ONLY when connection string does not specify password
        /// </summary>
        [DataTestMethod]
        [DataRow("Server =<>;Database=<>;Username=xyz;", false, false,
            DisplayName = "No managed identity access token even when connection string specifies Username only.")]
        [DataRow("Server =<>;Database=<>;Username=xyz;", true, false,
            DisplayName = "Managed identity access token from config used when connection string specifies Username only.")]
        [DataRow("Server =<>;Database=<>;Username=xyz;", true, true,
            DisplayName = "Default managed identity access token used when connection string specifies Username only.")]
        [DataRow("Server =<>;Database=<>;Password=xyz;", false, false,
            DisplayName = "No managed identity access token when connection string specifies Password only.")]
        [DataRow("Server =<>;Database=<>;Username=xyz;Password=xxx", false, false,
            DisplayName = "No managed identity access token when connection string specifies both Username and Password.")]
        public async Task TestHandleManagedIdentityAccess(
            string connectionString,
            bool expectManagedIdentityAccessToken,
            bool isDefaultAzureCredential)
        {
            RuntimeConfig mockConfig = new(
               Schema: "",
               DataSource: new(DatabaseType.PostgreSQL, connectionString, new()),
               Runtime: new(
                   Rest: new(),
                   GraphQL: new(),
                   Mcp: new(),
                   Host: new(null, null)
               ),
               Entities: new(new Dictionary<string, Entity>())
            );

            RuntimeConfigProvider provider = TestHelper.GenerateInMemoryRuntimeConfigProvider(mockConfig);
            Mock<DbExceptionParser> dbExceptionParser = new(provider);
            Mock<ILogger<PostgreSqlQueryExecutor>> queryExecutorLogger = new();
            Mock<IHttpContextAccessor> httpContextAccessor = new();
            PostgreSqlQueryExecutor postgreSqlQueryExecutor = new(provider, dbExceptionParser.Object, queryExecutorLogger.Object, httpContextAccessor.Object);

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
                    postgreSqlQueryExecutor.AzureCredential = dacMock.Object;
                }
                else
                {
                    await provider.Initialize(
                        provider.GetConfig().ToJson(),
                        graphQLSchema: null,
                        connectionString: connectionString,
                        accessToken: CONFIG_TOKEN);
                    postgreSqlQueryExecutor = new(provider, dbExceptionParser.Object, queryExecutorLogger.Object, httpContextAccessor.Object);
                }
            }

            using NpgsqlConnection conn = new(connectionString);
            await postgreSqlQueryExecutor.SetManagedIdentityAccessTokenIfAnyAsync(conn, string.Empty);
            NpgsqlConnectionStringBuilder connStringBuilder = new(conn.ConnectionString);

            if (expectManagedIdentityAccessToken)
            {
                if (isDefaultAzureCredential)
                {
                    Assert.AreEqual(expected: DEFAULT_TOKEN, actual: connStringBuilder.Password);
                }
                else
                {
                    Assert.AreEqual(expected: CONFIG_TOKEN, actual: connStringBuilder.Password);
                }
            }
            else
            {
                Assert.AreEqual(connectionString, conn.ConnectionString);
            }
        }
    }
}
