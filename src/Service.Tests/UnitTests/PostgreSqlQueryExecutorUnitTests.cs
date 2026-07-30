// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Data;
using System.Net;
using System.Threading.Tasks;
using Azure.Core;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Service.Exceptions;
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

        /// <summary>
        /// The PostgreSQL upsert result-set contract starts with advisory-lock confirmation,
        /// followed by the existence count and the mutation result.
        /// </summary>
        [TestMethod]
        public async Task UpsertResultSetsConsumeLockConfirmationBeforeCount()
        {
            PostgreSqlQueryExecutor queryExecutor = CreateQueryExecutor();
            using DataSet resultSets = new();
            resultSets.Tables.Add(CreateResultTable(PostgresQueryBuilder.UPSERT_LOCK_RESULT, DBNull.Value));

            DataTable countResult = new();
            countResult.Columns.Add(PostgresQueryBuilder.COUNT_ROWS_WITH_GIVEN_PK, typeof(long));
            countResult.Columns.Add(PostgresQueryBuilder.IS_FALLBACK_TO_UPDATE, typeof(bool));
            countResult.Rows.Add(0L, false);
            resultSets.Tables.Add(countResult);

            resultSets.Tables.Add(CreateResultTable("id", 42));
            using DataTableReader reader = resultSets.CreateDataReader();

            DbResultSet result = await queryExecutor.GetMultipleResultSetsIfAnyAsync(reader);

            Assert.AreEqual(1, result.Rows.Count);
            Assert.AreEqual(42, result.Rows[0].Columns["id"]);
        }

        /// <summary>
        /// A changed or missing lock result must fail closed rather than shifting result-set parsing.
        /// </summary>
        [TestMethod]
        public async Task UpsertResultSetsRejectMissingLockConfirmation()
        {
            PostgreSqlQueryExecutor queryExecutor = CreateQueryExecutor();
            using DataSet resultSets = new();
            resultSets.Tables.Add(CreateResultTable("unexpected", DBNull.Value));
            using DataTableReader reader = resultSets.CreateDataReader();

            DataApiBuilderException exception = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(
                () => queryExecutor.GetMultipleResultSetsIfAnyAsync(reader));

            Assert.AreEqual(HttpStatusCode.InternalServerError, exception.StatusCode);
        }

        /// <summary>
        /// Adding the lock result set must not change successful existing-row update handling.
        /// </summary>
        [TestMethod]
        public async Task UpsertResultSetsPreserveExistingRowUpdate()
        {
            PostgreSqlQueryExecutor queryExecutor = CreateQueryExecutor();
            using DataSet resultSets = CreateUpsertResultSets(existingRowCount: 1, isFallbackToUpdate: false);
            resultSets.Tables.Add(CreateResultTable("id", 42));
            using DataTableReader reader = resultSets.CreateDataReader();

            DbResultSet result = await queryExecutor.GetMultipleResultSetsIfAnyAsync(reader);

            Assert.AreEqual(1, result.Rows.Count);
            Assert.AreEqual(42, result.Rows[0].Columns["id"]);
        }

        /// <summary>
        /// An existing row with no mutation result remains an update-policy denial.
        /// </summary>
        [TestMethod]
        public async Task UpsertResultSetsPreserveUpdatePolicyDenial()
        {
            PostgreSqlQueryExecutor queryExecutor = CreateQueryExecutor();
            using DataSet resultSets = CreateUpsertResultSets(existingRowCount: 1, isFallbackToUpdate: false);
            resultSets.Tables.Add(CreateEmptyResultTable("id"));
            using DataTableReader reader = resultSets.CreateDataReader();

            DataApiBuilderException exception = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(
                () => queryExecutor.GetMultipleResultSetsIfAnyAsync(reader));

            Assert.AreEqual(HttpStatusCode.Forbidden, exception.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.DatabasePolicyFailure, exception.SubStatusCode);
        }

        /// <summary>
        /// An update-only fallback with no matching row remains a not-found response.
        /// </summary>
        [TestMethod]
        public async Task UpsertResultSetsPreserveUpdateOnlyNotFoundResult()
        {
            PostgreSqlQueryExecutor queryExecutor = CreateQueryExecutor();
            using DataSet resultSets = CreateUpsertResultSets(existingRowCount: 0, isFallbackToUpdate: true);
            resultSets.Tables.Add(CreateEmptyResultTable("id"));
            using DataTableReader reader = resultSets.CreateDataReader();

            DataApiBuilderException exception = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(
                () => queryExecutor.GetMultipleResultSetsIfAnyAsync(reader, new() { "id/42", "Book" }));

            Assert.AreEqual(HttpStatusCode.NotFound, exception.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ItemNotFound, exception.SubStatusCode);
        }

        /// <summary>
        /// A missing row with no insert result remains a create-policy denial.
        /// </summary>
        [TestMethod]
        public async Task UpsertResultSetsPreserveCreatePolicyDenial()
        {
            PostgreSqlQueryExecutor queryExecutor = CreateQueryExecutor();
            using DataSet resultSets = CreateUpsertResultSets(existingRowCount: 0, isFallbackToUpdate: false);
            resultSets.Tables.Add(CreateEmptyResultTable("id"));
            using DataTableReader reader = resultSets.CreateDataReader();

            DataApiBuilderException exception = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(
                () => queryExecutor.GetMultipleResultSetsIfAnyAsync(reader));

            Assert.AreEqual(HttpStatusCode.Forbidden, exception.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.DatabasePolicyFailure, exception.SubStatusCode);
        }

        private static PostgreSqlQueryExecutor CreateQueryExecutor()
        {
            RuntimeConfig runtimeConfig = new(
                Schema: string.Empty,
                DataSource: new(DatabaseType.PostgreSQL, "Server=localhost;Database=dab;Username=dab;Password=dab", new()),
                Runtime: new(Rest: new(), GraphQL: new(), Mcp: new(), Host: new(null, null)),
                Entities: new(new Dictionary<string, Entity>()));
            RuntimeConfigProvider provider = TestHelper.GenerateInMemoryRuntimeConfigProvider(runtimeConfig);

            return new PostgreSqlQueryExecutor(
                provider,
                new Mock<DbExceptionParser>(provider).Object,
                new Mock<ILogger<PostgreSqlQueryExecutor>>().Object,
                new Mock<IHttpContextAccessor>().Object);
        }

        private static DataTable CreateResultTable(string columnName, object value)
        {
            DataTable table = new();
            table.Columns.Add(columnName, value is DBNull ? typeof(object) : value.GetType());
            table.Rows.Add(value);
            return table;
        }

        private static DataTable CreateEmptyResultTable(string columnName)
        {
            DataTable table = new();
            table.Columns.Add(columnName, typeof(int));
            return table;
        }

        private static DataSet CreateUpsertResultSets(long existingRowCount, bool isFallbackToUpdate)
        {
            DataSet resultSets = new();
            resultSets.Tables.Add(CreateResultTable(PostgresQueryBuilder.UPSERT_LOCK_RESULT, DBNull.Value));

            DataTable countResult = new();
            countResult.Columns.Add(PostgresQueryBuilder.COUNT_ROWS_WITH_GIVEN_PK, typeof(long));
            countResult.Columns.Add(PostgresQueryBuilder.IS_FALLBACK_TO_UPDATE, typeof(bool));
            countResult.Rows.Add(existingRowCount, isFallbackToUpdate);
            resultSets.Tables.Add(countResult);
            return resultSets;
        }
    }
}
