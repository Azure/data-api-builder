// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Azure.DataApiBuilder.Service.Telemetry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Telemetry
{
    [TestClass]
    [TestCategory("EngineTelemetry")]
    [TestCategory("CliTelemetry")]
    [DoNotParallelize]
    public class ProductTelemetryAssemblyBoundaryTests
    {
        [DataTestMethod]
        [DataRow(typeof(ApplicationInsightsEventAttributes))]
        [DataRow(typeof(ApplicationInsightsTelemetryDestination))]
        [DataRow(typeof(EngineTelemetryApplicationInsightsExporter))]
        [DataRow(typeof(EngineTelemetryExportAttempt))]
        [DataRow(typeof(EngineTelemetrySdkTransportHandler))]
        [DataRow(typeof(ProductTelemetryBootstrap))]
        [DataRow(typeof(ProductTelemetrySenderPool))]
        public void ExportInfrastructureIsInternalToCoreNotService(Type type)
        {
            Assembly core = typeof(IProductTelemetryEvent).Assembly;
            Assembly service = typeof(EngineTelemetryHosting).Assembly;

            Assert.AreEqual("Microsoft.DataApiBuilder.Core", core.GetName().Name);
            Assert.AreSame(core, type.Assembly);
            Assert.AreEqual("Azure.DataApiBuilder.Core.Telemetry.Product", type.Namespace);
            Assert.IsTrue(type.IsNotPublic, "Export infrastructure must remain a top-level internal type.");
            Assert.IsFalse(type.IsVisible);
            Assert.IsNull(service.GetType(type.FullName!));
            Assert.IsNull(service.GetType("Azure.DataApiBuilder.Service.Telemetry." + type.Name));
            Assert.IsFalse(core.GetReferencedAssemblies().Any(reference => reference.Name == service.GetName().Name),
                "The shared sender and its factory must not introduce a Core-to-Service dependency.");
        }

        [TestMethod]
        public void DefaultDisabledBootstrapDoesNotCreateSdkSenders()
        {
            // Observe the actual process pool without acquiring a lease, replacing its factory,
            // or disposing it. Existing cached senders, if any, must also remain untouched.
            (object Sender, object? Exporter)[] before = SnapshotDefaultSenders();
            List<string> reads = [];

            Assert.IsFalse(ProductTelemetryBootstrap.TryGetDestination(name =>
            {
                reads.Add(name);
                return null;
            }, out ApplicationInsightsTelemetryDestination? destination));

            Assert.IsNull(destination);
            CollectionAssert.AreEqual(new[]
            {
                ProductTelemetryBootstrap.OPT_OUT_VARIABLE,
                ProductTelemetryBootstrap.TEST_MODE_VARIABLE
            }, reads, "Default-disabled bootstrap must not read SDK settings or destination routing.");

            (object Sender, object? Exporter)[] after = SnapshotDefaultSenders();
            Assert.AreEqual(before.Length, after.Length, "Default-disabled bootstrap must not create a sender.");
            for (int index = 0; index < before.Length; index++)
            {
                Assert.AreSame(before[index].Sender, after[index].Sender);
                Assert.AreSame(before[index].Exporter, after[index].Exporter,
                    "Default-disabled bootstrap must not initialize or replace an SDK exporter.");
            }
        }

        private static (object Sender, object? Exporter)[] SnapshotDefaultSenders()
        {
            const BindingFlags INSTANCE = BindingFlags.Instance | BindingFlags.NonPublic;
            Type poolType = typeof(ProductTelemetrySenderPool);
            object pool = poolType.GetField("_default", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            object sync = poolType.GetField("_sync", INSTANCE)!.GetValue(pool)!;
            lock (sync)
            {
                IDictionary senders = (IDictionary)poolType.GetField("_senders", INSTANCE)!.GetValue(pool)!;
                return senders.Values.Cast<object>()
                    .Select(sender => (sender, sender.GetType().GetProperty("Exporter", INSTANCE)!.GetValue(sender)))
                    .ToArray();
            }
        }
    }
}
