// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Config.Telemetry;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Telemetry;

[TestClass]
[TestCategory("EngineTelemetry")]
public class EngineTelemetryFailureStageTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    [DataTestMethod]
    [DataRow((int)TelemetryFailureStage.Unknown, "unknown")]
    [DataRow((int)TelemetryFailureStage.Initialization, "initialization")]
    [DataRow((int)TelemetryFailureStage.Configuration, "configuration")]
    [DataRow((int)TelemetryFailureStage.Parsing, "parsing")]
    [DataRow((int)TelemetryFailureStage.Validation, "validation")]
    [DataRow((int)TelemetryFailureStage.Metadata, "metadata")]
    [DataRow((int)TelemetryFailureStage.Serving, "serving")]
    [DataRow(int.MaxValue, "unknown")]
    [DataRow(-1, "unknown")]
    public async Task ConfigurationFailuresAlwaysHaveClosedStageAndCategory(int stage, string expected)
    {
        Capture exporter = new();
        using EngineTelemetrySession session = CreateSession(exporter);
        session.AcceptConfiguration(new(null, new(DatabaseType.MSSQL, string.Empty), new(new Dictionary<string, Entity>())));
        session.MarkHostReady();
        session.ConfigurationChangeFailed((TelemetryFailureStage)stage);
        Assert.IsTrue(session.IsReady);
        await session.StopAsync();
        EngineTelemetryEvent failure = exporter.Events.Single(record => record.Name == "dab.engine.configuration_change_failed");
        Assert.AreEqual(expected, failure.Properties["failure_stage"]);
        Assert.AreEqual("configuration", failure.Properties["failure_category"]);
        Assert.AreEqual(1L, failure.ConfigurationEpoch);
        Assert.IsFalse(failure.Properties.ContainsKey("snapshot_schema"));
    }

    [TestMethod]
    public void ChangeTokenRejectsNullCallbacksBeforeSignaling()
    {
        DabChangeToken token = new();
        ArgumentNullException error = Assert.ThrowsException<ArgumentNullException>(() => token.RegisterChangeCallback(null!, null));
        Assert.AreEqual("callback", error.ParamName);
        token.SignalChange();
    }

    [DataTestMethod]
    [DataRow("faulted_cancellation")]
    [DataRow("canceled_task")]
    [DataRow("synchronous_throw")]
    [DataRow("null_task")]
    public async Task FailureObservationPreservesOriginalHandlerJoinBehavior(string behavior)
    {
        (Type? Error, TaskStatus Status) baseline = await ObserveAsync(enabled: false);
        (Type? Error, TaskStatus Status) observed = await ObserveAsync(enabled: true);
        Assert.AreEqual(baseline, observed, "Telemetry must not change public initialization task status or selected exception.");

        async Task<(Type? Error, TaskStatus Status)> ObserveAsync(bool enabled)
        {
            Capture exporter = new();
            using EngineTelemetrySession session = EngineTelemetrySession.Create(() => exporter,
                enableSyntheticCollection: enabled, readEnvironmentVariable: _ => null,
                showNotice: () => { }, startTimer: false);
            using FileSystemRuntimeConfigLoader loader = new(new MockFileSystem());
            using RuntimeConfigProvider provider = new(loader) { ProductTelemetry = session };
            provider.RuntimeConfigLoadedHandlers.Add((_, _) => behavior switch
            {
                "faulted_cancellation" => Task.FromException<bool>(new OperationCanceledException()),
                "canceled_task" => Task.FromCanceled<bool>(new CancellationToken(canceled: true)),
                "synchronous_throw" => throw new InvalidOperationException("synthetic synchronous failure"),
                "null_task" => null!,
                _ => throw new InvalidOperationException()
            });
            int laterCalls = 0;
            provider.RuntimeConfigLoadedHandlers.Add((_, _) =>
            {
                laterCalls++;
                return Task.FromException<bool>(new InvalidOperationException("synthetic later failure"));
            });
            Task<bool> initialization = provider.Initialize("{\"data-source\":{\"database-type\":\"mssql\",\"connection-string\":\"Server=synthetic.invalid\"}}",
                schema: null, accessToken: null);
            Type? errorType = null;
            try
            {
                await initialization;
            }
            catch (Exception exception)
            {
                errorType = exception.GetType();
            }

            Assert.AreEqual(behavior == "synchronous_throw" ? 0 : 1, laterCalls);
            Assert.IsNull(TelemetryFailureContext.Current);
            await session.StopAsync();
            Assert.AreEqual(enabled ? 1 : 0, exporter.Events.Count(record => record.Name == "dab.engine.configuration_change_failed"));
            return (errorType, initialization.Status);
        }
    }

    [TestMethod]
    public async Task V2ParseFailureDoesNotAcceptAPreviouslyRejectedCandidate()
    {
        Capture exporter = new();
        using EngineTelemetrySession session = CreateSession(exporter);
        using FileSystemRuntimeConfigLoader loader = new(new MockFileSystem());
        using RuntimeConfigProvider provider = new(loader) { ProductTelemetry = session };
        bool acceptHandler = false;
        provider.RuntimeConfigLoadedHandlers.Add((_, _) => Task.FromResult(acceptHandler));
        const string json = "{\"data-source\":{\"database-type\":\"mssql\",\"connection-string\":\"Server=synthetic.invalid\"}}";
        Assert.IsFalse(await provider.Initialize(json, schema: null, accessToken: null));
        Assert.IsTrue(provider.TryGetLoadedConfig(out _), "Characterize the existing retained candidate, without changing serving behavior.");
        acceptHandler = true;
        Assert.IsTrue(await provider.Initialize("{", schema: null, accessToken: null),
            "Preserve the legacy V2 Boolean even though this attempt did not parse a new candidate.");
        session.MarkHostReady();
        Assert.IsFalse(session.IsReady);
        await session.StopAsync();
        EngineTelemetryEvent[] failures = exporter.Events.Where(record => record.Name == "dab.engine.configuration_change_failed").ToArray();
        CollectionAssert.AreEqual(new[] { "initialization", "parsing" }, failures.Select(record => record.Properties["failure_stage"]).ToArray());
        Assert.IsTrue(failures.All(record => record.ConfigurationEpoch == 0));
        Assert.IsFalse(exporter.Events.Any(record => record.Name is "dab.engine.ready" or "dab.engine.configuration_changed"));
    }

    [TestMethod]
    public void ChangeTokenUsesSignalingAttemptWithoutReplacingOtherCapturedContext()
    {
        AsyncLocal<string?> unrelated = new();
        TelemetryFailureContext registration = new();
        TelemetryFailureContext signal = new();
        DabChangeToken token = new();
        IDisposable callback;
        using (TelemetryFailureContext.Enter(registration))
        {
            unrelated.Value = "registration";
            callback = token.RegisterChangeCallback(_ =>
            {
                Assert.AreSame(signal, TelemetryFailureContext.Current);
                Assert.AreEqual("registration", unrelated.Value);
                TelemetryFailureContext.Current!.RecordFailure(TelemetryFailureStage.Metadata);
            }, null);
        }

        using (callback)
        using (TelemetryFailureContext.Enter(signal))
        {
            unrelated.Value = "signal";
            token.SignalChange();
            Assert.AreSame(signal, TelemetryFailureContext.Current);
            Assert.AreEqual("signal", unrelated.Value);
        }

        Assert.IsNull(TelemetryFailureContext.Current);
        Assert.AreEqual(TelemetryFailureStage.Unknown, registration.FailureStage);
        Assert.AreEqual(TelemetryFailureStage.Metadata, signal.FailureStage);
    }

    [TestMethod]
    public void UnobservedChangeDoesNotReuseRegistrationFailureContext()
    {
        TelemetryFailureContext registration = new();
        DabChangeToken token = new();
        IDisposable callback;
        using (TelemetryFailureContext.Enter(registration))
        {
            callback = token.RegisterChangeCallback(_ => Assert.IsNull(TelemetryFailureContext.Current), null);
        }

        using (callback)
        {
            token.SignalChange();
        }

        Assert.AreEqual(TelemetryFailureStage.Unknown, registration.FailureStage);
    }

    [TestMethod]
    public async Task RepeatedSignalsCannotReplaceTheFirstSignalsFailureContext()
    {
        TelemetryFailureContext first = new();
        TelemetryFailureContext second = new();
        DabChangeToken token = new();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable last = token.RegisterChangeCallback(_ =>
        {
            Assert.AreSame(first, TelemetryFailureContext.Current);
            TelemetryFailureContext.Current!.RecordFailure(TelemetryFailureStage.Validation);
        }, null);
        using IDisposable blocker = token.RegisterChangeCallback(_ =>
        {
            entered.TrySetResult();
            release.Task.WaitAsync(_timeout).GetAwaiter().GetResult();
        }, null);
        Task signaling = Task.Run(() =>
        {
            using IDisposable? scope = TelemetryFailureContext.Enter(first);
            token.SignalChange();
        });
        try
        {
            await entered.Task.WaitAsync(_timeout);
            using (TelemetryFailureContext.Enter(second))
            {
                token.SignalChange();
            }
        }
        finally
        {
            release.TrySetResult();
            await signaling.WaitAsync(_timeout);
        }

        Assert.AreEqual(TelemetryFailureStage.Validation, first.FailureStage);
        Assert.AreEqual(TelemetryFailureStage.Unknown, second.FailureStage);
    }

    [TestMethod]
    public async Task ConcurrentLateConfigurationAttemptsKeepTheirOwnFirstFailure()
    {
        Capture exporter = new();
        using EngineTelemetrySession session = CreateSession(exporter);
        using FileSystemRuntimeConfigLoader loader = new(new MockFileSystem());
        using RuntimeConfigProvider provider = new(loader) { ProductTelemetry = session };
        TaskCompletionSource bothEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource firstFailed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int entered = 0;
        provider.RuntimeConfigLoadedHandlers.Add(async (_, _) =>
        {
            TelemetryFailureContext context = TelemetryFailureContext.Current!;
            int ordinal = Interlocked.Increment(ref entered);
            if (ordinal == 2)
            {
                bothEntered.TrySetResult();
            }

            await bothEntered.Task.WaitAsync(_timeout);
            if (ordinal == 1)
            {
                context.RecordFailure(TelemetryFailureStage.Metadata);
                firstFailed.TrySetResult();
            }
            else
            {
                await firstFailed.Task.WaitAsync(_timeout);
                context.RecordFailure(TelemetryFailureStage.Validation);
            }

            // The generic handler's later fallback must not replace this specific failure.
            return false;
        });
        const string json = "{\"data-source\":{\"database-type\":\"mssql\",\"connection-string\":\"Server=synthetic.invalid;Integrated Security=true\"},\"entities\":{}}";
        Task<bool> first = provider.Initialize(json, schema: null, accessToken: null);
        Task<bool> second = provider.Initialize(json, schema: null, accessToken: null);
        CollectionAssert.AreEqual(new[] { false, false }, await Task.WhenAll(first, second).WaitAsync(_timeout));
        Assert.IsNull(TelemetryFailureContext.Current);
        await session.StopAsync();
        EngineTelemetryEvent[] failures = exporter.Events.Where(record => record.Name == "dab.engine.configuration_change_failed").ToArray();
        CollectionAssert.AreEquivalent(new[] { "metadata", "validation" }, failures.Select(record => record.Properties["failure_stage"]).ToArray());
        Assert.IsTrue(failures.All(record => record.ConfigurationEpoch == 0));
        Assert.IsFalse(exporter.Events.Any(record => record.Name == "dab.engine.ready"));
    }

    [TestMethod]
    public async Task ConcurrentCallbacksCannotReplaceTheFirstRecordedFailure()
    {
        TelemetryFailureContext context = new();
        context.RecordFailure(TelemetryFailureStage.Metadata);
        await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => context.RecordFailure(TelemetryFailureStage.Serving))));
        Assert.AreEqual(TelemetryFailureStage.Metadata, context.FailureStage);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RealValidatorClassifiesCollectedMetadataErrorsWithoutOverwritingPriorValidation(bool earlierValidationError)
    {
        MockFileSystem files = new();
        string schemaPath = files.Path.Combine(files.Directory.GetCurrentDirectory(), "synthetic-metadata.graphql");
        files.AddFile(schemaPath, new MockFileData("type Book @model(name: \"Book\") { id: ID! }"));
        Entity entity = new(new("books", EntitySourceType.Table, null, null),
            new("Book", "Books"), Fields: null, new(Enabled: false),
            [new("anonymous", [new(EntityActionOperation.Read, null, null)])], Mappings: null,
            Relationships: new()
            {
                ["related"] = new(Cardinality.One, "Book", [], [], null, [], [])
            });
        RuntimeConfig config = new(Schema: RuntimeConfig.DEFAULT_CONFIG_SCHEMA_LINK,
            DataSource: new(DatabaseType.CosmosDB_NoSQL, "unused", new()
            {
                ["database"] = "synthetic",
                ["container"] = "books",
                ["schema"] = schemaPath
            }),
            Entities: new(new Dictionary<string, Entity> { ["Book"] = entity }),
            Runtime: new(new(Enabled: false), new(Path: earlierValidationError ? "graphql" : "/graphql"), Mcp: null,
                Host: new(null, null, HostMode.Development)));
        using FileSystemRuntimeConfigLoader loader = new(files) { RuntimeConfig = config };
        using RuntimeConfigProvider provider = new(loader);
        RuntimeConfigValidator validator = new(provider, files, NullLogger<RuntimeConfigValidator>.Instance, isValidateOnly: true);
        TelemetryFailureContext failure = new();
        using (TelemetryFailureContext.Enter(failure))
        {
            Assert.IsFalse(await validator.TryValidateConfig("synthetic-config.json", NullLoggerFactory.Instance));
        }

        Assert.AreEqual(earlierValidationError ? 3 : 2, validator.ConfigValidationExceptions.Count,
            "The two missing inferred relationship objects must be collected, not thrown or skipped.");
        Assert.AreEqual(2, validator.ConfigValidationExceptions.Count(exception => exception.Message.StartsWith("Could not infer database object", StringComparison.Ordinal)));
        Assert.AreEqual(earlierValidationError ? TelemetryFailureStage.Validation : TelemetryFailureStage.Metadata, failure.FailureStage);
    }

    [DataTestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task NestedDisabledAttemptsDoNotContaminateAnEnabledParent(bool parentV2, bool childV2)
    {
        const string json = "{\"data-source\":{\"database-type\":\"mssql\",\"connection-string\":\"Server=synthetic.invalid\"}}";
        Capture exporter = new();
        using EngineTelemetrySession session = CreateSession(exporter);
        session.MarkHostReady();
        using FileSystemRuntimeConfigLoader loader = new(new MockFileSystem());
        using RuntimeConfigProvider parent = new(loader) { ProductTelemetry = session };
        parent.RuntimeConfigLoadedHandlers.Add(async (_, _) =>
        {
            TelemetryFailureContext? outer = TelemetryFailureContext.Current;
            Assert.IsNotNull(outer);
            using EngineTelemetrySession disabled = EngineTelemetrySession.Create();
            using FileSystemRuntimeConfigLoader childLoader = new(new MockFileSystem());
            using RuntimeConfigProvider child = new(childLoader) { ProductTelemetry = disabled };
            await Initialize(child, childV2, "{");
            Assert.AreSame(outer, TelemetryFailureContext.Current);
            Assert.IsFalse(child.TryGetLoadedConfig(out _));
            child.RuntimeConfigLoadedHandlers.Add(async (_, _) =>
            {
                Assert.IsNull(TelemetryFailureContext.Current);
                await Task.Yield();
                Assert.IsNull(TelemetryFailureContext.Current);
                return true;
            });
            Assert.IsTrue(await Initialize(child, childV2, json));
            Assert.AreSame(outer, TelemetryFailureContext.Current);
            return true;
        });

        Assert.IsTrue(await Initialize(parent, parentV2, json));
        Assert.IsTrue(session.IsReady, "A handled failure in a different disabled provider is not this attempt's failure.");
        Assert.IsNull(TelemetryFailureContext.Current);
        await session.StopAsync();
        Assert.AreEqual(1, exporter.Events.Count(record => record.Name == "dab.engine.ready"));
        Assert.IsFalse(exporter.Events.Any(record => record.Name == "dab.engine.configuration_change_failed"));

        static Task<bool> Initialize(RuntimeConfigProvider provider, bool versionTwo, string configuration) => versionTwo
            ? provider.Initialize(configuration, schema: null, accessToken: null)
            : provider.Initialize(configuration, graphQLSchema: null, connectionString: "Server=synthetic.invalid",
                accessToken: null, replacementSettings: null);
    }

    [TestMethod]
    public async Task DisabledInitializationHasNoFailureContext()
    {
        using EngineTelemetrySession session = EngineTelemetrySession.Create();
        using FileSystemRuntimeConfigLoader loader = new(new MockFileSystem());
        using RuntimeConfigProvider provider = new(loader) { ProductTelemetry = session };
        provider.RuntimeConfigLoadedHandlers.Add((_, _) =>
        {
            Assert.IsNull(TelemetryFailureContext.Current);
            return Task.FromResult(false);
        });
        Assert.IsFalse(await provider.Initialize("{\"data-source\":{\"database-type\":\"mssql\",\"connection-string\":\"Server=synthetic.invalid\"}}",
            schema: null, accessToken: null));
        Assert.IsNull(TelemetryFailureContext.Current);
    }

    private static EngineTelemetrySession CreateSession(Capture exporter) => EngineTelemetrySession.Create(
        () => exporter, enableSyntheticCollection: true, readEnvironmentVariable: _ => null,
        showNotice: () => { }, resolveIdentity: _ => new(Guid.NewGuid(), "ephemeral"), startTimer: false);

    private sealed class Capture : IEngineTelemetryExporter
    {
        internal ConcurrentQueue<EngineTelemetryEvent> Events { get; } = new();
        public ValueTask<bool> ExportAsync(EngineTelemetryEvent record, CancellationToken cancellationToken)
        {
            Events.Enqueue(record);
            return ValueTask.FromResult(true);
        }

        public void Dispose() { }
    }
}
