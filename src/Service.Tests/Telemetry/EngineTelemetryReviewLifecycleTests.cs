// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Telemetry
{
    /// <summary>
    /// Exercises synchronous callers of the real session and delivery shutdown. Export is
    /// memory-only, timers are manually fired, and the synchronization context belongs only
    /// to the test's background thread. No process environment or console state is changed.
    /// </summary>
    [TestClass]
    [TestCategory("EngineTelemetry")]
    public class EngineTelemetryReviewLifecycleTests
    {
        private static readonly TimeSpan _testTimeout = TimeSpan.FromSeconds(10);

        [DataTestMethod]
        [DataRow("success", false)]
        [DataRow("success", true)]
        [DataRow("deadline", false)]
        [DataRow("deadline", true)]
        [DataRow("cancellation", true)]
        public async Task SynchronousSessionStopCompletesWithoutPostingToCallerContext(string completion, bool cancelable)
        {
            GatedExporter exporter = new();
            ShutdownClock clock = new();
            NonPumpingSynchronizationContext context = new();
            using CancellationTokenSource cancellation = new();
            using EngineTelemetrySession session = EngineTelemetrySession.Create(
                exporterFactory: () => exporter,
                enableSyntheticCollection: true,
                readEnvironmentVariable: _ => null,
                showNotice: () => { },
                resolveIdentity: _ => throw new AssertFailedException("Shutdown without configuration must not resolve an identity."),
                startTimer: false);
            EngineTelemetryDelivery? delivery = null;
            TaskCompletionSource stopReturned = Signal();
            TaskCompletionSource threadFinished = Signal();
            Task? stop = null;
            Exception? stopFailure = null;
            bool threadStarted = false;
            Thread thread = new(() =>
            {
                SynchronizationContext? previous = SynchronizationContext.Current;
                try
                {
                    SynchronizationContext.SetSynchronizationContext(context);
                    stop = cancelable ? session.StopAsync(cancellation.Token) : session.StopAsync();
                    stopReturned.TrySetResult();

                    // Intentionally match the synchronous shutdown boundary in Program.
                    // Do not wrap StopAsync in Task.Run or clear the caller's context.
                    stop.GetAwaiter().GetResult();
                }
                catch (Exception exception)
                {
                    stopFailure = exception;
                }
                finally
                {
                    SynchronizationContext.SetSynchronizationContext(previous);
                    stopReturned.TrySetResult();
                    threadFinished.TrySetResult();
                }
            })
            {
                IsBackground = true,
                Name = "Engine telemetry synchronous shutdown regression"
            };

            try
            {
                Assert.IsTrue(session.IsEnabled);
                CancellationToken exportCancellation = await exporter.Entered.Task.WaitAsync(_testTimeout);

                // Session intentionally has no delivery-clock option. Change only the clock
                // on its actual delivery before the first StopAsync, while export is held.
                // Neither the delivery instance, worker, queued events nor stop task is replaced.
                FieldInfo? deliveryField = typeof(EngineTelemetrySession).GetField("_delivery", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(deliveryField);
                delivery = deliveryField.GetValue(session) as EngineTelemetryDelivery;
                Assert.IsNotNull(delivery);
                FieldInfo? clockField = typeof(EngineTelemetryDelivery).GetField("_timeProvider", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(clockField);
                clockField.SetValue(delivery, clock);

                thread.Start();
                threadStarted = true;
                await stopReturned.Task.WaitAsync(_testTimeout);
                Assert.IsNull(stopFailure, "Calling StopAsync must not fault the host.");
                Assert.IsNotNull(stop);
                Assert.IsFalse(stop.IsCompleted, "A pending export must force the asynchronous shutdown path.");
                ShutdownTimer deadline = await clock.TimerCreated.Task.WaitAsync(_testTimeout);
                Assert.AreSame(context, deadline.CallerContext, "The real delivery wait must be created on the non-pumping caller thread.");
                Assert.AreEqual(TimeSpan.FromSeconds(2), deadline.DueTime);
                Assert.AreEqual(Timeout.InfiniteTimeSpan, deadline.Period);
                Assert.AreEqual(1, clock.CreatedTimerCount);

                switch (completion)
                {
                    case "success":
                        exporter.Release.TrySetResult();
                        break;
                    case "deadline":
                        deadline.Fire();
                        break;
                    case "cancellation":
                        cancellation.Cancel();
                        break;
                    default:
                        throw new AssertFailedException("Unknown shutdown completion mode.");
                }

                // A captured continuation reports a failure immediately instead of waiting
                // for the outer bound. Nothing pumps this context during the assertions.
                Task completed = await Task.WhenAny(threadFinished.Task, context.Posted.Task).WaitAsync(_testTimeout);
                Assert.AreSame(threadFinished.Task, completed, "Shutdown posted a continuation to the blocked caller's context.");
                await threadFinished.Task.WaitAsync(_testTimeout);
                Assert.IsNull(stopFailure, "Synchronous shutdown must complete without an application exception.");
                Assert.IsTrue(stop.IsCompletedSuccessfully);
                Assert.AreEqual(0, context.PostCalls);
                Assert.IsFalse(session.IsEnabled);

                if (completion == "success")
                {
                    await exporter.Disposed.Task.WaitAsync(_testTimeout);
                    CollectionAssert.AreEqual(new[] { "dab/engine.process_started", "dab/engine.stopped" },
                        exporter.Records.Select(record => record.Name).ToArray());
                    Assert.AreEqual(2, exporter.Attempts.Count);
                    Assert.AreEqual(0L, delivery.DroppedEvents);
                    Assert.IsFalse(exportCancellation.IsCancellationRequested);
                }
                else
                {
                    Assert.IsTrue(exportCancellation.IsCancellationRequested);
                    Assert.IsFalse(exporter.Release.Task.IsCompleted, "Only deadline/caller cancellation may complete this stop.");
                    Assert.IsFalse(exporter.Disposed.Task.IsCompleted, "Shutdown must not join an exporter still held by the test.");
                    Assert.AreEqual(1, exporter.Attempts.Count);
                    Assert.AreEqual("dab/engine.process_started", exporter.Attempts.Single().Name);
                    Assert.AreEqual(0, exporter.Records.Count);
                    Assert.AreEqual(2L, delivery.DroppedEvents, "Both the in-flight start and queued stop record are discarded.");
                }

                Assert.AreEqual(completion == "deadline", deadline.WasFired,
                    "Success and caller cancellation must not depend on the graceful-drain deadline.");
            }
            finally
            {
                // Even a missing ConfigureAwait(false) must not leak a blocked thread. Only
                // after verification ends, dispatch any captured continuations for cleanup.
                context.ReleaseForCleanup();
                session.Disable();
                exporter.Release.TrySetResult();
                if (threadStarted)
                {
                    await threadFinished.Task.WaitAsync(_testTimeout);
                    Assert.IsTrue(thread.Join(_testTimeout), "The owned shutdown thread did not exit during cleanup.");
                }

                if (delivery is not null)
                {
                    await delivery.StopAsync().WaitAsync(_testTimeout);
                }

                if (exporter.Entered.Task.IsCompletedSuccessfully)
                {
                    await exporter.Disposed.Task.WaitAsync(_testTimeout);
                    Assert.AreEqual(1, exporter.DisposeCalls);
                }
            }
        }

        private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

        private sealed class GatedExporter : IEngineTelemetryExporter
        {
            private int _disposeCalls;

            public TaskCompletionSource<CancellationToken> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource Release { get; } = Signal();
            public TaskCompletionSource Disposed { get; } = Signal();
            public ConcurrentQueue<EngineTelemetryEvent> Attempts { get; } = new();
            public ConcurrentQueue<EngineTelemetryEvent> Records { get; } = new();
            public int DisposeCalls => Volatile.Read(ref _disposeCalls);

            public async ValueTask<bool> ExportAsync(EngineTelemetryEvent record, CancellationToken cancellationToken)
            {
                Attempts.Enqueue(record);
                Entered.TrySetResult(cancellationToken);
                // Deliberately stay pending after cancellation until cleanup releases the
                // gate. This proves stop is bounded independently of exporter cooperation.
                await Release.Task.ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                Records.Enqueue(record);
                return true;
            }

            public void Dispose()
            {
                Interlocked.Increment(ref _disposeCalls);
                Disposed.TrySetResult();
            }
        }

        private sealed class NonPumpingSynchronizationContext : SynchronizationContext
        {
            private readonly object _sync = new();
            private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _pending = new();
            private bool _cleanup;
            private int _postCalls;

            public TaskCompletionSource Posted { get; } = Signal();
            public int PostCalls => Volatile.Read(ref _postCalls);

            public override SynchronizationContext CreateCopy() => this;

            public override void Post(SendOrPostCallback callback, object? state)
            {
                Interlocked.Increment(ref _postCalls);
                Posted.TrySetResult();
                lock (_sync)
                {
                    if (!_cleanup)
                    {
                        _pending.Enqueue((callback, state));
                        return;
                    }
                }

                QueueForCleanup(callback, state);
            }

            public void ReleaseForCleanup()
            {
                lock (_sync)
                {
                    _cleanup = true;
                    while (_pending.TryDequeue(out (SendOrPostCallback Callback, object? State) work))
                    {
                        QueueForCleanup(work.Callback, work.State);
                    }
                }
            }

            private static void QueueForCleanup(SendOrPostCallback callback, object? state)
                => ThreadPool.QueueUserWorkItem(_ => callback(state));
        }

        private sealed class ShutdownClock : TimeProvider
        {
            private int _createdTimerCount;

            public TaskCompletionSource<ShutdownTimer> TimerCreated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public int CreatedTimerCount => Volatile.Read(ref _createdTimerCount);

            public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            {
                ShutdownTimer timer = new(callback, state, dueTime, period, SynchronizationContext.Current);
                Interlocked.Increment(ref _createdTimerCount);
                TimerCreated.TrySetResult(timer);
                return timer;
            }
        }

        private sealed class ShutdownTimer : ITimer
        {
            private readonly object _sync = new();
            private readonly TimerCallback _callback;
            private readonly object? _state;
            private bool _disposed;
            private bool _fired;

            public ShutdownTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period, SynchronizationContext? callerContext)
            {
                _callback = callback;
                _state = state;
                DueTime = dueTime;
                Period = period;
                CallerContext = callerContext;
            }

            public TimeSpan DueTime { get; private set; }
            public TimeSpan Period { get; private set; }
            public SynchronizationContext? CallerContext { get; }
            public bool WasFired
            {
                get
                {
                    lock (_sync)
                    {
                        return _fired;
                    }
                }
            }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (_sync)
                {
                    if (_disposed)
                    {
                        return false;
                    }

                    DueTime = dueTime;
                    Period = period;
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
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
