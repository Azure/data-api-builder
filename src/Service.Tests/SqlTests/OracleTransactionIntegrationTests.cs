// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Service.Tests.UnitTests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Oracle.ManagedDataAccess.Client;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests;

[TestClass, TestCategory(TestCategory.ORACLE)]
[DoNotParallelize]
public class OracleTransactionIntegrationTests : SqlTestBase
{
    private const string SESSION_QUERY = "SELECT SYS_CONTEXT('USERENV', 'SID') FROM DUAL";

    [ClassInitialize]
    public static async Task SetupAsync(TestContext context)
    {
        Assert.IsFalse(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DAB_ORACLE_TEST_CONNECTION_STRING")),
            "Set DAB_ORACLE_TEST_CONNECTION_STRING to a dedicated Oracle test schema.");
        DatabaseEngine = TestCategory.ORACLE;
        _queryEngineFactory = new Mock<IQueryEngineFactory>();
        await InitializeTestFixture(connectionStringOptions: "Pooling=false");
    }

    [ClassCleanup]
    public static void CleanupFixture()
    {
        _application?.Dispose();
        TestHelper.UnsetAllDABEnvironmentVariables();
    }

    [TestInitialize]
    public async Task ResetDatabaseAsync()
    {
        TestHelper.SetupDatabaseEnvironment(TestCategory.ORACLE);
        await ResetDbStateAsync();
    }

    [TestMethod]
    public async Task GraphQLMutationsReturnFollowUpResultsWithoutPooling()
    {
        JsonElement created = await ExecuteGraphQLRequestAsync(
            "mutation { createBook(item: { title: \"Unpooled create\", publisher_id: 1234 }) { id title publisher { name } } }",
            "createBook", isAuthenticated: false);
        int id = created.GetProperty("id").GetInt32();
        Assert.AreEqual(1000, id);
        Assert.AreEqual("Unpooled create", created.GetProperty("title").GetString());
        Assert.AreEqual("Big Company", created.GetProperty("publisher").GetProperty("name").GetString());

        JsonElement updated = await ExecuteGraphQLRequestAsync(
            $"mutation {{ updateBook(id: {id}, item: {{ title: \"Unpooled update\" }}) {{ id title publisher {{ name }} }} }}",
            "updateBook", isAuthenticated: false);
        Assert.AreEqual(id, updated.GetProperty("id").GetInt32());
        Assert.AreEqual("Unpooled update", updated.GetProperty("title").GetString());
        Assert.AreEqual("Big Company", updated.GetProperty("publisher").GetProperty("name").GetString());

        JsonElement deleted = await ExecuteGraphQLRequestAsync(
            $"mutation {{ deleteBook(id: {id}) {{ id title }} }}", "deleteBook", isAuthenticated: false);
        Assert.AreEqual(id, deleted.GetProperty("id").GetInt32());
        Assert.AreEqual("Unpooled update", deleted.GetProperty("title").GetString());
        using JsonDocument count = JsonDocument.Parse(await GetDatabaseResultAsync($"SELECT JSON_OBJECT('count' VALUE COUNT(*)) FROM BOOKS WHERE ID = {id}"));
        Assert.AreEqual(0, count.RootElement.GetProperty("count").GetInt32());
    }

    [DataTestMethod]
    [DataRow("POST", true)]
    [DataRow("POST", false)]
    [DataRow("PUT", true)]
    [DataRow("PUT", false)]
    [DataRow("PATCH", true)]
    [DataRow("PATCH", false)]
    public async Task RestPolicyFollowUpPreservesUnpooledMutations(string method, bool satisfiesReadPolicy)
    {
        const string base64 = "ABEiM0RVZneImaq7zN3u/w==";
        const string hex = "00112233445566778899AABBCCDDEEFF";
        const string pointRoute = "api/BinaryPolicySample/id/ABEiM0RVZneImaq7zN3u%2Fw%3D%3D";
        string? rawValue = satisfiesReadPolicy ? "AQID" : null;
        using HttpRequestMessage request = new(new HttpMethod(method), method == "POST" ? "api/BinaryPolicySample" : pointRoute)
        {
            Content = method == "POST" ? JsonContent.Create(new { id = base64, raw_value = rawValue, payload = "BAUG" })
                : JsonContent.Create(new { raw_value = rawValue, payload = "BAUG" })
        };
        using HttpResponseMessage response = await HttpClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
        using JsonDocument result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement rows = result.RootElement.GetProperty("value");
        Assert.AreEqual(satisfiesReadPolicy ? 1 : 0, rows.GetArrayLength());
        if (satisfiesReadPolicy)
        {
            Assert.AreEqual(base64, rows[0].GetProperty("id").GetString());
            Assert.AreEqual(rawValue, rows[0].GetProperty("raw_value").GetString());
            Assert.IsFalse(rows[0].TryGetProperty("payload", out _));
        }

        using JsonDocument persisted = JsonDocument.Parse(await GetDatabaseResultAsync(
            $"SELECT JSON_OBJECT('id' VALUE RAWTOHEX(ID), 'raw_value' VALUE RAWTOHEX(RAW_VALUE), 'payload_length' VALUE DBMS_LOB.GETLENGTH(PAYLOAD)) FROM DAB_ORACLE_BINARIES WHERE ID = HEXTORAW('{hex}')"));
        Assert.AreEqual(hex, persisted.RootElement.GetProperty("id").GetString());
        Assert.AreEqual(satisfiesReadPolicy ? "010203" : null, persisted.RootElement.GetProperty("raw_value").GetString());
        Assert.AreEqual(3, persisted.RootElement.GetProperty("payload_length").GetInt32());
    }

    [TestMethod]
    public async Task MixedSyncAndAsyncQueriesReuseOneUnpooledSessionPerTransaction()
    {
        OracleTestFixture fixture = CreateUnpooledFixture();
        using TransactionScope scope = CreateScope();
        string first = ReadSession(fixture);
        string second = await ReadSessionAsync(fixture);
        string third = ReadSession(fixture);

        Assert.AreEqual(first, second);
        Assert.AreEqual(first, third);
        Assert.AreEqual(Guid.Empty, Transaction.Current.TransactionInformation.DistributedIdentifier);
        scope.Complete();
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UnpooledTransactionCommitsOrRollsBackAsRequested(bool complete)
    {
        OracleTestFixture fixture = CreateUnpooledFixture();
        using (TransactionScope scope = CreateScope())
        {
            await fixture.Executor.ExecuteQueryAsync<object>("INSERT INTO DAB_ORACLE_BOOKS (ID, TITLE) VALUES (81, 'transactional')",
                parameters: null, dataReaderHandler: null, dataSourceName: fixture.SourceName);
            Assert.AreEqual(1, await CountTransactionRowsAsync(fixture));
            Assert.AreEqual(Guid.Empty, Transaction.Current.TransactionInformation.DistributedIdentifier);
            if (complete)
            {
                scope.Complete();
            }
        }

        Assert.AreEqual(complete ? 1 : 0, await CountTransactionRowsAsync(fixture));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TransactionCompletionDisposesAndForgetsUnpooledConnections(bool complete)
    {
        OracleTestFixture fixture = CreateUnpooledFixture();
        ObservingOracleQueryExecutor executor = new(fixture);
        Task<string> Read() => executor.ExecuteQueryAsync(SESSION_QUERY, parameters: null, async (reader, _) =>
        {
            Assert.IsTrue(await reader.ReadAsync());
            return reader.GetString(0);
        }, fixture.SourceName);
        OracleConnection first;
        using (TransactionScope scope = CreateScope())
        {
            await Read();
            first = executor.Connections.Single();
            Assert.AreEqual(ConnectionState.Open, first.State, "A transaction's connection must stay open until the transaction finishes.");
            if (complete)
            {
                scope.Complete();
            }
        }

        Assert.AreEqual(ConnectionState.Closed, first.State, "Completing or rolling back a transaction must close its unpooled connection.");
        Assert.AreEqual(0, executor.RetainedTransactionCount);
        OracleConnection second;
        using (TransactionScope scope = CreateScope())
        {
            await Read();
            Assert.AreEqual(2, executor.Connections.Count);
            second = executor.Connections[1];
            Assert.AreNotSame(first, second, "A completed transaction's unpooled connection cannot be reused by another transaction.");
            Assert.AreEqual(ConnectionState.Open, second.State);
            if (complete)
            {
                scope.Complete();
            }
        }

        Assert.AreEqual(ConnectionState.Closed, second.State);
        Assert.AreEqual(0, executor.RetainedTransactionCount);
    }

    [TestMethod]
    public async Task ParallelAsyncQueriesShareOneUnpooledTransactionSession()
    {
        OracleTestFixture fixture = CreateUnpooledFixture();
        using TransactionScope scope = CreateScope();
        string expected = await ReadSessionAsync(fixture);
        string[] sessions = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => ReadSessionAsync(fixture)));

        CollectionAssert.AreEqual(Enumerable.Repeat(expected, 6).ToArray(), sessions);
        Assert.AreEqual(Guid.Empty, Transaction.Current.TransactionInformation.DistributedIdentifier);
        scope.Complete();
    }

    [TestMethod]
    public async Task CancellingQueuedQueryDoesNotReleaseActiveConnectionOrBlockLaterQueries()
    {
        OracleTestFixture fixture = CreateUnpooledFixture();
        using TransactionScope scope = CreateScope();
        TaskCompletionSource<bool> readerEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> releaseReader = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<string> activeQuery = fixture.Executor.ExecuteQueryAsync(SESSION_QUERY, parameters: null, async (reader, _) =>
        {
            Assert.IsTrue(await reader.ReadAsync());
            string session = reader.GetString(0);
            readerEntered.SetResult(true);
            await releaseReader.Task;
            return session;
        }, fixture.SourceName);
        string activeSession = null;
        try
        {
            await readerEntered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            using CancellationTokenSource cancellation = new();
            Task<string> queuedQuery = fixture.Executor.ExecuteQueryAsync(SESSION_QUERY, parameters: null, async (reader, _) =>
            {
                Assert.IsTrue(await reader.ReadAsync());
                return reader.GetString(0);
            }, fixture.SourceName, cancellation.Token);
            cancellation.Cancel();
            OperationCanceledException cancellationError = null;
            try
            {
                await queuedQuery;
            }
            catch (OperationCanceledException error)
            {
                cancellationError = error;
            }

            Assert.IsNotNull(cancellationError, "The query waiting for the active session must observe cancellation.");
            Assert.AreEqual(cancellation.Token, cancellationError.CancellationToken);
            Assert.IsFalse(activeQuery.IsCompleted, "Cancelling a queued lease must not release another query's reader.");
        }
        finally
        {
            releaseReader.TrySetResult(true);
            activeSession = await activeQuery;
        }

        Assert.AreEqual(activeSession, await ReadSessionAsync(fixture));
        Assert.AreEqual(Guid.Empty, Transaction.Current.TransactionInformation.DistributedIdentifier);
        scope.Complete();
    }

    [TestMethod]
    public async Task AbortedTransactionRejectsQueryAlreadyWaitingForItsConnection()
    {
        OracleTestFixture fixture = CreateUnpooledFixture();
        using TransactionScope scope = CreateScope();
        Transaction transaction = Transaction.Current;
        TaskCompletionSource<bool> transactionCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        // Subscribe first: the provider's completion callback waits for the active reader's lease.
        transaction.TransactionCompleted += (_, _) => transactionCompleted.TrySetResult(true);
        TaskCompletionSource<bool> readerEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> releaseReader = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<string> activeQuery = fixture.Executor.ExecuteQueryAsync(SESSION_QUERY, parameters: null, async (reader, _) =>
        {
            Assert.IsTrue(await reader.ReadAsync());
            string session = reader.GetString(0);
            readerEntered.SetResult(true);
            await releaseReader.Task;
            return session;
        }, fixture.SourceName);
        Task rollback = null;
        int queuedHandlerExecutions = 0;
        try
        {
            await readerEntered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Task<string> queuedQuery = fixture.Executor.ExecuteQueryAsync(SESSION_QUERY, parameters: null, async (reader, _) =>
            {
                Interlocked.Increment(ref queuedHandlerExecutions);
                Assert.IsTrue(await reader.ReadAsync());
                return reader.GetString(0);
            }, fixture.SourceName);
            Assert.IsFalse(queuedQuery.IsCompleted, "The second query must be waiting for the active lease.");
            rollback = Task.Run(() => transaction.Rollback());
            await transactionCompleted.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.AreEqual(TransactionStatus.Aborted, transaction.TransactionInformation.Status);
            releaseReader.TrySetResult(true);

            await Assert.ThrowsExceptionAsync<TransactionException>(() => queuedQuery);
            Assert.AreEqual(0, Volatile.Read(ref queuedHandlerExecutions), "An aborted transaction must reject its queued query before reading data.");
        }
        finally
        {
            releaseReader.TrySetResult(true);
            await activeQuery.WaitAsync(TimeSpan.FromSeconds(30));
            if (rollback is not null)
            {
                await rollback.WaitAsync(TimeSpan.FromSeconds(30));
            }
        }
    }

    private static OracleTestFixture CreateUnpooledFixture()
    {
        OracleConnectionStringBuilder connection = new(Environment.GetEnvironmentVariable("DAB_ORACLE_TEST_CONNECTION_STRING")) { Pooling = false };
        return new(connection.ConnectionString);
    }

    private sealed class ObservingOracleQueryExecutor(OracleTestFixture fixture)
        : OracleQueryExecutor(fixture.Config, new OracleDbExceptionParser(fixture.Config), NullLogger<IQueryExecutor>.Instance, fixture.Http)
    {
        public List<OracleConnection> Connections { get; } = new();

        public int RetainedTransactionCount
        {
            get
            {
                // Pair the real connection-state observation with a guard against retaining completed transaction entries.
                object cache = typeof(OracleQueryExecutor).GetField("_transactionConnections", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(this)!;
                return (int)cache.GetType().GetProperty("Count")!.GetValue(cache)!;
            }
        }

        public override OracleConnection CreateConnection(string dataSourceName)
        {
            OracleConnection connection = base.CreateConnection(dataSourceName);
            Connections.Add(connection);
            return connection;
        }
    }

    private static TransactionScope CreateScope() => new(TransactionScopeOption.RequiresNew,
        new TransactionOptions { IsolationLevel = System.Transactions.IsolationLevel.ReadCommitted, Timeout = TimeSpan.FromSeconds(30) },
        TransactionScopeAsyncFlowOption.Enabled);

    private static string ReadSession(OracleTestFixture fixture) => fixture.Executor.ExecuteQuery(SESSION_QUERY,
        parameters: null, (reader, _) =>
        {
            Assert.IsTrue(reader.Read());
            return reader.GetString(0);
        }, dataSourceName: fixture.SourceName);

    private static Task<string> ReadSessionAsync(OracleTestFixture fixture) => fixture.Executor.ExecuteQueryAsync(SESSION_QUERY,
        parameters: null, async (reader, _) =>
        {
            Assert.IsTrue(await reader.ReadAsync());
            return reader.GetString(0);
        }, fixture.SourceName);

    private static Task<int> CountTransactionRowsAsync(OracleTestFixture fixture) => fixture.Executor.ExecuteQueryAsync(
        "SELECT COUNT(*) FROM DAB_ORACLE_BOOKS WHERE ID = 81", parameters: null, async (reader, _) =>
        {
            Assert.IsTrue(await reader.ReadAsync());
            return reader.GetInt32(0);
        }, fixture.SourceName);
}
