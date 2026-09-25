// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Moq.Protected;

namespace Azure.DataApiBuilder.Service.Tests.Telemetry
{
    /// <summary>
    /// Runs the actual generic query executor across configuration replacement with a controlled
    /// ADO.NET connection/command. No database connection or credential acquisition is attempted.
    /// </summary>
    [TestClass]
    [TestCategory("EngineTelemetry")]
    public class EngineTelemetryQueryExecutorTests
    {
        [DataTestMethod]
        [DataRow(false, false, "captured", "ms_sql")]
        [DataRow(true, false, "captured", "ms_sql")]
        [DataRow(false, true, "captured", "ms_sql")]
        [DataRow(true, true, "captured", "ms_sql")]
        [DataRow(false, true, "replacement", "postgre_sql")]
        [DataRow(true, true, "replacement", "postgre_sql")]
        [DataRow(false, true, "superseded", "unknown")]
        [DataRow(true, true, "superseded", "unknown")]
        public async Task CommandAttemptsSurviveConfigurationReplacement(bool asynchronous, bool reloadBeforeExecution,
            string sourceModel, string expectedProvider)
        {
            CapturingExporter exporter = new();
            using EngineTelemetrySession session = EngineTelemetrySession.Create(
                () => exporter, enableSyntheticCollection: true, readEnvironmentVariable: _ => null,
                showNotice: () => { }, resolveIdentity: _ => new(Guid.NewGuid(), "ephemeral"), startTimer: false);
            RuntimeConfig initial = CreateConfig(DatabaseType.MSSQL);
            RuntimeConfig replacement = CreateConfig(DatabaseType.PostgreSQL);
            FileSystemRuntimeConfigLoader loader = new(new MockFileSystem()) { RuntimeConfig = initial };
            using RuntimeConfigProvider provider = new(loader) { ProductTelemetry = session };
            QueryExecutor<ControlledConnection> executor = new(new MsSqlDbExceptionParser(provider),
                NullLogger<IQueryExecutor>.Instance, provider, new HttpContextAccessor(), handler: null);
            session.AcceptConfiguration(initial);
            session.MarkHostReady();
            using EngineTelemetryRequestScope request = session.BeginRequest(
                EngineTelemetryApi.Rest, EngineTelemetryTransport.Http, EngineTelemetryRole.Anonymous);
            Assert.AreNotEqual(initial.DefaultDataSourceName, replacement.DefaultDataSourceName);
            string sourceName = sourceModel == "replacement" ? replacement.DefaultDataSourceName : initial.DefaultDataSourceName;
            if (sourceModel == "superseded")
            {
                // A command selected from an intermediate model can outlive both that model
                // and the request's initial one. Count the execution with unknown attribution.
                RuntimeConfig intermediate = CreateConfig(DatabaseType.DWSQL);
                loader.RuntimeConfig = intermediate;
                session.AcceptConfiguration(intermediate, "hot_reload");
                sourceName = intermediate.DefaultDataSourceName;
            }

            void Reload()
            {
                loader.RuntimeConfig = replacement;
                session.AcceptConfiguration(replacement, "hot_reload");
            }

            using DataTable table = new();
            Mock<DbCommand> command = new();
            int executions = 0;
            command.Protected().Setup<DbDataReader>("ExecuteDbDataReader", ItExpr.IsAny<CommandBehavior>())
                .Returns(() => { executions++; return table.CreateDataReader(); });
            command.Protected().Setup<Task<DbDataReader>>("ExecuteDbDataReaderAsync",
                ItExpr.IsAny<CommandBehavior>(), ItExpr.IsAny<CancellationToken>())
                .Returns(() => { executions++; return Task.FromResult<DbDataReader>(table.CreateDataReader()); });
            using ControlledConnection connection = new() { Command = command.Object, OnOpen = reloadBeforeExecution ? null : Reload };
            if (reloadBeforeExecution)
            {
                Reload();
            }

            int result = asynchronous
                ? await executor.ExecuteQueryAgainstDbAsync(connection, "SELECT 42", new Dictionary<string, DbConnectionParam>(),
                    (_, _) => Task.FromResult(42), httpContext: null, dataSourceName: sourceName)
                : executor.ExecuteQueryAgainstDb(connection, "SELECT 42", new Dictionary<string, DbConnectionParam>(),
                    (_, _) => 42, httpContext: null, dataSourceName: sourceName);
            Assert.AreEqual(42, result);
            Assert.AreEqual(1, executions, "The provider command, not just a telemetry helper, must have executed.");
            Assert.AreSame(replacement, provider.GetConfig());
            request.Complete(EngineTelemetryOutcome.Success, 200);
            await session.StopAsync();

            EngineTelemetryEvent[] attempts = exporter.Records.Where(record => record.Name == "dab.engine.usage_summary" &&
                record.Properties["family"] == "database_attempt").ToArray();
            Assert.AreEqual(1, attempts.Length, "Replacing source IDs must not discard an actual command execution.");
            Assert.AreEqual(1L, attempts[0].ConfigurationEpoch);
            Assert.AreEqual(expectedProvider, attempts[0].Properties["provider"]);
            Assert.AreEqual("1", attempts[0].Properties["count"]);
            Assert.AreEqual("1", attempts[0].Properties["success"]);
        }

        [DataTestMethod]
        [DataRow(false, "none")]
        [DataRow(true, "none")]
        [DataRow(false, "connection")]
        [DataRow(true, "connection")]
        [DataRow(false, "command")]
        [DataRow(true, "command")]
        public async Task TokenAwareExecutionKeepsCallerCancellationAndCountsOnlyStartedCommands(bool cancelRequest, string boundary)
        {
            CapturingExporter exporter = new();
            using EngineTelemetrySession session = EngineTelemetrySession.Create(() => exporter, enableSyntheticCollection: true,
                readEnvironmentVariable: _ => null, showNotice: () => { },
                resolveIdentity: _ => new(Guid.NewGuid(), "ephemeral"), startTimer: false);
            RuntimeConfig config = CreateConfig(DatabaseType.MSSQL);
            using FileSystemRuntimeConfigLoader loader = new(new MockFileSystem()) { RuntimeConfig = config };
            using RuntimeConfigProvider provider = new(loader) { ProductTelemetry = session };
            using CancellationTokenSource callerCancellation = new();
            using CancellationTokenSource requestCancellation = new();
            DefaultHttpContext http = new() { RequestAborted = requestCancellation.Token };
            void Cancel() => (cancelRequest ? requestCancellation : callerCancellation).Cancel();
            int executions = 0;
            CancellationToken executionToken = default;
            using DataTable table = new();
            Mock<DbCommand> command = new();
            command.Protected().Setup<Task<DbDataReader>>("ExecuteDbDataReaderAsync",
                ItExpr.IsAny<CommandBehavior>(), ItExpr.IsAny<CancellationToken>())
                .Returns((CommandBehavior _, CancellationToken token) =>
                {
                    executions++;
                    executionToken = token;
                    if (boundary == "command")
                    {
                        Cancel();
                    }

                    token.ThrowIfCancellationRequested();
                    return Task.FromResult<DbDataReader>(table.CreateDataReader());
                });
            using ControlledConnection connection = new()
            {
                Command = command.Object,
                OnOpenAsync = token =>
                {
                    Assert.IsTrue(token.CanBeCanceled);
                    if (boundary == "connection")
                    {
                        Cancel();
                    }
                }
            };
            Mock<QueryExecutor<ControlledConnection>> executor = new(new MsSqlDbExceptionParser(provider),
                NullLogger<IQueryExecutor>.Instance, provider, new HttpContextAccessor(), null) { CallBase = true };
            executor.Setup(value => value.CreateConnection(config.DefaultDataSourceName)).Returns(connection);
            session.AcceptConfiguration(config);
            session.MarkHostReady();
            using EngineTelemetryRequestScope request = session.BeginRequest(
                EngineTelemetryApi.Rest, EngineTelemetryTransport.Http, EngineTelemetryRole.Anonymous);
            Task<int> Execute() => executor.Object.ExecuteQueryAsync("SELECT 42", new Dictionary<string, DbConnectionParam>(),
                (_, _) => Task.FromResult(42), config.DefaultDataSourceName, callerCancellation.Token, http);
            if (boundary == "none")
            {
                Assert.AreEqual(42, await Execute());
                Assert.IsTrue(executionToken.CanBeCanceled);
                request.Complete(EngineTelemetryOutcome.Success, 200);
            }
            else
            {
                await Assert.ThrowsExceptionAsync<OperationCanceledException>(Execute);
                request.Complete(EngineTelemetryOutcome.Canceled);
            }

            await session.StopAsync();
            EngineTelemetryEvent[] attempts = exporter.Records.Where(record => record.Name == "dab.engine.usage_summary" &&
                record.Properties["family"] == "database_attempt").ToArray();
            Assert.AreEqual(boundary == "connection" ? 0 : 1, executions);
            Assert.AreEqual(executions, attempts.Length);
            if (executions == 1)
            {
                Assert.AreEqual("1", attempts[0].Properties[boundary == "command" ? "canceled" : "success"]);
                Assert.AreEqual("1", attempts[0].Properties["count"]);
            }
        }

        private static RuntimeConfig CreateConfig(DatabaseType databaseType) => new(
            Schema: null, DataSource: new(databaseType, string.Empty),
            Entities: new(new Dictionary<string, Entity>()));

        public sealed class ControlledConnection : DbConnection
        {
            private ConnectionState _state;

            internal DbCommand? Command { get; init; }
            internal Action? OnOpen { get; init; }
            internal Action<CancellationToken>? OnOpenAsync { get; init; }
            [AllowNull]
            public override string ConnectionString { get; set; } = string.Empty;
            public override string Database => "synthetic";
            public override string DataSource => "synthetic";
            public override string ServerVersion => "1.0";
            public override ConnectionState State => _state;

            public override void Open()
            {
                OnOpen?.Invoke();
                _state = ConnectionState.Open;
            }

            public override Task OpenAsync(CancellationToken cancellationToken)
            {
                OnOpenAsync?.Invoke(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                Open();
                return Task.CompletedTask;
            }

            public override void Close() => _state = ConnectionState.Closed;
            public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
            protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
            protected override DbCommand CreateDbCommand() => Command ?? throw new InvalidOperationException("A synthetic command is required.");
        }

        private sealed class CapturingExporter : IEngineTelemetryExporter
        {
            internal ConcurrentQueue<EngineTelemetryEvent> Records { get; } = new();

            public ValueTask<bool> ExportAsync(EngineTelemetryEvent telemetryEvent, CancellationToken cancellationToken)
            {
                Records.Enqueue(telemetryEvent);
                return ValueTask.FromResult(true);
            }

            public void Dispose() { }
        }
    }
}
