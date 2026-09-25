// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;

namespace Azure.DataApiBuilder.Core.Telemetry.Product
{
    /// <summary>
    /// Bounded, memory-only aggregation isolated from customer diagnostics. No sender, identity
    /// storage, timers, or process-global telemetry providers are created here. All mutation and
    /// window sealing share one short critical section; no external callbacks run inside it,
    /// except the injected local clock. Disabled instances do not allocate aggregation state.
    /// </summary>
    internal sealed class EngineTelemetryAggregator
    {
        private static readonly TimeSpan _windowLength = TimeSpan.FromHours(6);
        private readonly object _sync = new();
        private State? _state;

        private EngineTelemetryAggregator(State? state)
        {
            _state = state;
        }

        public bool IsEnabled
        {
            get
            {
                lock (_sync)
                {
                    return _state is not null;
                }
            }
        }

        /// <summary>
        /// Default-off even in Release builds. The only enabling path is explicit internal
        /// synthetic validation. Read the umbrella veto before clocks, counters, or identity work.
        /// Environment changes do not change an already-created instance; use Disable instead.
        /// </summary>
        public static EngineTelemetryAggregator Create(
            EngineTelemetryOptions? options = null,
            TimeProvider? timeProvider = null,
            Func<string, string?>? readEnvironmentVariable = null)
        {
            if (options?.EnableSyntheticCollection != true)
            {
                return new(null);
            }

            readEnvironmentVariable ??= Environment.GetEnvironmentVariable;
            if (EngineTelemetryOptions.IsOptedOut(readEnvironmentVariable(EngineTelemetryOptions.OPT_OUT_ENVIRONMENT_VARIABLE)))
            {
                return new(null);
            }

            options.Validate();
            return new(new State(options, timeProvider ?? TimeProvider.System));
        }

        /// <summary>
        /// Call only after accepting a configuration, never on a rejected reload. This does not
        /// imply serving readiness, emit a snapshot, flush counters, or relabel existing work.
        /// </summary>
        public EngineTelemetryConfiguration AcceptConfiguration()
        {
            lock (_sync)
            {
                if (_state is null || _state.Epoch == long.MaxValue)
                {
                    return default;
                }

                return new(_state.Owner, ++_state.Epoch);
            }
        }

        public void RecordRequest(EngineTelemetryConfiguration configuration, EngineTelemetryApi api, EngineTelemetryTransport transport,
            EngineTelemetryRole role, EngineTelemetryOutcome outcome, TimeSpan? duration)
            => Record(configuration, EngineTelemetryDimensions.ForRequest(api, transport, role), outcome, duration);

        public void RecordOperation(EngineTelemetryConfiguration configuration, EngineTelemetryApi api, EngineTelemetryOperation operation,
            EngineTelemetryProvider provider, EngineTelemetryObject objectType, EngineTelemetryOutcome outcome)
            => Record(configuration, EngineTelemetryDimensions.ForOperation(api, operation, provider, objectType), outcome);

        public void RecordDatabaseAttempt(EngineTelemetryConfiguration configuration, EngineTelemetryProvider provider, EngineTelemetryOutcome outcome)
            => Record(configuration, EngineTelemetryDimensions.ForDatabaseAttempt(provider), outcome);

        public void RecordCacheLookup(EngineTelemetryConfiguration configuration, EngineTelemetryCacheLayer layer, EngineTelemetryCacheResult result)
            => Record(configuration, EngineTelemetryDimensions.ForCacheLookup(layer, result), EngineTelemetryOutcome.Unknown);

        public void RecordEmbedding(EngineTelemetryConfiguration configuration, EngineTelemetryApi api, EngineTelemetryOutcome outcome)
            => Record(configuration, EngineTelemetryDimensions.ForEmbedding(api), outcome);

        public void RecordHttpOutcome(EngineTelemetryConfiguration configuration, EngineTelemetryApi api, int? status)
            => Record(configuration, EngineTelemetryDimensions.ForHttpOutcome(api, status), EngineTelemetryOutcome.Unknown);

        /// <summary>Detach completed windows once; the still-open window is not flushed.</summary>
        public EngineTelemetryDrain DrainCompletedWindows()
        {
            lock (_sync)
            {
                if (_state is null)
                {
                    return EngineTelemetryDrain.Empty;
                }

                AdvanceWindow(_state, _state.Clock.GetUtcNow().ToUniversalTime());
                return Drain(_state);
            }
        }

        /// <summary>
        /// Seal the remaining delta once and stop recording. The caller, not request processing,
        /// owns any later bounded shutdown delivery. This method never performs network I/O.
        /// </summary>
        public EngineTelemetryDrain Complete()
        {
            lock (_sync)
            {
                if (_state is null)
                {
                    return EngineTelemetryDrain.Empty;
                }

                State state = _state;
                DateTimeOffset now = state.Clock.GetUtcNow().ToUniversalTime();
                AdvanceWindow(state, now);
                SealWindow(state, now > state.LastObservation ? now : state.LastObservation, isFinal: true);
                _state = null;
                return Drain(state);
            }
        }

        /// <summary>Stop recording and discard all owned data, without producing a final delta.</summary>
        public void Disable()
        {
            lock (_sync)
            {
                _state = null;
            }
        }

        private void Record(EngineTelemetryConfiguration configuration, EngineTelemetryDimensions dimensions, EngineTelemetryOutcome outcome, TimeSpan? duration = null)
        {
            lock (_sync)
            {
                State? state = _state;
                if (state is null || !ReferenceEquals(configuration.Owner, state.Owner) || configuration.Epoch <= 0 || configuration.Epoch > state.Epoch)
                {
                    return;
                }

                DateTimeOffset now = state.Clock.GetUtcNow().ToUniversalTime();
                if (now < state.LastObservation)
                {
                    // Never reopen a sealed calendar segment or silently move old-time work into
                    // a later segment after a wall-clock correction. Report the local loss.
                    AddLoss(ref state.ClockRegressionDrops, 1, ref state.WindowLossCountsCapped);
                    return;
                }

                AdvanceWindow(state, now);
                state.LastObservation = now;
                SeriesKey key = new(configuration.Epoch, dimensions);
                if (!state.Series.TryGetValue(key, out Counters? counters))
                {
                    if (state.Series.Count == state.Options.SeriesCapacity)
                    {
                        AddLoss(ref state.SeriesCapacityDrops, 1, ref state.WindowLossCountsCapped);
                        return;
                    }

                    counters = new Counters(dimensions.Measurement);
                    state.Series.Add(key, counters);
                }

                if (!counters.TryRecord(EngineTelemetryDimensions.Normalize(outcome), duration, state.Options.CounterCeiling))
                {
                    AddLoss(ref state.CounterCapacityDrops, 1, ref state.WindowLossCountsCapped);
                }
            }
        }

        private static void AdvanceWindow(State state, DateTimeOffset now)
        {
            if (now < state.WindowEnd)
            {
                return;
            }

            SealWindow(state, state.WindowEnd, isFinal: false);
            // Skip empty elapsed windows in O(1), even after a long suspension or clock jump.
            state.WindowStart = FloorToWindow(now);
            state.WindowEnd = state.WindowStart + _windowLength;
            state.LastObservation = state.WindowStart;
        }

        private static DateTimeOffset FloorToWindow(DateTimeOffset now)
            => new(now.UtcTicks - (now.UtcTicks % _windowLength.Ticks), TimeSpan.Zero);

        private static void SealWindow(State state, DateTimeOffset end, bool isFinal)
        {
            if (state.Series.Count == 0 && state.SeriesCapacityDrops == 0 && state.CounterCapacityDrops == 0 && state.ClockRegressionDrops == 0)
            {
                return;
            }

            if (state.Pending.Count < state.Options.PendingWindowCapacity)
            {
                ImmutableArray<EngineTelemetrySeries>.Builder series = ImmutableArray.CreateBuilder<EngineTelemetrySeries>(state.Series.Count);
                foreach ((SeriesKey key, Counters counters) in state.Series)
                {
                    series.Add(counters.Snapshot(key));
                }

                state.Pending.Enqueue(new(state.WindowStart, end, isFinal, series.MoveToImmutable(),
                    state.SeriesCapacityDrops, state.CounterCapacityDrops, state.ClockRegressionDrops, state.WindowLossCountsCapped));
            }
            else
            {
                // Do not duplicate a delta later to compensate for loss. The delivery layer can
                // retry the same immutable result, but collection always starts a new segment.
                AddLoss(ref state.DroppedWindows, 1, ref state.DrainLossCountsCapped);
                foreach (Counters counters in state.Series.Values)
                {
                    AddLoss(ref state.DroppedMeasurements, counters.Count, ref state.DrainLossCountsCapped);
                }

                // Preserve measurable losses even when their window cannot be queued. These
                // observations were not included in any of the recorded series counts above.
                AddLoss(ref state.DroppedMeasurements, state.SeriesCapacityDrops, ref state.DrainLossCountsCapped);
                AddLoss(ref state.DroppedMeasurements, state.CounterCapacityDrops, ref state.DrainLossCountsCapped);
                AddLoss(ref state.DroppedMeasurements, state.ClockRegressionDrops, ref state.DrainLossCountsCapped);
                state.DrainLossCountsCapped |= state.WindowLossCountsCapped;
            }

            state.Series.Clear();
            state.SeriesCapacityDrops = 0;
            state.CounterCapacityDrops = 0;
            state.ClockRegressionDrops = 0;
            state.WindowLossCountsCapped = false;
        }

        private static EngineTelemetryDrain Drain(State state)
        {
            if (state.Pending.Count == 0 && state.DroppedWindows == 0)
            {
                return EngineTelemetryDrain.Empty;
            }

            EngineTelemetryDrain result = new(state.Pending.ToImmutableArray(), state.DroppedWindows, state.DroppedMeasurements, state.DrainLossCountsCapped);
            state.Pending.Clear();
            state.DroppedWindows = 0;
            state.DroppedMeasurements = 0;
            state.DrainLossCountsCapped = false;
            return result;
        }

        private static void AddLoss(ref long count, long increment, ref bool capped)
        {
            if (increment > long.MaxValue - count)
            {
                count = long.MaxValue;
                capped = true;
            }
            else
            {
                count += increment;
            }
        }

        private readonly record struct SeriesKey(long Epoch, EngineTelemetryDimensions Dimensions);

        private sealed class State
        {
            public State(EngineTelemetryOptions options, TimeProvider clock)
            {
                Options = options;
                Clock = clock;
                WindowStart = clock.GetUtcNow().ToUniversalTime();
                LastObservation = WindowStart;
                WindowEnd = FloorToWindow(WindowStart) + _windowLength;
            }

            public EngineTelemetryOptions Options { get; }
            public TimeProvider Clock { get; }
            public object Owner { get; } = new();
            public Dictionary<SeriesKey, Counters> Series { get; } = new();
            public Queue<EngineTelemetryWindow> Pending { get; } = new();
            public long Epoch { get; set; }
            public DateTimeOffset WindowStart { get; set; }
            public DateTimeOffset WindowEnd { get; set; }
            public DateTimeOffset LastObservation { get; set; }
            public long SeriesCapacityDrops;
            public long CounterCapacityDrops;
            public long ClockRegressionDrops;
            public bool WindowLossCountsCapped;
            public long DroppedWindows;
            public long DroppedMeasurements;
            public bool DrainLossCountsCapped;
        }

        private sealed class Counters
        {
            private readonly long[]? _outcomes;
            private readonly long[]? _latency;
            private long _timedCount;
            private bool _capped;

            public Counters(EngineTelemetryMeasurement measurement)
            {
                if (measurement is not (EngineTelemetryMeasurement.CacheLookup or EngineTelemetryMeasurement.HttpOutcome))
                {
                    _outcomes = new long[5];
                }

                if (measurement == EngineTelemetryMeasurement.Request)
                {
                    _latency = new long[EngineTelemetryHistogram.UpperBoundsMilliseconds.Length + 1];
                }
            }

            public long Count { get; private set; }

            public bool TryRecord(EngineTelemetryOutcome outcome, TimeSpan? duration, long ceiling)
            {
                if (Count == ceiling)
                {
                    _capped = true;
                    return false;
                }

                Count++;
                if (_outcomes is not null)
                {
                    _outcomes[(int)outcome]++;
                }

                if (_latency is not null && duration.HasValue && duration.Value >= TimeSpan.Zero)
                {
                    int bucket = 0;
                    while (bucket < EngineTelemetryHistogram.UpperBoundsMilliseconds.Length
                        && duration.Value.Ticks > EngineTelemetryHistogram.UpperBoundsMilliseconds[bucket] * TimeSpan.TicksPerMillisecond)
                    {
                        bucket++;
                    }

                    _latency[bucket]++;
                    _timedCount++;
                }

                return true;
            }

            public EngineTelemetrySeries Snapshot(SeriesKey key)
            {
                EngineTelemetryOutcomeCounts? outcomes = _outcomes is null ? null : new(_outcomes[0], _outcomes[1], _outcomes[2], _outcomes[3], _outcomes[4]);
                EngineTelemetryHistogram? latency = _latency is null ? null : new(_latency.ToImmutableArray(), _timedCount, _timedCount == Count && !_capped);
                return new(key.Epoch, key.Dimensions, Count, outcomes, latency, _capped);
            }
        }
    }
}
