// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Telemetry
{
    [TestClass]
    [TestCategory("EngineTelemetry")]
    [TestCategory("CliTelemetry")]
    public class CliTelemetryDeliveryTests
    {
        private static readonly TimeSpan _testTimeout = TimeSpan.FromSeconds(10);

        [TestMethod]
        public void EnvelopeExposesOnlyCommonMetadataAndRetainsImmutableProperties()
        {
            ImmutableDictionary<string, string>.Builder properties = ImmutableDictionary.CreateBuilder<string, string>();
            properties.Add("command", "init");
            CliTelemetryEvent record = CreateEvent() with { Properties = properties.ToImmutable() };
            IProductTelemetryEvent envelope = record;
            properties["command"] = "start";
            CliTelemetryEvent copy = record with { Sequence = record.Sequence + 1 };

            Assert.IsTrue(envelope.IsSynthetic);
            Assert.IsTrue(copy.IsSynthetic);
            Assert.AreEqual("init", envelope.Properties["command"]);
            Assert.AreSame(record.Properties, copy.Properties);
            Assert.AreEqual(envelope.EventId, copy.EventId);
            Assert.AreEqual(envelope.SessionId, copy.SessionId);
            Assert.AreEqual(envelope.OccurredAt, copy.OccurredAt);
            Assert.AreEqual(envelope.Name, copy.Name);
            Assert.AreEqual(1L, envelope.Sequence);
            Assert.AreEqual(2L, copy.Sequence);
            CollectionAssert.AreEquivalent(new[]
            {
                nameof(IProductTelemetryEvent.EventId),
                nameof(IProductTelemetryEvent.SessionId),
                nameof(IProductTelemetryEvent.Sequence),
                nameof(IProductTelemetryEvent.OccurredAt),
                nameof(IProductTelemetryEvent.Name),
                nameof(IProductTelemetryEvent.Properties),
                nameof(IProductTelemetryEvent.IsSynthetic)
            }, typeof(CliTelemetryEvent).GetProperties().Select(property => property.Name).ToArray(),
                "CLI envelopes must not acquire an engine configuration epoch property.");
        }

        [TestMethod]
        public async Task CliDeliveryBoundsQueuedAndInFlightRecordsAndDisposesOwnedExporter()
        {
            GatedExporter exporter = new();
            int factoryCalls = 0;
            using ProductTelemetryDelivery<CliTelemetryEvent> delivery = new(() =>
            {
                Interlocked.Increment(ref factoryCalls);
                return exporter;
            }, capacity: 2);
            CliTelemetryEvent first = CreateEvent();
            CliTelemetryEvent second = CreateEvent(2);

            try
            {
                Assert.AreEqual(0, Volatile.Read(ref factoryCalls), "Exporter initialization must remain lazy.");
                Assert.IsTrue(delivery.TryEnqueue(first));
                await exporter.Entered.Task.WaitAsync(_testTimeout);
                Assert.IsTrue(delivery.TryEnqueue(second));
                Assert.IsFalse(delivery.TryEnqueue(CreateEvent(3)));
                Assert.AreEqual(1L, delivery.DroppedEvents);
                Assert.AreEqual(0, exporter.DisposeCalls);

                Task stop = delivery.StopAsync();
                Assert.AreSame(stop, delivery.StopAsync());
                exporter.Release.TrySetResult();
                await stop.WaitAsync(_testTimeout);

                CliTelemetryEvent[] attempts = exporter.Attempts.ToArray();
                Assert.AreEqual(2, attempts.Length);
                Assert.AreSame(first, attempts[0]);
                Assert.AreSame(second, attempts[1]);
                Assert.AreEqual(1, factoryCalls);
                Assert.AreEqual(1, exporter.DisposeCalls);
                Assert.AreEqual(1L, delivery.DroppedEvents);
            }
            finally
            {
                exporter.Release.TrySetResult();
            }
        }

        [TestMethod]
        public async Task CommonExporterCanBeUsedContravariantlyForCliDelivery()
        {
            CommonExporter exporter = new();
            IProductTelemetryExporter<IProductTelemetryEvent> commonExporter = exporter;
            IProductTelemetryExporter<CliTelemetryEvent> cliExporter = commonExporter;
            using ProductTelemetryDelivery<CliTelemetryEvent> delivery = new(() => cliExporter);
            CliTelemetryEvent record = CreateEvent();

            Assert.IsTrue(delivery.TryEnqueue(record));
            await delivery.StopAsync().WaitAsync(_testTimeout);

            Assert.AreSame(record, exporter.Records.Single());
            Assert.AreEqual(0L, delivery.DroppedEvents);
            Assert.AreEqual(1, exporter.DisposeCalls);
        }

        private static CliTelemetryEvent CreateEvent(long sequence = 1) => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            sequence,
            new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.Zero),
            "cli.synthetic",
            ImmutableDictionary<string, string>.Empty);

        private sealed class GatedExporter : IProductTelemetryExporter<CliTelemetryEvent>
        {
            private int _disposeCalls;

            public ConcurrentQueue<CliTelemetryEvent> Attempts { get; } = new();
            public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public int DisposeCalls => Volatile.Read(ref _disposeCalls);

            public async ValueTask<bool> ExportAsync(CliTelemetryEvent record, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Attempts.Enqueue(record);
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                return true;
            }

            public void Dispose() => Interlocked.Increment(ref _disposeCalls);
        }

        private sealed class CommonExporter : IProductTelemetryExporter<IProductTelemetryEvent>
        {
            private int _disposeCalls;

            public ConcurrentQueue<IProductTelemetryEvent> Records { get; } = new();
            public int DisposeCalls => Volatile.Read(ref _disposeCalls);

            public ValueTask<bool> ExportAsync(IProductTelemetryEvent record, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Records.Enqueue(record);
                return ValueTask.FromResult(true);
            }

            public void Dispose() => Interlocked.Increment(ref _disposeCalls);
        }
    }
}
