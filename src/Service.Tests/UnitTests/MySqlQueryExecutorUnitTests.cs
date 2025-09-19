// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Threading.Tasks;
using Azure.Core;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.Identity;
using Microsoft.AspNetCore.Http;
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
            RuntimeConfig mockConfig = new(
               Schema: "",
               DataSource: new(DatabaseType.MySQL, connectionString, new()),
               Runtime: new(
                   Rest: new(),
                   GraphQL: new(),
                   Mcp: new(),
                   Host: new(null, null)
               ),
               Entities: new(new Dictionary<string, Entity>())
           );

            MockFileSystem fileSystem = new();
            fileSystem.AddFile(FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME, new MockFileData(mockConfig.ToJson()));
            FileSystemRuntimeConfigLoader loader = new(fileSystem);
            RuntimeConfigProvider provider = new(loader);
            Mock<DbExceptionParser> dbExceptionParser = new(provider);
            Mock<ILogger<MySqlQueryExecutor>> queryExecutorLogger = new();
            Mock<IHttpContextAccessor> httpContextAccessor = new();
            MySqlQueryExecutor mySqlQueryExecutor = new(provider, dbExceptionParser.Object, queryExecutorLogger.Object, httpContextAccessor.Object);

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
                    await provider.Initialize(
                        provider.GetConfig().ToJson(),
                        graphQLSchema: null,
                        connectionString: connectionString,
                        accessToken: CONFIG_TOKEN);
                    mySqlQueryExecutor = new(provider, dbExceptionParser.Object, queryExecutorLogger.Object, httpContextAccessor.Object);
                }
            }

            using MySqlConnection conn = new(connectionString);
            await mySqlQueryExecutor.SetManagedIdentityAccessTokenIfAnyAsync(conn, string.Empty);
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
