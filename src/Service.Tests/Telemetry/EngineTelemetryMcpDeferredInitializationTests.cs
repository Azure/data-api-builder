// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Core.Telemetry;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Azure.DataApiBuilder.Mcp.Core;
using Azure.DataApiBuilder.Mcp.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;
using Moq;
using static Azure.DataApiBuilder.Mcp.Model.McpEnums;

namespace Azure.DataApiBuilder.Service.Tests.Telemetry
{
    /// <summary>
    /// Real stdio dispatch, cached initialization task, registry publication, product request
    /// wrapper and engine sessions. Only metadata/refresh work, a closed-category data tool,
    /// configuration storage, stdin/stdout, clock, identity and exporter are in-memory fakes.
    /// No database, cloud destination, console redirection or environment mutation is used.
    /// These tests never manually accept configuration or mark the real server's session ready.
    /// </summary>
    [TestClass]
    [TestCategory("EngineTelemetry")]
    [TestCategory("CliTelemetry")]
    [DoNotParallelize]
    public class EngineTelemetryMcpDeferredInitializationTests
    {
        private const string SENTINEL = "MCP_DEFERRED_PRIVATE_18a74d";
        private const string ENTITY = SENTINEL + "_entity";
        private const string PROCESS_STARTED = "dab.engine.process_started";
        private const string READY = "dab.engine.ready";
        private const string FAILED = "dab.engine.startup_failed";
        private const string FIRST_SERVED = "dab.engine.first_request_served";
        private const string FIRST_SUCCESS = "dab.engine.first_successful_request";
        private const string SUMMARY = "dab.engine.usage_summary";
        private const string HEARTBEAT = "dab.engine.heartbeat";
        private const string STOPPED = "dab.engine.stopped";
        private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

        [DataTestMethod]
        [DataRow(false, false)]
        [DataRow(false, true)]
        [DataRow(true, false)]
        [DataRow(true, true)]
        public async Task ProtocolOnlyShutdownOrEofNeverInitializesToolsOrReportsReadiness(bool sendControls, bool shutdown)
        {
            string[]? disabledOutput = null;
            foreach (bool enabled in new[] { false, true })
            {
                await using Fixture fixture = new(enabled);
                fixture.InitializeMetadata = _ => Task.FromException(new InvalidOperationException(SENTINEL));
                fixture.BeforeRegistryPublication = _ => throw new InvalidOperationException(SENTINEL);
                fixture.Start();

                if (sendControls)
                {
                    fixture.Input.Send(Initialize(1));
                    AssertInitialize(await fixture.ReadResponseAsync(1));
                    fixture.Input.Send("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
                    fixture.Input.Send(Rpc(2, "ping"));
                    AssertOk(await fixture.ReadResponseAsync(2));
                    // A real logging/setLevel dispatch, with no stderr restoration or global
                    // logger changes: "none" exercises the controller's disable path.
                    fixture.Input.Send(Rpc(3, "logging/setLevel", new { level = "none" }));
                    Assert.AreEqual("{}", (await fixture.ReadResponseAsync(3)).GetProperty("result").GetRawText());
                }

                if (shutdown)
                {
                    fixture.Input.Send(Rpc(4, "shutdown"));
                    AssertOk(await fixture.ReadResponseAsync(4));
                }

                await fixture.FinishAsync();
                Assert.AreEqual((sendControls ? 3 : 0) + (shutdown ? 1 : 0), fixture.Output.Lines.Length,
                    "Notifications and EOF must not generate extra protocol frames.");
                fixture.VerifyInitialization(metadata: 0, registry: 0);
                fixture.LogController.Verify(controller => controller.UpdateFromMcp("none"), Times.Exactly(sendControls ? 1 : 0));
                Assert.IsFalse(fixture.Session.IsReady);
                Assert.IsFalse(fixture.Session.HasStartupFailed);
                Assert.AreEqual(0, fixture.IdentityResolutions);
                AssertToolExecutions(fixture, 0);
                EngineTelemetryEvent[] records = await fixture.DrainAsync();
                AssertNeverReady(records);
                CollectionAssert.AreEqual(enabled ? new[] { PROCESS_STARTED, STOPPED } : Array.Empty<string>(),
                    records.Select(record => record.Name).ToArray());
                AssertProtocolParity(ref disabledOutput, fixture);
            }
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task BlockedMetadataAllowsInitializeButDelaysFirstToolAndAcceptsOnlyFinalModel(bool firstIsCall)
        {
            string[]? disabledOutput = null;
            foreach (bool enabled in new[] { false, true })
            {
                RuntimeConfig initial = CreateConfiguration(includeEntity: false);
                RuntimeConfig final = CreateConfiguration();
                await using Fixture fixture = new(enabled, initialConfig: initial);
                InitializationGate metadataGate = new();
                fixture.InitializeMetadata = async token =>
                {
                    await metadataGate.WaitAsync(token);
                    // Model publication stands in for metadata expanding autoentities. The
                    // server, not this fixture, must accept this final model for telemetry.
                    fixture.Loader.RuntimeConfig = final;
                };
                fixture.Start();
                fixture.Input.Send(Initialize(1));
                fixture.Input.Send(ToolRequest(2, firstIsCall));

                AssertInitialize(await fixture.ReadResponseAsync(1));
                await metadataGate.Entered.Task.WaitAsync(_timeout);
                Assert.AreEqual(1, fixture.Output.Lines.Length,
                    "The initialize response must precede metadata completion and the first tool response.");
                Assert.IsFalse(fixture.Completion.IsCompleted);
                fixture.VerifyInitialization(metadata: 1, registry: 0);
                Assert.AreSame(initial, fixture.Loader.RuntimeConfig);
                await AssertStillInitializingAsync(fixture);
                AssertToolExecutions(fixture, 0);

                metadataGate.Release.TrySetResult();
                AssertToolResultOrList(await fixture.ReadResponseAsync(2), firstIsCall);
                Assert.AreSame(final, fixture.RegistryConfiguration);
                Assert.AreEqual(enabled, fixture.Session.IsReady);
                fixture.Input.Send(ToolRequest(3, isCall: false));
                AssertToolResultOrList(await fixture.ReadResponseAsync(3), isCall: false);
                fixture.Input.Send(ToolRequest(4, isCall: true));
                AssertToolResultOrList(await fixture.ReadResponseAsync(4), isCall: true);
                fixture.Input.Send(Rpc(5, "shutdown"));
                AssertOk(await fixture.ReadResponseAsync(5));
                await fixture.FinishAsync();

                fixture.VerifyInitialization(metadata: 1, registry: 1);
                Assert.AreEqual(0, fixture.Loader.LoadAttempts);
                Assert.AreEqual(enabled ? 1 : 0, fixture.IdentityResolutions);
                AssertToolExecutions(fixture, firstIsCall ? 2 : 1, final);
                EngineTelemetryEvent[] records = await fixture.DrainAsync();
                AssertSuccessfulLifecycle(records, enabled, requests: firstIsCall ? 2 : 1);
                if (enabled)
                {
                    Assert.AreEqual("1", records.Single(record => record.Name == READY).Properties["scale.entity_count"],
                        "Readiness must describe the final metadata-expanded model, not the handshake's empty model.");
                }

                AssertProtocolParity(ref disabledOutput, fixture);
            }
        }

        [TestMethod]
        public async Task BlockedRegistryPublicationKeepsReadinessAndToolResponsePending()
        {
            string[]? disabledOutput = null;
            foreach (bool enabled in new[] { false, true })
            {
                await using Fixture fixture = new(enabled);
                InitializationGate registryGate = new();
                fixture.BeforeRegistryPublication = token => registryGate.WaitAsync(token).GetAwaiter().GetResult();
                fixture.Start();
                fixture.Input.Send(Initialize(1));
                fixture.Input.Send(ToolRequest(2, isCall: true));

                AssertInitialize(await fixture.ReadResponseAsync(1));
                await registryGate.Entered.Task.WaitAsync(_timeout);
                fixture.VerifyInitialization(metadata: 1, registry: 1);
                Assert.AreEqual(0, fixture.Registry.GetAdvertisedTools().Count,
                    "The fake refresh service has not published its candidate yet.");
                Assert.AreEqual(1, fixture.Output.Lines.Length);
                Assert.IsFalse(fixture.Completion.IsCompleted);
                AssertToolExecutions(fixture, 0);
                await AssertStillInitializingAsync(fixture);

                registryGate.Release.TrySetResult();
                AssertToolResultOrList(await fixture.ReadResponseAsync(2), isCall: true);
                fixture.Input.Send(ToolRequest(3, isCall: false));
                AssertToolResultOrList(await fixture.ReadResponseAsync(3), isCall: false);
                await fixture.FinishAsync();

                fixture.VerifyInitialization(metadata: 1, registry: 1);
                Assert.AreEqual(1, fixture.Registry.GetAdvertisedTools().Count);
                Assert.AreEqual(enabled, fixture.Session.IsReady);
                AssertToolExecutions(fixture, 1, fixture.InitialConfig);
                AssertSuccessfulLifecycle(await fixture.DrainAsync(), enabled, requests: 1);
                AssertProtocolParity(ref disabledOutput, fixture);
            }
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task FirstListOrDirectValidCallInitializesOnceWithoutAHandshake(bool firstIsCall)
        {
            string[]? disabledOutput = null;
            foreach (bool enabled in new[] { false, true })
            {
                await using Fixture fixture = new(enabled);
                fixture.Start();
                fixture.Input.Send(ToolRequest(1, firstIsCall));
                AssertToolResultOrList(await fixture.ReadResponseAsync(1), firstIsCall);
                fixture.Input.Send(ToolRequest(2, isCall: true));
                AssertToolResultOrList(await fixture.ReadResponseAsync(2), isCall: true);
                fixture.Input.Send(ToolRequest(3, isCall: false));
                AssertToolResultOrList(await fixture.ReadResponseAsync(3), isCall: false);
                await fixture.FinishAsync();

                fixture.VerifyInitialization(metadata: 1, registry: 1);
                Assert.AreEqual(enabled, fixture.Session.IsReady);
                Assert.AreEqual(enabled ? 1 : 0, fixture.IdentityResolutions);
                AssertToolExecutions(fixture, firstIsCall ? 2 : 1, fixture.InitialConfig);
                AssertSuccessfulLifecycle(await fixture.DrainAsync(), enabled, requests: firstIsCall ? 2 : 1);
                AssertProtocolParity(ref disabledOutput, fixture);
            }
        }

        [DataTestMethod]
        [DataRow("metadata", false)]
        [DataRow("metadata", true)]
        [DataRow("serving", false)]
        [DataRow("serving", true)]
        public async Task InitializationFailureIsAProtocolErrorAndCachedAcrossSubsequentToolRequests(string stage, bool firstIsCall)
        {
            string[]? disabledOutput = null;
            foreach (bool enabled in new[] { false, true })
            {
                await using Fixture fixture = new(enabled);
                InvalidOperationException failure = new(SENTINEL + "_initialization_failure");
                if (stage == "metadata")
                {
                    fixture.InitializeMetadata = _ => Task.FromException(failure);
                }
                else
                {
                    fixture.BeforeRegistryPublication = _ => throw failure;
                }

                fixture.Start();
                fixture.Input.Send(Initialize(1));
                AssertInitialize(await fixture.ReadResponseAsync(1));
                fixture.Input.Send(ToolRequest(2, firstIsCall));
                AssertInternalError(await fixture.ReadResponseAsync(2));
                Assert.IsFalse(fixture.Session.IsReady);
                Assert.AreEqual(enabled, fixture.Session.HasStartupFailed,
                    "The failure must already be recorded when RunAsync translates it into a protocol error.");

                // Making both dependencies healthy must not turn the cached fault into an
                // implicit retry, even when the next request uses the other tool method.
                fixture.InitializeMetadata = _ => Task.CompletedTask;
                fixture.BeforeRegistryPublication = null;
                fixture.Input.Send(ToolRequest(3, !firstIsCall));
                AssertInternalError(await fixture.ReadResponseAsync(3));
                fixture.Input.Send(ToolRequest(4, firstIsCall));
                AssertInternalError(await fixture.ReadResponseAsync(4));
                fixture.Input.Send(Rpc(5, "ping"));
                AssertOk(await fixture.ReadResponseAsync(5));
                fixture.Input.Send(Rpc(6, "shutdown"));
                AssertOk(await fixture.ReadResponseAsync(6));
                await fixture.FinishAsync();

                Assert.IsTrue(fixture.Completion.IsCompletedSuccessfully,
                    "Initialization faults remain protocol errors, not host-loop exceptions.");
                fixture.VerifyInitialization(metadata: 1, registry: stage == "metadata" ? 0 : 1);
                Assert.AreEqual(0, fixture.Registry.GetAdvertisedTools().Count);
                Assert.AreEqual(0, fixture.IdentityResolutions);
                AssertToolExecutions(fixture, 0);
                EngineTelemetryEvent[] records = await fixture.DrainAsync();
                AssertNeverReady(records, enabled ? stage : null);
                CollectionAssert.AreEqual(enabled ? new[] { PROCESS_STARTED, FAILED, STOPPED } : Array.Empty<string>(),
                    records.Select(record => record.Name).ToArray(),
                    "Repeated requests against the cached fault must not emit duplicate startup failures.");
                AssertProtocolParity(ref disabledOutput, fixture);
            }
        }

        [DataTestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public async Task CancellationBeforeToolInitializationPreservesLoopContractWithoutReadiness(bool beforeRun)
        {
            string[]? disabledOutput = null;
            foreach (bool enabled in new[] { false, true })
            {
                await using Fixture fixture = new(enabled);
                if (beforeRun)
                {
                    fixture.Input.Send(Initialize(1));
                    fixture.Input.Send(ToolRequest(2, isCall: true));
                    fixture.Cancellation.Cancel();
                    fixture.Start();
                    await fixture.FinishAsync();
                    Assert.IsTrue(fixture.Completion.IsCompletedSuccessfully,
                        "Main's pre-canceled while loop returns normally without reading stdin.");
                    Assert.AreEqual(0, fixture.Output.Lines.Length);
                    Assert.AreEqual(0, fixture.Input.ReadCount);
                }
                else
                {
                    fixture.Start();
                    fixture.Input.Send(Initialize(1));
                    AssertInitialize(await fixture.ReadResponseAsync(1));
                    Assert.AreEqual(1, await fixture.Input.NextReadAsync());
                    Assert.AreEqual(2, await fixture.Input.NextReadAsync());
                    // Cancellation while awaiting stdin is outside the dispatch catch. It
                    // still propagates to the host helper, whose serving-stage test is separate.
                    fixture.Cancellation.Cancel();
                    try
                    {
                        await fixture.Completion.WaitAsync(_timeout);
                        Assert.Fail("Cancellation while reading stdin must propagate to the host helper.");
                    }
                    catch (OperationCanceledException exception)
                    {
                        Assert.AreEqual(fixture.Cancellation.Token, exception.CancellationToken);
                    }

                    Assert.AreEqual(1, fixture.Output.Lines.Length,
                        "An idle-read cancellation must not invent a response or deferred initialization attempt.");
                }

                fixture.VerifyInitialization(metadata: 0, registry: 0);
                Assert.IsFalse(fixture.Session.IsReady);
                Assert.IsFalse(fixture.Session.HasStartupFailed,
                    "The bare server must not misclassify a never-started metadata operation; the helper owns loop failures.");
                Assert.AreEqual(0, fixture.IdentityResolutions);
                AssertToolExecutions(fixture, 0);
                EngineTelemetryEvent[] records = await fixture.DrainAsync();
                AssertNeverReady(records);
                CollectionAssert.AreEqual(enabled ? new[] { PROCESS_STARTED, STOPPED } : Array.Empty<string>(),
                    records.Select(record => record.Name).ToArray());
                AssertProtocolParity(ref disabledOutput, fixture);
            }
        }

        [DataTestMethod]
        [DataRow("metadata")]
        [DataRow("serving")]
        public async Task CancellationDuringInitializationReportsItsBoundaryWithoutReadiness(string stage)
        {
            string[]? disabledOutput = null;
            foreach (bool enabled in new[] { false, true })
            {
                await using Fixture fixture = new(enabled);
                InitializationGate gate = new();
                if (stage == "metadata")
                {
                    fixture.InitializeMetadata = gate.WaitAsync;
                }
                else
                {
                    fixture.BeforeRegistryPublication = token => gate.WaitAsync(token).GetAwaiter().GetResult();
                }

                fixture.Start();
                fixture.Input.Send(Initialize(1));
                fixture.Input.Send(ToolRequest(2, isCall: true));
                AssertInitialize(await fixture.ReadResponseAsync(1));
                await gate.Entered.Task.WaitAsync(_timeout);
                await AssertStillInitializingAsync(fixture);
                Assert.AreEqual(1, fixture.Output.Lines.Length);

                fixture.Cancellation.Cancel();
                AssertInternalError(await fixture.ReadResponseAsync(2));
                await fixture.FinishAsync();
                Assert.IsTrue(fixture.Completion.IsCompletedSuccessfully,
                    "Dispatch retains main's protocol-error translation for cancellation inside deferred initialization.");
                fixture.VerifyInitialization(metadata: 1, registry: stage == "metadata" ? 0 : 1);
                Assert.IsTrue(fixture.MetadataTokens.Single().IsCancellationRequested);
                Assert.IsFalse(fixture.Session.IsReady);
                Assert.AreEqual(enabled, fixture.Session.HasStartupFailed);
                Assert.AreEqual(0, fixture.Registry.GetAdvertisedTools().Count);
                Assert.AreEqual(0, fixture.IdentityResolutions);
                AssertToolExecutions(fixture, 0);
                EngineTelemetryEvent[] records = await fixture.DrainAsync();
                AssertNeverReady(records, enabled ? stage : null);
                CollectionAssert.AreEqual(enabled ? new[] { PROCESS_STARTED, FAILED, STOPPED } : Array.Empty<string>(),
                    records.Where(record => record.Name != HEARTBEAT).Select(record => record.Name).ToArray());
                AssertProtocolParity(ref disabledOutput, fixture);
            }
        }

        [DataTestMethod]
        [DataRow("loaded", true)]
        [DataRow("loaded", false)]
        [DataRow("unloaded", true)]
        [DataRow("unloaded", false)]
        [DataRow("absent", true)]
        [DataRow("absent", false)]
        public async Task ReadinessRequiresAnAlreadyLoadedConfigurationButRegistryRefreshRemainsOptional(string configurationState, bool includeRegistry)
        {
            string[]? disabledOutput = null;
            foreach (bool enabled in new[] { false, true })
            {
                await using Fixture fixture = new(enabled, configurationState, includeRegistry);
                fixture.Start();
                // Skip initialize: that handler legitimately calls GetConfig for its description.
                // These tool requests isolate the non-loading TryGetLoadedConfig readiness gate.
                fixture.Input.Send(ToolRequest(1, isCall: false));
                AssertToolList(await fixture.ReadResponseAsync(1), hasTool: includeRegistry);
                fixture.Input.Send(ToolRequest(2, isCall: false));
                AssertToolList(await fixture.ReadResponseAsync(2), hasTool: includeRegistry);
                await fixture.FinishAsync();

                fixture.VerifyInitialization(metadata: 1, registry: includeRegistry ? 1 : 0);
                bool expectedReady = enabled && configurationState == "loaded";
                Assert.AreEqual(expectedReady, fixture.Session.IsReady);
                Assert.IsFalse(fixture.Session.HasStartupFailed);
                Assert.AreEqual(0, fixture.Loader.LoadAttempts,
                    "Telemetry must not load a merely available config to manufacture readiness.");
                Assert.AreEqual(expectedReady ? 1 : 0, fixture.IdentityResolutions);
                AssertToolExecutions(fixture, 0);
                EngineTelemetryEvent[] records = await fixture.DrainAsync();
                if (expectedReady)
                {
                    AssertSuccessfulLifecycle(records, enabled, requests: 0);
                    CollectionAssert.AreEqual(new[] { PROCESS_STARTED, READY, STOPPED },
                        records.Select(record => record.Name).ToArray());
                }
                else
                {
                    AssertNeverReady(records);
                    CollectionAssert.AreEqual(enabled ? new[] { PROCESS_STARTED, STOPPED } : Array.Empty<string>(),
                        records.Select(record => record.Name).ToArray());
                }

                AssertProtocolParity(ref disabledOutput, fixture);
            }
        }

        [TestMethod]
        public async Task ConcurrentServersOwnDistinctInitializationTasksReadinessAndRequestTelemetry()
        {
            await using Fixture first = new(enabled: true);
            await using Fixture second = new(enabled: true);
            InitializationGate firstGate = new();
            InitializationGate secondGate = new();
            first.InitializeMetadata = firstGate.WaitAsync;
            second.InitializeMetadata = secondGate.WaitAsync;
            first.Start();
            second.Start();
            first.Input.Send(Initialize(1));
            first.Input.Send(ToolRequest(2, isCall: true));
            second.Input.Send(Initialize(1));
            second.Input.Send(ToolRequest(2, isCall: true));
            AssertInitialize(await first.ReadResponseAsync(1));
            AssertInitialize(await second.ReadResponseAsync(1));
            await Task.WhenAll(firstGate.Entered.Task, secondGate.Entered.Task).WaitAsync(_timeout);
            first.VerifyInitialization(metadata: 1, registry: 0);
            second.VerifyInitialization(metadata: 1, registry: 0);
            Assert.AreNotEqual(first.MetadataTokens.Single(), second.MetadataTokens.Single());
            await AssertStillInitializingAsync(first);
            await AssertStillInitializingAsync(second);

            secondGate.Release.TrySetResult();
            AssertToolResultOrList(await second.ReadResponseAsync(2), isCall: true);
            second.Input.Send(Rpc(3, "ping"));
            AssertOk(await second.ReadResponseAsync(3));
            Assert.IsTrue(second.Session.IsReady);
            Assert.IsFalse(first.Session.IsReady);
            Assert.IsFalse(first.Completion.IsCompleted);
            Assert.AreEqual(1, first.Output.Lines.Length);
            await AssertStillInitializingAsync(first);

            // Stopping the already-serving owner must not disable, complete or initialize the
            // other owner that is still waiting on its own metadata task.
            await second.FinishAsync();
            EngineTelemetryEvent[] secondRecords = await second.DrainAsync();
            Assert.IsTrue(first.Session.IsEnabled);
            Assert.IsFalse(first.Session.IsReady);
            Assert.AreEqual(0, first.IdentityResolutions);
            firstGate.Release.TrySetResult();
            AssertToolResultOrList(await first.ReadResponseAsync(2), isCall: true);
            await first.FinishAsync();
            first.VerifyInitialization(metadata: 1, registry: 1);
            second.VerifyInitialization(metadata: 1, registry: 1);
            AssertToolExecutions(first, 1, first.InitialConfig);
            AssertToolExecutions(second, 1, second.InitialConfig);
            Assert.AreNotSame(first.InitialConfig, second.InitialConfig);
            Assert.AreEqual(1, first.IdentityResolutions);
            Assert.AreEqual(1, second.IdentityResolutions);
            EngineTelemetryEvent[] firstRecords = await first.DrainAsync();
            AssertSuccessfulLifecycle(firstRecords, enabled: true, requests: 1);
            AssertSuccessfulLifecycle(secondRecords, enabled: true, requests: 1);
            Assert.AreNotEqual(firstRecords[0].SessionId, secondRecords[0].SessionId);
            Assert.AreNotEqual(first.ApiId, second.ApiId);
            Assert.AreEqual(first.ApiId.ToString("D"), firstRecords.Single(record => record.Name == READY).Properties["dab_api_id"]);
            Assert.AreEqual(second.ApiId.ToString("D"), secondRecords.Single(record => record.Name == READY).Properties["dab_api_id"]);
            Assert.AreEqual(firstRecords.Length + secondRecords.Length,
                firstRecords.Concat(secondRecords).Select(record => record.EventId).Distinct().Count());
        }

        private static async Task AssertStillInitializingAsync(Fixture fixture)
        {
            Assert.IsFalse(fixture.Session.IsReady);
            Assert.IsFalse(fixture.Session.HasStartupFailed);
            Assert.AreEqual(0, fixture.IdentityResolutions,
                "Neither accepting configuration nor resolving its identity belongs before deferred initialization completes.");
            // The heartbeat follows all earlier events through the single exporter worker.
            // An empty asynchronous exporter queue alone is not evidence of no ready/failure.
            EngineTelemetryEvent[] records = await fixture.CheckpointAsync();
            AssertNeverReady(records);
            if (fixture.Enabled)
            {
                Assert.AreEqual("initializing", records.Last(record => record.Name == HEARTBEAT).Properties["run_state"]);
            }
        }

        private static void AssertNeverReady(EngineTelemetryEvent[] records, string? failureStage = null)
        {
            Assert.AreEqual(0, records.Count(record => record.Name == READY));
            Assert.AreEqual(0, records.Count(record => record.Name is FIRST_SERVED or FIRST_SUCCESS or SUMMARY));
            Assert.AreEqual(failureStage is null ? 0 : 1, records.Count(record => record.Name == FAILED));
            if (failureStage is not null)
            {
                EngineTelemetryEvent failure = records.Single(record => record.Name == FAILED);
                Assert.AreEqual(failureStage, failure.Properties["failure_stage"]);
                Assert.AreEqual("initialization", failure.Properties["failure_category"]);
                Assert.AreEqual(0L, failure.ConfigurationEpoch);
                Assert.IsFalse(failure.Properties.ContainsKey("dab_api_id"));
                Assert.IsTrue(failure.Sequence < records.Single(record => record.Name == STOPPED).Sequence);
            }
        }

        private static void AssertSuccessfulLifecycle(EngineTelemetryEvent[] records, bool enabled, int requests)
        {
            if (!enabled)
            {
                Assert.AreEqual(0, records.Length);
                return;
            }

            EngineTelemetryEvent ready = records.Single(record => record.Name == READY);
            Assert.AreEqual(1L, ready.ConfigurationEpoch);
            Assert.AreEqual("startup", ready.Properties["configuration_delivery"]);
            Assert.AreEqual(0, records.Count(record => record.Name == FAILED || record.Name == "dab.engine.configuration_changed"),
                "A cached successful initialization must not accept configuration again or create another epoch.");
            Assert.AreEqual(requests == 0 ? 0 : 1, records.Count(record => record.Name == FIRST_SERVED));
            Assert.AreEqual(requests == 0 ? 0 : 1, records.Count(record => record.Name == FIRST_SUCCESS));
            if (requests > 0)
            {
                EngineTelemetryEvent served = records.Single(record => record.Name == FIRST_SERVED);
                EngineTelemetryEvent success = records.Single(record => record.Name == FIRST_SUCCESS);
                Assert.IsTrue(ready.Sequence < served.Sequence && served.Sequence < success.Sequence);
                Assert.IsTrue(new[] { served, success }.All(record => record.ConfigurationEpoch == 1
                    && record.Properties["api"] == "mcp" && record.Properties["transport"] == "stdio"
                    && record.Properties["outcome"] == "success"));
            }

            foreach (string family in new[] { "request", "operation" })
            {
                EngineTelemetryEvent[] summaries = records.Where(record => record.Name == SUMMARY && record.Properties["family"] == family).ToArray();
                Assert.AreEqual((long)requests, summaries.Sum(record => Counter(record, "count")), family);
                Assert.AreEqual((long)requests, summaries.Sum(record => Counter(record, "success")), family);
                foreach (string outcome in new[] { "failure", "partial_failure", "unknown", "canceled" })
                {
                    Assert.AreEqual(0L, summaries.Sum(record => Counter(record, outcome)), family + ":" + outcome);
                }

                Assert.IsTrue(summaries.All(record => record.ConfigurationEpoch == 1 && record.Properties["api"] == "mcp"));
                if (family == "request")
                {
                    Assert.AreEqual((long)requests, summaries.Sum(record => Counter(record, "timed_count")));
                    Assert.IsTrue(summaries.All(record => record.Properties["transport"] == "stdio"
                        && record.Properties["role_class"] == "anonymous"));
                }
                else
                {
                    Assert.IsTrue(summaries.All(record => record.Properties["operation"] == "read"
                        && record.Properties["provider"] == "ms_sql" && record.Properties["object_type"] == "table"));
                }
            }

            Assert.IsTrue(records.Where(record => record.Name == SUMMARY)
                .All(record => record.Properties["family"] is "request" or "operation"),
                "A fake tool performs no SQL attempts, and stdio must not fabricate HTTP outcomes.");
            Assert.AreEqual(STOPPED, records.Last().Name);
        }

        private static long Counter(EngineTelemetryEvent record, string property)
            => long.Parse(record.Properties[property], CultureInfo.InvariantCulture);

        private static void AssertToolExecutions(Fixture fixture, int count, RuntimeConfig? acceptedConfig = null)
        {
            fixture.Tool.Verify(tool => tool.ExecuteAsync(It.IsAny<JsonDocument?>(), It.IsAny<IServiceProvider>(), It.IsAny<CancellationToken>()),
                Times.Exactly(count));
            ToolObservation[] observations = fixture.ToolObservations.ToArray();
            Assert.AreEqual(count, observations.Length);
            foreach (ToolObservation observation in observations)
            {
                Assert.AreSame(fixture.Session, observation.Owner);
                Assert.AreEqual(fixture.Cancellation.Token, observation.Token);
                Assert.AreEqual(fixture.Enabled, observation.Ready,
                    "The first real closed-category tool execution must see readiness, not create it itself.");
                if (fixture.Enabled)
                {
                    Assert.IsNotNull(observation.Request);
                    Assert.IsTrue(observation.Request.IsEligible);
                    Assert.IsTrue(observation.Request.IsCompleted);
                    Assert.AreEqual(EngineTelemetryApi.Mcp, observation.Request.Api);
                    Assert.AreEqual(EngineTelemetryTransport.Stdio, observation.Request.Transport);
                    Assert.AreEqual(EngineTelemetryRole.Anonymous, observation.Request.Role);
                    Assert.AreEqual(EngineTelemetryOutcome.Success, observation.Request.Outcome);
                    Assert.AreEqual(1L, observation.Request.Configuration.Epoch);
                    Assert.AreSame(acceptedConfig, observation.Request.Config);
                }
                else
                {
                    Assert.IsNull(observation.Request, "Disabled telemetry must not create a product request scope.");
                }
            }

            Assert.IsNull(fixture.Session.CurrentRequest);
            Assert.IsNull(fixture.Services.GetRequiredService<IHttpContextAccessor>().HttpContext);
        }

        private static void AssertProtocolParity(ref string[]? disabledOutput, Fixture fixture)
        {
            if (!fixture.Enabled)
            {
                disabledOutput = fixture.Output.Lines;
            }
            else
            {
                Assert.IsNotNull(disabledOutput);
                CollectionAssert.AreEqual(disabledOutput, fixture.Output.Lines,
                    "Enabling product telemetry must preserve every protocol response and its ordering exactly.");
            }
        }

        private static string Rpc(int id, string method, object? parameters = null)
            => JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters });

        private static string Initialize(int id) => Rpc(id, "initialize", new
        {
            protocolVersion = "2025-03-26",
            capabilities = new { },
            clientInfo = new { name = SENTINEL + "_client", version = "1.0" }
        });

        private static string ToolRequest(int id, bool isCall)
            => isCall ? Rpc(id, "tools/call", new { name = "read_records", arguments = new { entity = ENTITY } }) : Rpc(id, "tools/list");

        private static void AssertInitialize(JsonElement response)
        {
            Assert.IsFalse(response.TryGetProperty("error", out _));
            Assert.AreEqual("2025-03-26", response.GetProperty("result").GetProperty("protocolVersion").GetString());
            Assert.IsTrue(response.GetProperty("result").GetProperty("capabilities").TryGetProperty("tools", out _));
        }

        private static void AssertOk(JsonElement response)
        {
            Assert.IsFalse(response.TryGetProperty("error", out _));
            Assert.IsTrue(response.GetProperty("result").GetProperty("ok").GetBoolean());
        }

        private static void AssertToolResultOrList(JsonElement response, bool isCall)
        {
            if (!isCall)
            {
                AssertToolList(response, hasTool: true);
                return;
            }

            Assert.IsFalse(response.TryGetProperty("error", out _));
            JsonElement result = response.GetProperty("result");
            Assert.IsFalse(result.TryGetProperty("isError", out JsonElement isError) && isError.GetBoolean());
            JsonElement content = result.GetProperty("content");
            Assert.AreEqual(1, content.GetArrayLength());
            Assert.AreEqual("text", content[0].GetProperty("type").GetString());
            Assert.AreEqual(SENTINEL + "_result", content[0].GetProperty("text").GetString());
        }

        private static void AssertToolList(JsonElement response, bool hasTool)
        {
            Assert.IsFalse(response.TryGetProperty("error", out _));
            JsonElement tools = response.GetProperty("result").GetProperty("tools");
            Assert.AreEqual(hasTool ? 1 : 0, tools.GetArrayLength());
            if (hasTool)
            {
                Assert.AreEqual("read_records", tools[0].GetProperty("name").GetString());
            }
        }

        private static void AssertInternalError(JsonElement response)
        {
            Assert.IsFalse(response.TryGetProperty("result", out _));
            Assert.AreEqual(-32603, response.GetProperty("error").GetProperty("code").GetInt32());
            Assert.AreEqual("Internal error", response.GetProperty("error").GetProperty("message").GetString());
            Assert.IsFalse(response.GetRawText().Contains(SENTINEL, StringComparison.Ordinal),
                "Deferred initialization exceptions must not leak private details into protocol errors.");
        }

        private static RuntimeConfig CreateConfiguration(bool includeEntity = true)
        {
            string entities = includeEntity ? $$"""
                "{{ENTITY}}": {
                  "source": { "type": "table", "object": "{{SENTINEL}}_object" },
                  "rest": false,
                  "graphql": false,
                  "mcp": true,
                  "permissions": [{ "role": "anonymous", "actions": ["read"] }]
                }
                """ : string.Empty;
            string json = $$"""
                {
                  "data-source": { "database-type": "mssql", "connection-string": "" },
                  "runtime": {
                    "rest": { "enabled": false },
                    "graphql": { "enabled": false },
                    "mcp": { "enabled": true, "description": "{{SENTINEL}}_description" },
                    "host": { "mode": "development", "authentication": { "provider": "Simulator" } }
                  },
                  "entities": { {{entities}} }
                }
                """;
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(json, out RuntimeConfig? config, replacementSettings: null));
            Assert.IsNotNull(config);
            return config;
        }

        private sealed class Fixture : IAsyncDisposable
        {
            private readonly CapturingExporter _exporter = new();
            private readonly ManualClock _clock = new();
            private readonly IConfigurationRoot _configuration;
            private readonly RuntimeConfigProvider _configProvider;
            private readonly McpStdoutWriter _stdout;
            private readonly McpStdioServer _server;
            private Task? _running;
            private int _identityResolutions;
            private int _exporterCreations;

            internal bool Enabled { get; }
            internal Guid ApiId { get; } = Guid.NewGuid();
            internal int IdentityResolutions => Volatile.Read(ref _identityResolutions);
            internal RuntimeConfig InitialConfig { get; }
            internal RuntimeConfig? RegistryConfiguration { get; private set; }
            internal InMemoryConfigLoader Loader { get; }
            internal EngineTelemetrySession Session { get; }
            internal ServiceProvider Services { get; }
            internal ScriptedInput Input { get; } = new();
            internal ScriptedOutput Output { get; } = new();
            internal CancellationTokenSource Cancellation { get; } = new();
            internal McpToolRegistry Registry { get; } = new();
            internal Mock<IMetadataProviderFactory> Metadata { get; } = new(MockBehavior.Strict);
            internal Mock<IMcpToolRegistryRefreshService> Refresh { get; } = new(MockBehavior.Strict);
            internal Mock<IMcpTool> Tool { get; } = new(MockBehavior.Strict);
            internal Mock<ILogLevelController> LogController { get; } = new(MockBehavior.Strict);
            internal ConcurrentQueue<string> InitializationOrder { get; } = new();
            internal ConcurrentQueue<CancellationToken> MetadataTokens { get; } = new();
            internal ConcurrentQueue<CancellationToken> RegistryTokens { get; } = new();
            internal ConcurrentQueue<ToolObservation> ToolObservations { get; } = new();
            internal Func<CancellationToken, Task> InitializeMetadata { get; set; } = _ => Task.CompletedTask;
            internal Action<CancellationToken>? BeforeRegistryPublication { get; set; }
            internal Task Completion => _running ?? throw new InvalidOperationException("The fixture server has not started.");

            internal Fixture(bool enabled, string configurationState = "loaded", bool includeRegistry = true, RuntimeConfig? initialConfig = null)
            {
                Enabled = enabled;
                InitialConfig = initialConfig ?? CreateConfiguration();
                Loader = new(InitialConfig)
                {
                    RuntimeConfig = configurationState == "loaded" ? InitialConfig : null
                };
                Session = EngineTelemetrySession.Create(() =>
                {
                    Interlocked.Increment(ref _exporterCreations);
                    return _exporter;
                }, enableSyntheticCollection: enabled, executionMode: "mcp_stdio", clock: _clock,
                    readEnvironmentVariable: _ => null, showNotice: () => { }, startTimer: false,
                    resolveIdentity: _ =>
                    {
                        Interlocked.Increment(ref _identityResolutions);
                        return new(ApiId, "ephemeral");
                    });
                _configProvider = new(Loader) { ProductTelemetry = Session };
                _configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MCP:StdioMode"] = "true",
                    ["MCP:Role"] = "anonymous"
                }).Build();
                _stdout = new(Output);
                Metadata.Setup(factory => factory.InitializeAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken token) =>
                {
                    InitializationOrder.Enqueue("metadata");
                    MetadataTokens.Enqueue(token);
                    return InitializeMetadata(token);
                });
                Refresh.Setup(refresh => refresh.EnsureInitialized(It.IsAny<CancellationToken>())).Callback((CancellationToken token) =>
                {
                    InitializationOrder.Enqueue("registry");
                    RegistryTokens.Enqueue(token);
                    BeforeRegistryPublication?.Invoke(token);
                    token.ThrowIfCancellationRequested();
                    // Optional/missing-config cases deliberately keep this fake refresh
                    // independent of GetConfig so only the server's readiness gate is tested.
                    RegistryConfiguration = Loader.RuntimeConfig ?? InitialConfig;
                    Registry.ReplaceAll([Tool.Object], RegistryConfiguration);
                });
                Tool.SetupGet(tool => tool.ToolType).Returns(ToolType.BuiltIn);
                Tool.Setup(tool => tool.GetToolMetadata()).Returns(new Tool
                {
                    Name = "read_records",
                    Description = SENTINEL + "_tool_description",
                    InputSchema = JsonSerializer.SerializeToElement(new { type = "object" })
                });
                Tool.Setup(tool => tool.IsEnabled(It.IsAny<RuntimeConfig>())).Returns(true);
                Tool.Setup(tool => tool.ExecuteAsync(It.IsAny<JsonDocument?>(), It.IsAny<IServiceProvider>(), It.IsAny<CancellationToken>()))
                    .Returns((JsonDocument? _, IServiceProvider services, CancellationToken token) =>
                    {
                        EngineTelemetrySession owner = services.GetRequiredService<EngineTelemetrySession>();
                        ToolObservations.Enqueue(new(owner, owner.IsReady, owner.CurrentRequest, token));
                        return Task.FromResult(new CallToolResult
                        {
                            IsError = false,
                            Content = [new TextContentBlock { Text = SENTINEL + "_result" }]
                        });
                    });
                LogController.Setup(controller => controller.UpdateFromMcp("none")).Returns(false);
                ServiceCollection services = new();
                services.AddSingleton(Session);
                services.AddSingleton(_stdout);
                services.AddSingleton<IConfiguration>(_configuration);
                services.AddSingleton(Metadata.Object);
                services.AddSingleton(LogController.Object);
                services.AddHttpContextAccessor();
                if (configurationState != "absent")
                {
                    services.AddSingleton(_configProvider);
                }

                if (includeRegistry)
                {
                    services.AddSingleton(Refresh.Object);
                }

                Services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
                _server = new(Registry, Services, Input);
                Assert.AreEqual(enabled, Session.IsEnabled);
                Assert.IsFalse(Session.IsReady);
            }

            internal void Start()
            {
                Assert.IsNull(_running);
                // Registry refresh is synchronous. Run the real loop on a worker so the test
                // can inspect a blocked refresh without blocking its own continuation.
                _running = Task.Run(() => _server.RunAsync(Cancellation.Token));
            }

            internal async Task<JsonElement> ReadResponseAsync(int id)
            {
                JsonElement response = await Output.NextAsync();
                Assert.AreEqual("2.0", response.GetProperty("jsonrpc").GetString());
                Assert.AreEqual(id, response.GetProperty("id").GetInt32());
                return response;
            }

            internal async Task FinishAsync()
            {
                Input.Complete();
                await Completion.WaitAsync(_timeout);
            }

            internal void VerifyInitialization(int metadata, int registry)
            {
                Metadata.Verify(factory => factory.InitializeAsync(It.IsAny<CancellationToken>()), Times.Exactly(metadata));
                Metadata.VerifyNoOtherCalls();
                Refresh.Verify(refresh => refresh.EnsureInitialized(It.IsAny<CancellationToken>()), Times.Exactly(registry));
                Refresh.VerifyNoOtherCalls();
                CollectionAssert.AreEqual((metadata == 0 ? Array.Empty<string>() : new[] { "metadata" })
                    .Concat(registry == 0 ? Array.Empty<string>() : new[] { "registry" }).ToArray(), InitializationOrder.ToArray());
                Assert.AreEqual(metadata, MetadataTokens.Count);
                Assert.AreEqual(registry, RegistryTokens.Count);
                foreach (CancellationToken token in MetadataTokens.Concat(RegistryTokens))
                {
                    Assert.IsTrue(token.CanBeCanceled);
                    Assert.AreEqual(Cancellation.Token, token,
                        "Metadata and registry must receive the same token as the real stdio loop.");
                }
            }

            internal async Task<EngineTelemetryEvent[]> CheckpointAsync()
            {
                Assert.AreEqual(Enabled, Session.IsEnabled, "A telemetry error must not silently turn these assertions into an opt-out test.");
                if (Enabled)
                {
                    _clock.Advance(TimeSpan.FromMinutes(10));
                    Session.Tick();
                    await _exporter.Heartbeats.Reader.ReadAsync().AsTask().WaitAsync(_timeout);
                }

                return _exporter.Records.ToArray();
            }

            internal async Task<EngineTelemetryEvent[]> DrainAsync()
            {
                Assert.AreEqual(Enabled, Session.IsEnabled);
                await Session.StopAsync().WaitAsync(_timeout);
                if (Enabled)
                {
                    await _exporter.Stopped.Task.WaitAsync(_timeout);
                }

                EngineTelemetryEvent[] records = _exporter.Records.ToArray();
                Assert.AreEqual(Enabled ? 1 : 0, Volatile.Read(ref _exporterCreations));
                if (Enabled)
                {
                    Assert.AreEqual(PROCESS_STARTED, records.First().Name);
                    Assert.AreEqual(STOPPED, records.Last().Name);
                    Assert.AreEqual(1, records.Count(record => record.Name == PROCESS_STARTED));
                    Assert.AreEqual(1, records.Count(record => record.Name == STOPPED));
                    Assert.AreNotEqual(Guid.Empty, records[0].SessionId);
                    Assert.AreEqual(1, records.Select(record => record.SessionId).Distinct().Count());
                    Assert.AreEqual(records.Length, records.Select(record => record.EventId).Distinct().Count());
                    CollectionAssert.AreEqual(Enumerable.Range(1, records.Length).Select(index => (long)index).ToArray(),
                        records.Select(record => record.Sequence).ToArray());
                    Assert.IsTrue(records.All(record => record.IsSynthetic && record.Properties["execution_mode"] == "mcp_stdio"
                        && record.Properties["sender_dropped_events"] == "0"));
                    Assert.IsFalse(records[0].Properties.ContainsKey("dab_api_id"));
                }
                else
                {
                    Assert.AreEqual(0, records.Length);
                    Assert.AreEqual(0, IdentityResolutions);
                }

                Assert.IsFalse(JsonSerializer.Serialize(records).Contains(SENTINEL, StringComparison.Ordinal),
                    "Private names, descriptions, arguments, results and initialization exceptions must not enter product events.");
                return records;
            }

            public async ValueTask DisposeAsync()
            {
                Cancellation.Cancel();
                Input.Complete();
                try
                {
                    if (_running is not null)
                    {
                        try
                        {
                            await _running.WaitAsync(_timeout);
                        }
                        catch (OperationCanceledException) when (Cancellation.IsCancellationRequested)
                        {
                            // Idle stdin cancellation intentionally propagates from the real loop.
                        }
                    }
                }
                finally
                {
                    try
                    {
                        await Session.StopAsync().WaitAsync(_timeout);
                    }
                    finally
                    {
                        await Services.DisposeAsync();
                        _configProvider.Dispose();
                        (_configuration as IDisposable)?.Dispose();
                        _stdout.Dispose();
                        Session.Dispose();
                        Input.Dispose();
                        Output.Dispose();
                        Cancellation.Dispose();
                    }
                }
            }
        }

        private sealed record ToolObservation(EngineTelemetrySession Owner, bool Ready, EngineTelemetryRequestScope? Request, CancellationToken Token);

        private sealed class InitializationGate
        {
            internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            internal async Task WaitAsync(CancellationToken token)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(_timeout, token);
            }
        }

        private sealed class InMemoryConfigLoader(RuntimeConfig availableConfig) : RuntimeConfigLoader
        {
            internal int LoadAttempts { get; private set; }

            public override bool TryLoadKnownConfig([NotNullWhen(true)] out RuntimeConfig? config, bool replaceEnvVar = false)
            {
                LoadAttempts++;
                config = RuntimeConfig = availableConfig;
                return true;
            }

            public override string GetPublishedDraftSchemaLink()
                => Azure.DataApiBuilder.Config.ObjectModel.RuntimeConfig.DEFAULT_CONFIG_SCHEMA_LINK;
        }

        private sealed class ScriptedInput : TextReader
        {
            private readonly Channel<string> _lines = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = false
            });
            private readonly Channel<int> _reads = Channel.CreateUnbounded<int>();
            private int _readCount;

            internal int ReadCount => Volatile.Read(ref _readCount);

            internal void Send(string line) => Assert.IsTrue(_lines.Writer.TryWrite(line), "The script cannot send to completed stdin.");

            internal void Complete() => _lines.Writer.TryComplete();

            internal async Task<int> NextReadAsync() => await _reads.Reader.ReadAsync().AsTask().WaitAsync(_timeout);

            public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
            {
                _reads.Writer.TryWrite(Interlocked.Increment(ref _readCount));
                return await _lines.Reader.WaitToReadAsync(cancellationToken) && _lines.Reader.TryRead(out string? line)
                    ? line : null;
            }
        }

        private sealed class ScriptedOutput : TextWriter
        {
            private readonly ConcurrentQueue<string> _lines = new();
            private readonly Channel<string> _written = Channel.CreateUnbounded<string>();

            public override Encoding Encoding => Encoding.UTF8;

            internal string[] Lines => _lines.ToArray();

            public override void WriteLine(string? value)
            {
                string line = value ?? string.Empty;
                _lines.Enqueue(line);
                _written.Writer.TryWrite(line);
            }

            internal async Task<JsonElement> NextAsync()
            {
                string line = await _written.Reader.ReadAsync().AsTask().WaitAsync(_timeout);
                using JsonDocument document = JsonDocument.Parse(line);
                return document.RootElement.Clone();
            }
        }

        private sealed class CapturingExporter : IEngineTelemetryExporter
        {
            internal ConcurrentQueue<EngineTelemetryEvent> Records { get; } = new();
            internal Channel<EngineTelemetryEvent> Heartbeats { get; } = Channel.CreateUnbounded<EngineTelemetryEvent>();
            internal TaskCompletionSource Stopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public ValueTask<bool> ExportAsync(EngineTelemetryEvent record, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Records.Enqueue(record);
                if (record.Name == HEARTBEAT)
                {
                    Heartbeats.Writer.TryWrite(record);
                }
                else if (record.Name == STOPPED)
                {
                    Stopped.TrySetResult();
                }

                return ValueTask.FromResult(true);
            }

            public void Dispose() => Heartbeats.Writer.TryComplete();
        }

        private sealed class ManualClock : TimeProvider
        {
            private long _timestamp;

            public override long TimestampFrequency => TimeSpan.TicksPerSecond;

            public override long GetTimestamp() => Interlocked.Read(ref _timestamp);

            public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero).AddTicks(GetTimestamp());

            internal void Advance(TimeSpan duration) => Interlocked.Add(ref _timestamp, duration.Ticks);
        }
    }
}
