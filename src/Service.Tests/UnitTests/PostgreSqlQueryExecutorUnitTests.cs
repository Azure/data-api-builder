// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Data;
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
        /// Verifies that the advisory-lock result emitted by insert-capable upserts is skipped before
        /// the existing count and mutation result sets are interpreted.
        /// </summary>
        [TestMethod]
        public async Task InsertCapableUpsertSkipsAdvisoryLockResultSet()
        {
            RuntimeConfigProvider provider = TestHelper.GetRuntimeConfigProvider(TestHelper.GetRuntimeConfigLoader());
            Mock<DbExceptionParser> dbExceptionParser = new(provider);
            Mock<ILogger<PostgreSqlQueryExecutor>> queryExecutorLogger = new();
            Mock<IHttpContextAccessor> httpContextAccessor = new();
            PostgreSqlQueryExecutor executor = new(
                provider,
                dbExceptionParser.Object,
                queryExecutorLogger.Object,
                httpContextAccessor.Object);

            DataTable lockResult = new();
            lockResult.Columns.Add(PostgresQueryBuilder.UPSERT_LOCK_ACQUIRED, typeof(object));
            lockResult.Rows.Add(DBNull.Value);

            DataTable countResult = new();
            countResult.Columns.Add(PostgresQueryBuilder.COUNT_ROWS_WITH_GIVEN_PK, typeof(long));
            countResult.Columns.Add(PostgresQueryBuilder.IS_FALLBACK_TO_UPDATE, typeof(bool));
            countResult.Rows.Add(0L, false);

            DataTable mutationResult = new();
            mutationResult.Columns.Add("id", typeof(int));
            mutationResult.Rows.Add(42);

            using DataTableReader reader = new(new[] { lockResult, countResult, mutationResult });

            var result = await executor.GetMultipleResultSetsIfAnyAsync(reader);

            Assert.AreEqual(1, result.Rows.Count);
            Assert.AreEqual(42, result.Rows[0].Columns["id"]);
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
                        accessToken: CONFIG_TOKEN,
                        replacementSettings: new());
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
