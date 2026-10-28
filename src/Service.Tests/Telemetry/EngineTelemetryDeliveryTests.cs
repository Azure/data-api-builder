// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Telemetry
{
    [TestClass]
    [TestCategory("EngineTelemetry")]
    public class EngineTelemetryDeliveryTests
    {
        private static readonly TimeSpan _testTimeout = TimeSpan.FromSeconds(10);

        [TestMethod]
        public void EnvelopeRetainsImmutablePropertiesAndIsAlwaysSynthetic()
        {
            ImmutableDictionary<string, string>.Builder properties = ImmutableDictionary.CreateBuilder<string, string>();
            properties.Add("api", "rest");
            EngineTelemetryEvent record = CreateEvent() with { Properties = properties.ToImmutable() };
            properties["api"] = "mcp";
            EngineTelemetryEvent copy = record with { Sequence = record.Sequence + 1 };

            Assert.IsTrue(record.IsSynthetic);
            Assert.IsTrue(copy.IsSynthetic);
            Assert.AreEqual("rest", record.Properties["api"]);
            Assert.AreSame(record.Properties, copy.Properties);
            Assert.AreEqual(record.EventId, copy.EventId);
            Assert.AreEqual(record.SessionId, copy.SessionId);
            Assert.AreEqual(record.OccurredAt, copy.OccurredAt);
            Assert.AreEqual(record.ConfigurationEpoch, copy.ConfigurationEpoch);
            Assert.AreEqual(record.Name, copy.Name);
            Assert.AreEqual(1L, record.Sequence);
            Assert.AreEqual(2L, copy.Sequence);
        }

        [TestMethod]
        public void ConstructorRejectsInvalidArgumentsBeforeInitializingAnExporter()
        {
            Func<IEngineTelemetryExporter> factory = () => throw new AssertFailedException("Factory must remain lazy.");
            Assert.ThrowsException<ArgumentNullException>(() => new EngineTelemetryDelivery(null!));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new EngineTelemetryDelivery(factory, capacity: 0));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new EngineTelemetryDelivery(factory, capacity: -1));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new EngineTelemetryDelivery(factory, maxAttempts: 0));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new EngineTelemetryDelivery(factory, maxAttempts: -1));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new EngineTelemetryDelivery(factory, flushTimeout: TimeSpan.FromTicks(-1)));
            Assert.ThrowsException<ArgumentNullException>(() => new EngineTelemetryDelivery(factory, 1, 1, null, null!));
        }

        [TestMethod]
        public async Task EmptyStopNeverCreatesAnExporter()
        {
            int factoryCalls = 0;
            using EngineTelemetryDelivery delivery = new(() =>
            {
                Interlocked.Increment(ref factoryCalls);
                throw new InvalidOperationException("An empty worker must not initialize the exporter.");
            });

            await delivery.StopAsync().WaitAsync(_testTimeout);
            delivery.Disable();
            delivery.Dispose();
            await delivery.StopAsync().WaitAsync(_testTimeout);
            Assert.AreEqual(0, factoryCalls);
            Assert.AreEqual(0L, delivery.DroppedEvents);
        }

        [TestMethod]
        public async Task EnqueueReturnsWhileWorkerFactoryIsBlocked()
        {
            TaskCompletionSource entered = Signal();
            TaskCompletionSource release = Signal();
            FakeExporter exporter = new((_, _) => true);
            int factoryCalls = 0;
            using EngineTelemetryDelivery delivery = new(() =>
            {
                Interlocked.Increment(ref factoryCalls);
                entered.TrySetResult();
                Wait(release.Task);
                return exporter;
            });

            try
            {
                Assert.AreEqual(0, factoryCalls);
                Task<bool> enqueue = Task.Run(() => delivery.TryEnqueue(CreateEvent()));
                Assert.IsTrue(await enqueue.WaitAsync(_testTimeout), "Enqueue must not run the factory inline.");
                await entered.Task.WaitAsync(_testTimeout);
                Assert.AreEqual(0, exporter.Attempts.Count);

                release.TrySetResult();
                await delivery.StopAsync().WaitAsync(_testTimeout);
                Assert.AreEqual(1, factoryCalls);
                Assert.AreEqual(1, exporter.Attempts.Count);
                Assert.AreEqual(1, exporter.DisposeCalls);
                Assert.AreEqual(0L, delivery.DroppedEvents);
            }
            finally
            {
                release.TrySetResult();
            }
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task WorkerDoesNotInheritActivityOrAsyncLocalScopes(bool alreadySuppressed)
        {
            AsyncLocal<string?> scope = new() { Value = "customer-scope" };
            using Activity activity = new Activity("customer-request").Start();
            Activity? factoryActivity = activity;
            Activity? exportActivity = activity;
            Activity? disposeActivity = activity;
            string? factoryScope = scope.Value;
            string? exportScope = scope.Value;
            string? disposeScope = scope.Value;
            FakeExporter exporter = new((_, _) =>
            {
                exportActivity = Activity.Current;
                exportScope = scope.Value;
                return true;
            }, () =>
            {
                disposeActivity = Activity.Current;
                disposeScope = scope.Value;
            });
            Func<IEngineTelemetryExporter> factory = () =>
            {
                factoryActivity = Activity.Current;
                factoryScope = scope.Value;
                return exporter;
            };

            EngineTelemetryDelivery delivery;
            if (alreadySuppressed)
            {
                using (ExecutionContext.SuppressFlow())
                {
                    delivery = new(factory);
                }
            }
            else
            {
                delivery = new(factory);
            }

            using (delivery)
            {
                Assert.IsTrue(delivery.TryEnqueue(CreateEvent()));
                await delivery.StopAsync().WaitAsync(_testTimeout);
                Assert.AreEqual(1, exporter.Attempts.Count);
                Assert.AreEqual(1, exporter.DisposeCalls);
                Assert.IsNull(factoryActivity);
                Assert.IsNull(exportActivity);
                Assert.IsNull(disposeActivity);
                Assert.IsNull(factoryScope);
                Assert.IsNull(exportScope);
                Assert.IsNull(disposeScope);
                Assert.AreSame(activity, Activity.Current);
                Assert.AreEqual("customer-scope", scope.Value);
            }
        }

        [TestMethod]
        public async Task DefaultCapacityIncludesBlockedInFlightRecord()
        {
            TaskCompletionSource entered = Signal();
            TaskCompletionSource release = Signal();
            int calls = 0;
            FakeExporter exporter = new((_, token) =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    entered.TrySetResult();
                    Wait(release.Task, token);
                }

                return true;
            });
            using EngineTelemetryDelivery delivery = new(() => exporter);

            try
            {
                Assert.IsTrue(delivery.TryEnqueue(CreateEvent()));
                await entered.Task.WaitAsync(_testTimeout);
                for (int i = 1; i < 256; i++)
                {
                    Assert.IsTrue(delivery.TryEnqueue(CreateEvent(i + 1)));
                }

                for (int i = 0; i < 32; i++)
                {
                    Assert.IsFalse(delivery.TryEnqueue(CreateEvent()));
                }

                Assert.AreEqual(32L, delivery.DroppedEvents);
                Assert.AreEqual(1, exporter.Attempts.Count);
                Task stop = delivery.StopAsync();
                release.TrySetResult();
                await stop.WaitAsync(_testTimeout);
                Assert.AreEqual(256, exporter.Attempts.Count);
                Assert.AreEqual(32L, delivery.DroppedEvents);
                Assert.IsFalse(exporter.HadOverlappingExports);
            }
            finally
            {
                release.TrySetResult();
            }
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task CompletedRecordReleasesItsSlotBeforeTheNextExport(bool firstSucceeds)
        {
            TaskCompletionSource secondEntered = Signal();
            TaskCompletionSource release = Signal();
            FakeExporter exporter = new((record, token) =>
            {
                if (record.Sequence == 1)
                {
                    return firstSucceeds;
                }

                if (record.Sequence == 2)
                {
                    secondEntered.TrySetResult();
                    Wait(release.Task, token);
                }

                return true;
            });
            using EngineTelemetryDelivery delivery = new(() => exporter, capacity: 2, maxAttempts: 1);

            try
            {
                Assert.IsTrue(delivery.TryEnqueue(CreateEvent()));
                Assert.IsTrue(delivery.TryEnqueue(CreateEvent(2)));
                await secondEntered.Task.WaitAsync(_testTimeout);
                Assert.IsTrue(delivery.TryEnqueue(CreateEvent(3)), "The completed record no longer owns a slot.");
                Assert.IsFalse(delivery.TryEnqueue(CreateEvent(4)), "The in-flight and queued records still own both slots.");
                Task stop = delivery.StopAsync();
                release.TrySetResult();
                await stop.WaitAsync(_testTimeout);

                Assert.AreEqual(3, exporter.Attempts.Count);
                Assert.AreEqual(firstSucceeds ? 1L : 2L, delivery.DroppedEvents);
            }
            finally
            {
                release.TrySetResult();
            }
        }

        [TestMethod]
        public async Task ConcurrentProducersCannotExceedCapacityAndEveryRejectedRecordIsCounted()
        {
            TaskCompletionSource entered = Signal();
            TaskCompletionSource release = Signal();
            FakeExporter exporter = new((_, token) =>
            {
                entered.TrySetResult();
                Wait(release.Task, token);
                return true;
            });
            using EngineTelemetryDelivery delivery = new(() => exporter, capacity: 8);

            try
            {
                Assert.IsTrue(delivery.TryEnqueue(CreateEvent()));
                await entered.Task.WaitAsync(_testTimeout);
                Task<bool>[] producers = Enumerable.Range(0, 64)
                    .Select(i => Task.Run(() => delivery.TryEnqueue(CreateEvent(i + 2))))
                    .ToArray();
                bool[] accepted = await Task.WhenAll(producers).WaitAsync(_testTimeout);
                Assert.AreEqual(7, accepted.Count(value => value));
                Assert.AreEqual(57L, delivery.DroppedEvents);

                delivery.Disable();
                await exporter.Disposed.Task.WaitAsync(_testTimeout);
                await delivery.StopAsync().WaitAsync(_testTimeout);
                Assert.AreEqual(65L, delivery.DroppedEvents);
                Assert.AreEqual(1, exporter.Attempts.Count);
                Assert.IsFalse(exporter.HadOverlappingExports);
            }
            finally
            {
                release.TrySetResult();
            }
        }

        [TestMethod]
        public async Task FalseAndThrowRetriesRetainExactlyTheSameEnvelopeAndCapacitySlot()
        {
            ControlledTimeProvider clock = new();
            TaskCompletionSource succeeded = Signal();
            int calls = 0;
            FakeExporter exporter = new((_, _) =>
            {
                int attempt = Interlocked.Increment(ref calls);
                if (attempt == 1)
                {
                    return false;
                }

                if (attempt == 2)
                {
                    throw new InvalidOperationException("Synthetic export failure.");
                }

                succeeded.TrySetResult();
                return true;
            });
            using EngineTelemetryDelivery delivery = Create(() => exporter, clock, capacity: 1);
            EngineTelemetryEvent record = CreateEvent();
            Assert.IsTrue(delivery.TryEnqueue(record));

            ControlledTimer firstDelay = await clock.NextTimerAsync();
            Assert.AreEqual(TimeSpan.FromMilliseconds(100), firstDelay.DueTime);
            Assert.IsFalse(delivery.TryEnqueue(CreateEvent()), "A retry must retain its capacity slot.");
            Assert.AreEqual(1L, delivery.DroppedEvents);
            firstDelay.Fire();
            ControlledTimer secondDelay = await clock.NextTimerAsync();
            Assert.AreEqual(TimeSpan.FromMilliseconds(200), secondDelay.DueTime);
            secondDelay.Fire();

            await succeeded.Task.WaitAsync(_testTimeout);
            await delivery.StopAsync().WaitAsync(_testTimeout);
            Assert.AreEqual(3, exporter.Attempts.Count);
            Assert.IsTrue(exporter.Attempts.All(attempt => ReferenceEquals(record, attempt)));
            Assert.IsTrue(exporter.Attempts.All(attempt => attempt.EventId == record.EventId && attempt.IsSynthetic));
            Assert.AreEqual(1L, delivery.DroppedEvents, "Successful retries must not increment loss per attempt.");
            Assert.IsFalse(exporter.HadOverlappingExports);
        }

        [TestMethod]
        public async Task RetryDelaysAreExponentialAndCapped()
        {
            ControlledTimeProvider clock = new();
            FakeExporter exporter = new((_, _) => false);
            using EngineTelemetryDelivery delivery = Create(() => exporter, clock, maxAttempts: 7);
            Assert.IsTrue(delivery.TryEnqueue(CreateEvent()));

            foreach (int milliseconds in new[] { 100, 200, 400, 800, 1000, 1000 })
            {
                ControlledTimer delay = await clock.NextTimerAsync();
                Assert.AreEqual(TimeSpan.FromMilliseconds(milliseconds), delay.DueTime);
                delay.Fire();
            }

            await delivery.StopAsync().WaitAsync(_testTimeout);
            Assert.AreEqual(7, exporter.Attempts.Count);
            Assert.AreEqual(1L, delivery.DroppedEvents);
        }

        [DataTestMethod]
        [DataRow(0)]
        [DataRow(1)]
        [DataRow(2)]
        public async Task ExhaustedRecordIsDroppedOnceAndWorkerContinues(int failureMode)
        {
            ControlledTimeProvider clock = new();
            EngineTelemetryEvent failed = CreateEvent();
            EngineTelemetryEvent successful = CreateEvent(2);
            TaskCompletionSource nextRecord = Signal();
            FakeExporter exporter = new((record, _) =>
            {
                if (record.EventId == failed.EventId)
                {
                    return failureMode switch
                    {
                        0 => false,
                        1 => throw new InvalidOperationException("Synthetic failure."),
                        _ => throw new OperationCanceledException("Not the delivery cancellation token.")
                    };
                }

                nextRecord.TrySetResult();
                return true;
            });
            using EngineTelemetryDelivery delivery = Create(() => exporter, clock, capacity: 2);
            Assert.IsTrue(delivery.TryEnqueue(failed));
            Assert.IsTrue(delivery.TryEnqueue(successful));
            (await clock.NextTimerAsync()).Fire();
            (await clock.NextTimerAsync()).Fire();
            await nextRecord.Task.WaitAsync(_testTimeout);
            await delivery.StopAsync().WaitAsync(_testTimeout);

            CollectionAssert.AreEqual(new[] { failed, failed, failed, successful }, exporter.Attempts.ToArray());
            Assert.AreEqual(1L, delivery.DroppedEvents);
            Assert.AreEqual(1, exporter.DisposeCalls);
        }

        [TestMethod]
        public async Task FactoryInitializationFailuresUseTheRecordRetryBudget()
        {
            ControlledTimeProvider clock = new();
            FakeExporter exporter = new((_, _) => true);
            int factoryCalls = 0;
            using EngineTelemetryDelivery delivery = Create(() =>
            {
                if (Interlocked.Increment(ref factoryCalls) < 3)
                {
                    throw new InvalidOperationException("Synthetic initialization failure.");
                }

                return exporter;
            }, clock);
            EngineTelemetryEvent record = CreateEvent();
            Assert.IsTrue(delivery.TryEnqueue(record));
            ControlledTimer firstDelay = await clock.NextTimerAsync();
            Assert.AreEqual(1, factoryCalls);
            Assert.AreEqual(0, exporter.Attempts.Count);
            firstDelay.Fire();
            (await clock.NextTimerAsync()).Fire();
            await delivery.StopAsync().WaitAsync(_testTimeout);

            Assert.AreEqual(3, factoryCalls);
            Assert.AreSame(record, exporter.Attempts.Single());
            Assert.AreEqual(0L, delivery.DroppedEvents);
            Assert.AreEqual(1, exporter.DisposeCalls);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task ExhaustedFactoryFailureOrNullResultDoesNotFaultShutdown(bool returnsNull)
        {
            ControlledTimeProvider clock = new();
            int factoryCalls = 0;
            using EngineTelemetryDelivery delivery = Create(() =>
            {
                Interlocked.Increment(ref factoryCalls);
                return returnsNull ? null! : throw new InvalidOperationException("Synthetic initialization failure.");
            }, clock, maxAttempts: 2);
            Assert.IsTrue(delivery.TryEnqueue(CreateEvent()));
            (await clock.NextTimerAsync()).Fire();
            await delivery.StopAsync().WaitAsync(_testTimeout);

            Assert.AreEqual(2, factoryCalls);
            Assert.AreEqual(1L, delivery.DroppedEvents);
        }

        [TestMethod]
        public async Task GracefulStopClosesAdmissionAndDrainsSeriallyWithOneSharedWait()
        {
            ControlledTimeProvider clock = new();
            TaskCompletionSource entered = Signal();
            TaskCompletionSource release = Signal();
            FakeExporter exporter = new((_, token) =>
            {
                entered.TrySetResult();
                Wait(release.Task, token);
                return true;
            });
            using EngineTelemetryDelivery delivery = Create(() => exporter, clock, capacity: 2);
            EngineTelemetryEvent first = CreateEvent();
            EngineTelemetryEvent second = CreateEvent(2);

            try
            {
                Assert.IsTrue(delivery.TryEnqueue(first));
                await entered.Task.WaitAsync(_testTimeout);
                Assert.IsTrue(delivery.TryEnqueue(second));
                Task stop = delivery.StopAsync();
                Assert.AreSame(stop, delivery.StopAsync());
                Assert.IsFalse(stop.IsCompleted);
                Assert.IsFalse(delivery.TryEnqueue(CreateEvent(3)));
                ControlledTimer deadline = await clock.NextTimerAsync();
                Assert.AreEqual(TimeSpan.FromSeconds(2), deadline.DueTime);
                Assert.AreEqual(1, clock.CreatedTimerCount);

                release.TrySetResult();
                await stop.WaitAsync(_testTimeout);
                await deadline.Disposed.Task.WaitAsync(_testTimeout);
                CollectionAssert.AreEqual(new[] { first, second }, exporter.Attempts.ToArray());
                Assert.AreEqual(1L, delivery.DroppedEvents);
                Assert.AreEqual(1, exporter.DisposeCalls);
                Assert.IsFalse(exporter.HadOverlappingExports);
            }
            finally
            {
                release.TrySetResult();
            }
        }

        [TestMethod]
        public async Task DisableCancelsInFlightAndDiscardsQueuedRecordsWithoutRetryOrFlush()
        {
            TaskCompletionSource<CancellationToken> entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource release = Signal();
            FakeExporter exporter = new((_, token) =>
            {
                entered.TrySetResult(token);
                Wait(release.Task, token);
                return true;
            });
            using EngineTelemetryDelivery delivery = new(() => exporter, capacity: 2);

            try
            {
                Assert.IsTrue(delivery.TryEnqueue(CreateEvent()));
                CancellationToken token = await entered.Task.WaitAsync(_testTimeout);
                Assert.IsTrue(delivery.TryEnqueue(CreateEvent(2)));
                await Task.Run(delivery.Disable).WaitAsync(_testTimeout);
                Assert.IsTrue(token.IsCancellationRequested);
                Assert.AreEqual(2L, delivery.DroppedEvents);
                Assert.IsFalse(delivery.TryEnqueue(CreateEvent(3)));
                delivery.Disable();
                delivery.Dispose();
                await exporter.Disposed.Task.WaitAsync(_testTimeout);
                await delivery.StopAsync().WaitAsync(_testTimeout);

                Assert.AreEqual(3L, delivery.DroppedEvents);
                Assert.AreEqual(1, exporter.Attempts.Count);
                Assert.AreEqual(1, exporter.DisposeCalls);
            }
            finally
            {
                release.TrySetResult();
            }
        }

        [TestMethod]
        public async Task DisableDuringInitializationPreventsAnyExportAndWorkerDisposesCreatedExporter()
        {
            TaskCompletionSource entered = Signal();
            TaskCompletionSource release = Signal();
            FakeExporter exporter = new((_, _) => true);
            using EngineTelemetryDelivery delivery = new(() =>
            {
                entered.TrySetResult();
                Wait(release.Task);
                return exporter;
            }, capacity: 2);

            try
            {
                Assert.IsTrue(delivery.TryEnqueue(CreateEvent()));
                await entered.Task.WaitAsync(_testTimeout);
                Assert.IsTrue(delivery.TryEnqueue(CreateEvent(2)));
                await Task.Run(delivery.Disable).WaitAsync(_testTimeout);
                Assert.AreEqual(2L, delivery.DroppedEvents);
                release.TrySetResult();
                await exporter.Disposed.Task.WaitAsync(_testTimeout);
                await delivery.StopAsync().WaitAsync(_testTimeout);

                Assert.AreEqual(0, exporter.Attempts.Count);
                Assert.AreEqual(1, exporter.DisposeCalls);
            }
            finally
            {
                release.TrySetResult();
            }
        }

        [TestMethod]
        public async Task DisableDuringBackoffCancelsTheDelayAndDoesNotStartAnotherExport()
        {
            ControlledTimeProvider clock = new();
            FakeExporter exporter = new((_, _) => false);
            using EngineTelemetryDelivery delivery = Create(() => exporter, clock, capacity: 2);
            Assert.IsTrue(delivery.TryEnqueue(CreateEvent()));
            Assert.IsTrue(delivery.TryEnqueue(CreateEvent(2)));
            ControlledTimer delay = await clock.NextTimerAsync();
            delivery.Disable();
            await delay.Disposed.Task.WaitAsync(_testTimeout);
            await exporter.Disposed.Task.WaitAsync(_testTimeout);
            await delivery.StopAsync().WaitAsync(_testTimeout);
            delay.Fire();

            Assert.AreEqual(1, exporter.Attempts.Count);
            Assert.AreEqual(2L, delivery.DroppedEvents);
        }

        [DataTestMethod]
        [DataRow(500, 500)]
        [DataRow(3600000, 2000)]
        public async Task StopDeadlineIsClampedAndDoesNotWaitAgainAfterCancel(int requestedMilliseconds, int expectedMilliseconds)
        {
            ControlledTimeProvider clock = new();
            TaskCompletionSource<CancellationToken> entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource release = Signal();
            FakeExporter exporter = new((_, token) =>
            {
                entered.TrySetResult(token);
                Wait(release.Task, token);
                return true;
            });
            using EngineTelemetryDelivery delivery = Create(() => exporter, clock, capacity: 2,
                flushTimeout: TimeSpan.FromMilliseconds(requestedMilliseconds));

            try
            {
                Assert.IsTrue(delivery.TryEnqueue(CreateEvent()));
                CancellationToken token = await entered.Task.WaitAsync(_testTimeout);
                Assert.IsTrue(delivery.TryEnqueue(CreateEvent(2)));
                Task stop = delivery.StopAsync();
                Assert.AreSame(stop, delivery.StopAsync());
                ControlledTimer deadline = await clock.NextTimerAsync();
                Assert.AreEqual(TimeSpan.FromMilliseconds(expectedMilliseconds), deadline.DueTime);
                Assert.AreEqual(1, clock.CreatedTimerCount);
                deadline.Fire();
                await stop.WaitAsync(_testTimeout);

                Assert.IsTrue(token.IsCancellationRequested);
                Assert.AreEqual(2L, delivery.DroppedEvents);
                await exporter.Disposed.Task.WaitAsync(_testTimeout);
                Assert.AreEqual(1, exporter.Attempts.Count);
            }
            finally
            {
                release.TrySetResult();
            }
        }

        [TestMethod]
        public async Task ZeroTimeoutDoesNotWaitForAnExporterThatFailsToHonorCancellation()
        {
            TaskCompletionSource<CancellationToken> entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource release = Signal();
            FakeExporter exporter = new((_, token) =>
            {
                entered.TrySetResult(token);
                // Deliberately violate the adapter contract to verify the caller's wait is bounded.
                Wait(release.Task);
                return true;
            });
            using EngineTelemetryDelivery delivery = new(() => exporter, flushTimeout: TimeSpan.Zero);

            try
            {
                Assert.IsTrue(delivery.TryEnqueue(CreateEvent()));
                CancellationToken token = await entered.Task.WaitAsync(_testTimeout);
                await delivery.StopAsync().WaitAsync(_testTimeout);
                await Task.Run(delivery.Dispose).WaitAsync(_testTimeout);
                Assert.IsTrue(token.IsCancellationRequested);
                Assert.IsFalse(exporter.Disposed.Task.IsCompleted);
                Assert.AreEqual(1L, delivery.DroppedEvents);

                release.TrySetResult();
                await exporter.Disposed.Task.WaitAsync(_testTimeout);
                Assert.AreEqual(1, exporter.Attempts.Count);
                Assert.AreEqual(1L, delivery.DroppedEvents, "Late success must not double-account an abandoned record.");
            }
            finally
            {
                release.TrySetResult();
            }
        }

        [TestMethod]
        public async Task CallerCancellationStopsTheDrainWithoutWaitingForItsDeadline()
        {
            ControlledTimeProvider clock = new();
            TaskCompletionSource<CancellationToken> entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource release = Signal();
            FakeExporter exporter = new((_, token) =>
            {
                entered.TrySetResult(token);
                Wait(release.Task, token);
                return true;
            });
            using EngineTelemetryDelivery delivery = Create(() => exporter, clock, capacity: 2);
            using CancellationTokenSource callerCancellation = new();

            try
            {
                Assert.IsTrue(delivery.TryEnqueue(CreateEvent()));
                CancellationToken token = await entered.Task.WaitAsync(_testTimeout);
                Assert.IsTrue(delivery.TryEnqueue(CreateEvent(2)));
                Task sharedStop = delivery.StopAsync();
                Task callerStop = delivery.StopAsync(callerCancellation.Token);
                ControlledTimer deadline = await clock.NextTimerAsync();
                callerCancellation.Cancel();
                await callerStop.WaitAsync(_testTimeout);
                Assert.IsTrue(token.IsCancellationRequested);
                Assert.AreEqual(2L, delivery.DroppedEvents);
                await exporter.Disposed.Task.WaitAsync(_testTimeout);
                await sharedStop.WaitAsync(_testTimeout);
                await deadline.Disposed.Task.WaitAsync(_testTimeout);
                Assert.AreEqual(1, exporter.Attempts.Count);
            }
            finally
            {
                release.TrySetResult();
            }
        }

        [TestMethod]
        public async Task AlreadyCanceledStopDisablesWithoutInitializingAnExporter()
        {
            int factoryCalls = 0;
            using EngineTelemetryDelivery delivery = new(() =>
            {
                Interlocked.Increment(ref factoryCalls);
                throw new InvalidOperationException("Exporter must remain uninitialized.");
            });
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            await delivery.StopAsync(cancellation.Token).WaitAsync(_testTimeout);
            Assert.IsFalse(delivery.TryEnqueue(CreateEvent()));
            await delivery.StopAsync().WaitAsync(_testTimeout);
            Assert.AreEqual(0, factoryCalls);
            Assert.AreEqual(1L, delivery.DroppedEvents);
        }

        [TestMethod]
        public async Task StopAndDisposeDoNotWaitForBlockedExporterDisposal()
        {
            ControlledTimeProvider clock = new();
            TaskCompletionSource disposalEntered = Signal();
            TaskCompletionSource releaseDisposal = Signal();
            FakeExporter exporter = new((_, _) => true, () =>
            {
                disposalEntered.TrySetResult();
                Wait(releaseDisposal.Task);
            });
            using EngineTelemetryDelivery delivery = Create(() => exporter, clock);

            try
            {
                Assert.IsTrue(delivery.TryEnqueue(CreateEvent()));
                Task stop = delivery.StopAsync();
                await disposalEntered.Task.WaitAsync(_testTimeout);
                ControlledTimer deadline = await clock.NextTimerAsync();
                await Task.Run(delivery.Dispose).WaitAsync(_testTimeout);
                deadline.Fire();
                await stop.WaitAsync(_testTimeout);
                Assert.IsFalse(exporter.Disposed.Task.IsCompleted);
                Assert.AreEqual(0L, delivery.DroppedEvents);
                releaseDisposal.TrySetResult();
                await exporter.Disposed.Task.WaitAsync(_testTimeout);
                Assert.AreEqual(1, exporter.DisposeCalls);
            }
            finally
            {
                releaseDisposal.TrySetResult();
            }
        }

        [TestMethod]
        public async Task DisposeDoesNotJoinBlockingReentrantOrThrowingCancellationCallbacks()
        {
            TaskCompletionSource entered = Signal();
            TaskCompletionSource callbackReentered = Signal();
            TaskCompletionSource releaseCallback = Signal();
            TaskCompletionSource releaseExport = Signal();
            EngineTelemetryDelivery? owner = null;
            FakeExporter exporter = new((_, token) =>
            {
                using CancellationTokenRegistration registration = token.Register(() =>
                {
                    owner!.Dispose();
                    callbackReentered.TrySetResult();
                    Wait(releaseCallback.Task);
                    throw new InvalidOperationException("Synthetic cancellation callback failure.");
                });
                entered.TrySetResult();
                Wait(releaseExport.Task, token);
                return true;
            });
            using EngineTelemetryDelivery delivery = new(() => exporter);
            owner = delivery;

            try
            {
                Assert.IsTrue(delivery.TryEnqueue(CreateEvent()));
                await entered.Task.WaitAsync(_testTimeout);
                await Task.Run(delivery.Dispose).WaitAsync(_testTimeout);
                await callbackReentered.Task.WaitAsync(_testTimeout);
                Assert.IsFalse(exporter.Disposed.Task.IsCompleted);
                releaseCallback.TrySetResult();
                await exporter.Disposed.Task.WaitAsync(_testTimeout);
                await delivery.StopAsync().WaitAsync(_testTimeout);
                Assert.AreEqual(1, exporter.Attempts.Count);
                Assert.AreEqual(1, exporter.DisposeCalls);
                Assert.AreEqual(1L, delivery.DroppedEvents);
            }
            finally
            {
                releaseCallback.TrySetResult();
                releaseExport.TrySetResult();
            }
        }

        [TestMethod]
        public async Task ExporterDisposalExceptionsDoNotFaultStop()
        {
            FakeExporter exporter = new((_, _) => true, () => throw new InvalidOperationException("Synthetic disposal failure."));
            using EngineTelemetryDelivery delivery = new(() => exporter);
            Assert.IsTrue(delivery.TryEnqueue(CreateEvent()));
            await delivery.StopAsync().WaitAsync(_testTimeout);
            await delivery.StopAsync().WaitAsync(_testTimeout);
            delivery.Dispose();
            Assert.AreEqual(1, exporter.DisposeCalls);
            Assert.AreEqual(0L, delivery.DroppedEvents);
        }

        [TestMethod]
        public async Task DroppedEventsSaturatesWhenDiscardingAndRejectingRecords()
        {
            TaskCompletionSource entered = Signal();
            TaskCompletionSource release = Signal();
            FakeExporter exporter = new((_, token) =>
            {
                entered.TrySetResult();
                Wait(release.Task, token);
                return true;
            });
            using EngineTelemetryDelivery delivery = new(() => exporter, capacity: 2);

            try
            {
                Assert.IsTrue(delivery.TryEnqueue(CreateEvent()));
                await entered.Task.WaitAsync(_testTimeout);
                Assert.IsTrue(delivery.TryEnqueue(CreateEvent(2)));
                // Seed only the diagnostic counter, without adding a production test-only API.
                FieldInfo counter = typeof(EngineTelemetryDelivery).GetField("_droppedEvents", BindingFlags.Instance | BindingFlags.NonPublic)!;
                counter.SetValue(delivery, long.MaxValue - 1);
                delivery.Disable();
                Assert.AreEqual(long.MaxValue, delivery.DroppedEvents);
                Assert.IsFalse(delivery.TryEnqueue(CreateEvent()));
                delivery.Disable();
                await exporter.Disposed.Task.WaitAsync(_testTimeout);
                await delivery.StopAsync().WaitAsync(_testTimeout);
                Assert.AreEqual(long.MaxValue, delivery.DroppedEvents);
            }
            finally
            {
                release.TrySetResult();
            }
        }

        private static EngineTelemetryEvent CreateEvent(long sequence = 1) => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            sequence,
            new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero),
            1,
            "engine.synthetic",
            ImmutableDictionary<string, string>.Empty.Add("api", "rest"));

        private static EngineTelemetryDelivery Create(
            Func<IEngineTelemetryExporter> factory,
            ControlledTimeProvider clock,
            int capacity = 256,
            int maxAttempts = 3,
            TimeSpan? flushTimeout = null) => new(factory, capacity, maxAttempts, flushTimeout, clock);

        private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

        private static void Wait(Task task, CancellationToken cancellationToken = default)
            => task.WaitAsync(_testTimeout, cancellationToken).GetAwaiter().GetResult();

        private sealed class FakeExporter : IEngineTelemetryExporter
        {
            private readonly Func<EngineTelemetryEvent, CancellationToken, bool> _export;
            private readonly Action? _dispose;
            private int _activeExports;
            private int _overlappingExports;
            private int _disposeCalls;

            public FakeExporter(Func<EngineTelemetryEvent, CancellationToken, bool> export, Action? dispose = null)
            {
                _export = export;
                _dispose = dispose;
            }

            public ConcurrentQueue<EngineTelemetryEvent> Attempts { get; } = new();
            public TaskCompletionSource Disposed { get; } = Signal();
            public int DisposeCalls => Volatile.Read(ref _disposeCalls);
            public bool HadOverlappingExports => Volatile.Read(ref _overlappingExports) != 0;

            public ValueTask<bool> ExportAsync(EngineTelemetryEvent record, CancellationToken cancellationToken)
            {
                if (Interlocked.Increment(ref _activeExports) > 1)
                {
                    Interlocked.Exchange(ref _overlappingExports, 1);
                }

                try
                {
                    Attempts.Enqueue(record);
                    return ValueTask.FromResult(_export(record, cancellationToken));
                }
                finally
                {
                    Interlocked.Decrement(ref _activeExports);
                }
            }

            public void Dispose()
            {
                Interlocked.Increment(ref _disposeCalls);
                try
                {
                    _dispose?.Invoke();
                }
                finally
                {
                    Disposed.TrySetResult();
                }
            }
        }

        /// <summary>Expose one-shot timers as barriers; tests fire them instead of sleeping.</summary>
        private sealed class ControlledTimeProvider : TimeProvider
        {
            private readonly Channel<ControlledTimer> _timers = Channel.CreateUnbounded<ControlledTimer>(
                new UnboundedChannelOptions { AllowSynchronousContinuations = false });
            private int _createdTimerCount;

            public int CreatedTimerCount => Volatile.Read(ref _createdTimerCount);

            public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            {
                ControlledTimer timer = new(callback, state, dueTime);
                if (period != Timeout.InfiniteTimeSpan)
                {
                    throw new NotSupportedException("These tests use only one-shot timers.");
                }

                Interlocked.Increment(ref _createdTimerCount);
                _timers.Writer.TryWrite(timer);
                return timer;
            }

            public Task<ControlledTimer> NextTimerAsync() => _timers.Reader.ReadAsync().AsTask().WaitAsync(_testTimeout);
        }

        private sealed class ControlledTimer : ITimer
        {
            private readonly object _sync = new();
            private readonly TimerCallback _callback;
            private readonly object? _state;
            private bool _disposed;
            private bool _fired;

            public ControlledTimer(TimerCallback callback, object? state, TimeSpan dueTime)
            {
                _callback = callback;
                _state = state;
                DueTime = dueTime;
            }

            public TimeSpan DueTime { get; private set; }
            public TaskCompletionSource Disposed { get; } = Signal();

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (period != Timeout.InfiniteTimeSpan)
                {
                    throw new NotSupportedException("These tests use only one-shot timers.");
                }

                lock (_sync)
                {
                    if (_disposed)
                    {
                        return false;
                    }

                    DueTime = dueTime;
                    _fired = false;
                    return true;
                }
            }

            public void Fire()
            {
                lock (_sync)
                {
                    if (_disposed || _fired)
                    {
                        return;
                    }

                    _fired = true;
                }

                _callback(_state);
            }

            public void Dispose()
            {
                lock (_sync)
                {
                    _disposed = true;
                }

                Disposed.TrySetResult();
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
