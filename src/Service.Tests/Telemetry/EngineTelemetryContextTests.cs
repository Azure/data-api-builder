// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Telemetry;

[TestClass]
[TestCategory("EngineTelemetry")]
public class EngineTelemetryContextTests
{
    private const string PRIVATE_VALUE = "PRIVATE_PLATFORM_VALUE_a165fe";

    [DataTestMethod]
    [DataRow("true", "app", "azure_container_apps", "enabled")]
    [DataRow("true", "job", "azure_container_apps", "enabled")]
    [DataRow("true", "kubernetes", "kubernetes", "enabled")]
    [DataRow("true", "app+kubernetes", "azure_container_apps", "enabled")]
    [DataRow("true", "job+kubernetes", "azure_container_apps", "enabled")]
    [DataRow("true", "app+job+kubernetes", "azure_container_apps", "enabled")]
    [DataRow("true", "app+partial-kubernetes", "azure_container_apps", "enabled")]
    [DataRow("true", "job+partial-app", "azure_container_apps", "enabled")]
    [DataRow(null, "app", "azure_container_apps", "unknown")]
    [DataRow(null, "job", "azure_container_apps", "unknown")]
    [DataRow(null, "kubernetes", "kubernetes", "unknown")]
    [DataRow("true", "none", "generic_container", "enabled")]
    [DataRow(null, "none", "unknown", "unknown")]
    [DataRow("false", "app+kubernetes", "unknown", "disabled")]
    [DataRow("true", "partial-app+kubernetes", "generic_container", "enabled")]
    [DataRow(null, "partial-app+kubernetes", "unknown", "unknown")]
    [DataRow("true", "partial-revision+kubernetes", "generic_container", "enabled")]
    [DataRow("true", "partial-job+kubernetes", "generic_container", "enabled")]
    [DataRow("true", "partial-execution+kubernetes", "generic_container", "enabled")]
    [DataRow(null, "partial-app+partial-execution", "unknown", "unknown")]
    [DataRow("true", "partial-kubernetes", "generic_container", "enabled")]
    [DataRow("invalid", "partial-kubernetes", "unknown", "unknown")]
    [DataRow("invalid", "kubernetes", "kubernetes", "unknown")]
    public void HostingUsesOnlyDocumentedPresenceWithConservativePrecedence(string? container, string signals, string hosting, string containerState)
    {
        Dictionary<string, string?> environment = new() { ["DOTNET_RUNNING_IN_CONTAINER"] = container };
        foreach (string signal in signals.Split('+'))
        {
            switch (signal)
            {
                case "app":
                    environment["CONTAINER_APP_NAME"] = PRIVATE_VALUE + "_app";
                    environment["CONTAINER_APP_REVISION"] = PRIVATE_VALUE + "_revision";
                    break;
                case "job":
                    environment["CONTAINER_APP_JOB_NAME"] = PRIVATE_VALUE + "_job";
                    environment["CONTAINER_APP_JOB_EXECUTION_NAME"] = PRIVATE_VALUE + "_execution";
                    break;
                case "kubernetes":
                    environment["KUBERNETES_SERVICE_HOST"] = PRIVATE_VALUE + "_address";
                    environment["KUBERNETES_SERVICE_PORT_HTTPS"] = PRIVATE_VALUE + "_port";
                    break;
                case "partial-app":
                    environment["CONTAINER_APP_NAME"] = PRIVATE_VALUE;
                    break;
                case "partial-revision":
                    environment["CONTAINER_APP_REVISION"] = PRIVATE_VALUE;
                    break;
                case "partial-job":
                    environment["CONTAINER_APP_JOB_NAME"] = PRIVATE_VALUE;
                    break;
                case "partial-execution":
                    environment["CONTAINER_APP_JOB_EXECUTION_NAME"] = PRIVATE_VALUE;
                    break;
                case "partial-kubernetes":
                    environment["KUBERNETES_SERVICE_HOST"] = PRIVATE_VALUE;
                    break;
            }
        }

        List<string> reads = new();
        var context = EngineTelemetryContext.Create("web", name =>
        {
            reads.Add(name);
            return environment.GetValueOrDefault(name);
        });
        Assert.AreEqual(hosting, context["hosting"]);
        Assert.AreEqual(containerState, context["container"]);
        Assert.AreEqual(12, context.Count, "Detection must not add identifiers or extra context fields.");
        Assert.IsFalse(JsonSerializer.Serialize(context).Contains(PRIVATE_VALUE, StringComparison.Ordinal));
        string[] allowed = ["DOTNET_RUNNING_IN_CONTAINER", "CONTAINER_APP_NAME", "CONTAINER_APP_REVISION", "CONTAINER_APP_JOB_NAME",
            "CONTAINER_APP_JOB_EXECUTION_NAME", "KUBERNETES_SERVICE_HOST", "KUBERNETES_SERVICE_PORT_HTTPS"];
        Assert.IsTrue(reads.All(allowed.Contains));
        Assert.AreEqual(reads.Count, reads.Distinct().Count(), "Read each allowed input at most once.");
        Assert.AreEqual(container == "false" ? 1 : 7, reads.Count, "An explicit negative container flag must skip platform probes.");
    }

    [DataTestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow(" \t\r\n ")]
    public void EmptyPlatformMarkersCannotEstablishHosting(string? absent)
    {
        var context = EngineTelemetryContext.Create("web", name => name == "DOTNET_RUNNING_IN_CONTAINER" ? "true" : absent);
        Assert.AreEqual("generic_container", context["hosting"]);
    }

    [DataTestMethod]
    [DataRow(false, "kubernetes")]
    [DataRow(true, "generic_container")]
    public void BlankMarkersDoNotCompleteOrConflictWithPlatformEvidence(bool partialApp, string expected)
    {
        var context = EngineTelemetryContext.Create("web", name => name switch
        {
            "DOTNET_RUNNING_IN_CONTAINER" => "true",
            "KUBERNETES_SERVICE_HOST" or "KUBERNETES_SERVICE_PORT_HTTPS" => PRIVATE_VALUE,
            "CONTAINER_APP_NAME" when partialApp => PRIVATE_VALUE,
            _ => " \t\r\n "
        });
        Assert.AreEqual(expected, context["hosting"]);
    }

    [DataTestMethod]
    [DataRow("CONTAINER_APP_NAME")]
    [DataRow("CONTAINER_APP_REVISION")]
    [DataRow("CONTAINER_APP_JOB_NAME")]
    [DataRow("CONTAINER_APP_JOB_EXECUTION_NAME")]
    [DataRow("KUBERNETES_SERVICE_HOST")]
    [DataRow("KUBERNETES_SERVICE_PORT_HTTPS")]
    public void OneMarkerAloneIsInsufficient(string onlyMarker)
    {
        var context = EngineTelemetryContext.Create("web", name => name == onlyMarker ? PRIVATE_VALUE : null);
        Assert.AreEqual("unknown", context["hosting"]);
        Assert.AreEqual("unknown", context["container"]);
    }

    [DataTestMethod]
    [DataRow(false, false, 0)]
    [DataRow(true, true, 1)]
    [DataRow(true, false, 8)]
    public async Task SessionGatesPlatformReadsAndNeverRetainsTheirValues(bool enabled, bool optOut, int expectedReads)
    {
        string[] allowed = ["DAB_TELEMETRY_OPT_OUT", "DOTNET_RUNNING_IN_CONTAINER", "CONTAINER_APP_NAME", "CONTAINER_APP_REVISION",
            "CONTAINER_APP_JOB_NAME", "CONTAINER_APP_JOB_EXECUTION_NAME", "KUBERNETES_SERVICE_HOST", "KUBERNETES_SERVICE_PORT_HTTPS"];
        List<string> reads = new();
        Capture exporter = new();
        using EngineTelemetrySession session = EngineTelemetrySession.Create(() => exporter,
            enableSyntheticCollection: enabled, readEnvironmentVariable: name =>
            {
                Assert.IsTrue(allowed.Contains(name), "Do not probe any environment variable outside the published allowlist.");
                reads.Add(name);
                return name switch
                {
                    "DAB_TELEMETRY_OPT_OUT" => optOut ? "1" : null,
                    "DOTNET_RUNNING_IN_CONTAINER" => "true",
                    "CONTAINER_APP_NAME" or "CONTAINER_APP_REVISION" => PRIVATE_VALUE,
                    _ => null
                };
            }, showNotice: () => { }, resolveIdentity: _ => new(Guid.NewGuid(), "ephemeral"), startTimer: false);
        Assert.AreEqual(enabled && !optOut, session.IsEnabled, "A swallowed detector exception must not make the test pass by disabling collection.");
        session.AcceptConfiguration(new(null, new(DatabaseType.MSSQL, string.Empty), new(new Dictionary<string, Entity>())));
        session.MarkHostReady();
        Assert.AreEqual(enabled && !optOut, session.IsReady);
        session.AcceptConfiguration(new(null, new(DatabaseType.PostgreSQL, string.Empty), new(new Dictionary<string, Entity>())), "hot_reload");
        Assert.AreEqual(enabled && !optOut, session.IsEnabled);
        Assert.AreEqual(enabled && !optOut, session.IsReady);
        await session.StopAsync();
        Assert.AreEqual(expectedReads, reads.Count);
        Assert.AreEqual(reads.Count, reads.Distinct().Count(), "Immutable run context must not reread platform signals on reload or shutdown.");
        if (enabled && !optOut)
        {
            foreach (string name in new[] { "dab.engine.ready", "dab.engine.configuration_changed", "dab.engine.stopped" })
            {
                Assert.AreEqual(1, exporter.Events.Count(record => record.Name == name), "The lifecycle path must actually run.");
            }

            Assert.IsTrue(exporter.Events.All(record => record.Properties["hosting"] == "azure_container_apps"));
            Assert.IsFalse(JsonSerializer.Serialize(exporter.Events).Contains(PRIVATE_VALUE, StringComparison.Ordinal));
        }
        else
        {
            Assert.AreEqual(0, exporter.Events.Count);
        }
    }

    [DataTestMethod]
    [DataRow("CONTAINER_APP_NAME")]
    [DataRow("CONTAINER_APP_REVISION")]
    [DataRow("CONTAINER_APP_JOB_NAME")]
    [DataRow("CONTAINER_APP_JOB_EXECUTION_NAME")]
    [DataRow("KUBERNETES_SERVICE_HOST")]
    [DataRow("KUBERNETES_SERVICE_PORT_HTTPS")]
    public async Task UnavailablePlatformSignalDisablesOptionalCollectionWithoutExportingExceptions(string failedRead)
    {
        int exporterCreations = 0;
        Capture exporter = new();
        using EngineTelemetrySession session = EngineTelemetrySession.Create(() =>
        {
            Interlocked.Increment(ref exporterCreations);
            return exporter;
        }, enableSyntheticCollection: true, readEnvironmentVariable: name => name == failedRead
            ? throw new InvalidOperationException(PRIVATE_VALUE) : null, showNotice: () => { }, startTimer: false);
        Assert.IsFalse(session.IsEnabled);
        await session.StopAsync();
        Assert.AreEqual(0, exporterCreations);
        Assert.AreEqual(0, exporter.Events.Count);
    }

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
