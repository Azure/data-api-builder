// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Telemetry
{
    [TestClass]
    [TestCategory("EngineTelemetry")]
    public class EngineTelemetryAggregatorTests
    {
        private static readonly DateTimeOffset _start = new(2026, 9, 21, 5, 59, 0, TimeSpan.Zero);

        [TestMethod]
        public void DefaultOffDoesNotInitializeClockOrReadEnvironment()
        {
            EngineTelemetryAggregator aggregator = EngineTelemetryAggregator.Create(
                timeProvider: new ThrowingTimeProvider(),
                readEnvironmentVariable: _ => throw new InvalidOperationException("Disabled collection must not initialize dependencies."));

            AssertDisabled(aggregator);
        }

        [DataTestMethod]
        [DataRow("1")]
        [DataRow("true")]
        [DataRow("TRUE")]
        [DataRow(" true ")]
        [DataRow(" 1 ")]
        public void UmbrellaOptOutVetoesSyntheticCollectionBeforeStateAllocation(string optOut)
        {
            int environmentReads = 0;
            EngineTelemetryAggregator aggregator = EngineTelemetryAggregator.Create(
                new() { EnableSyntheticCollection = true, SeriesCapacity = 0 },
                new ThrowingTimeProvider(),
                variable =>
                {
                    Assert.AreEqual(EngineTelemetryOptions.OPT_OUT_ENVIRONMENT_VARIABLE, variable);
                    environmentReads++;
                    return optOut;
                });

            Assert.AreEqual(1, environmentReads);
            AssertDisabled(aggregator);
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("0")]
        [DataRow("false")]
        [DataRow("invalid")]
        public void NonOptOutValuesStillRequireExplicitSyntheticEnablement(string? value)
        {
            ManualTimeProvider clock = new(_start);
            EngineTelemetryAggregator disabled = EngineTelemetryAggregator.Create(new(), clock, _ => value);
            AssertDisabled(disabled);

            EngineTelemetryAggregator enabled = EngineTelemetryAggregator.Create(new() { EnableSyntheticCollection = true }, clock, _ => value);
            Assert.IsTrue(enabled.IsEnabled);
            Assert.IsTrue(enabled.Complete().IsSynthetic);
        }

        [TestMethod]
        public void EnvironmentIsReadOnceAndExplicitDisableDiscardsPendingAndCurrentData()
        {
            string? optOut = null;
            int reads = 0;
            ManualTimeProvider clock = new(_start);
            EngineTelemetryAggregator aggregator = EngineTelemetryAggregator.Create(new() { EnableSyntheticCollection = true }, clock,
                _ => { reads++; return optOut; });
            EngineTelemetryConfiguration configuration = aggregator.AcceptConfiguration();

            RecordRequest(aggregator, configuration);
            clock.Advance(TimeSpan.FromMinutes(2));
            // Setting an environment variable is not a supported in-process control.
            optOut = "true";
            RecordRequest(aggregator, configuration);
            Assert.IsTrue(aggregator.IsEnabled);
            Assert.AreEqual(1, reads);

            aggregator.Disable();
            RecordRequest(aggregator, configuration);
            AssertDisabled(aggregator);
            Assert.AreEqual(1, reads);
        }

        [TestMethod]
        public void CachedReadCountsOneRequestOneOperationAndNoDatabaseAttempts()
        {
            EngineTelemetryAggregator aggregator = Create(out _);
            EngineTelemetryConfiguration configuration = aggregator.AcceptConfiguration();

            aggregator.RecordCacheLookup(configuration, EngineTelemetryCacheLayer.Level1, EngineTelemetryCacheResult.Hit);
            RecordRead(aggregator, configuration);
            RecordRequest(aggregator, configuration);

            EngineTelemetryWindow window = aggregator.Complete().Windows.Single();
            Assert.AreEqual(1L, Total(window, EngineTelemetryMeasurement.Request));
            Assert.AreEqual(1L, Total(window, EngineTelemetryMeasurement.Operation));
            Assert.AreEqual(0L, Total(window, EngineTelemetryMeasurement.DatabaseAttempt));
            EngineTelemetrySeries cache = window.Series.Single(s => s.Dimensions.Measurement == EngineTelemetryMeasurement.CacheLookup);
            Assert.AreEqual(1L, cache.Count);
            Assert.AreEqual(EngineTelemetryCacheResult.Hit, cache.Dimensions.CacheResult);
            Assert.IsNull(cache.Outcomes, "A cache hit/miss is not a request success/failure.");
            Assert.IsNull(cache.Latency);
        }

        [TestMethod]
        public void RetryIncreasesAttemptsButNotOperationsOrRequests()
        {
            EngineTelemetryAggregator aggregator = Create(out _);
            EngineTelemetryConfiguration configuration = aggregator.AcceptConfiguration();

            aggregator.RecordDatabaseAttempt(configuration, EngineTelemetryProvider.MsSql, EngineTelemetryOutcome.Failure);
            aggregator.RecordDatabaseAttempt(configuration, EngineTelemetryProvider.MsSql, EngineTelemetryOutcome.Success);
            RecordRead(aggregator, configuration);
            RecordRequest(aggregator, configuration);

            EngineTelemetryWindow window = aggregator.Complete().Windows.Single();
            Assert.AreEqual(1L, Total(window, EngineTelemetryMeasurement.Request));
            Assert.AreEqual(1L, Total(window, EngineTelemetryMeasurement.Operation));
            EngineTelemetrySeries attempts = window.Series.Single(s => s.Dimensions.Measurement == EngineTelemetryMeasurement.DatabaseAttempt);
            Assert.AreEqual(2L, attempts.Count);
            Assert.AreEqual(1L, attempts.Outcomes!.Failure);
            Assert.AreEqual(1L, attempts.Outcomes.Success);
        }

        [TestMethod]
        public void MultipleOperationsAndCacheLayersRemainIndependent()
        {
            EngineTelemetryAggregator aggregator = Create(out _);
            EngineTelemetryConfiguration configuration = aggregator.AcceptConfiguration();
            RecordRead(aggregator, configuration);
            RecordRead(aggregator, configuration);
            aggregator.RecordCacheLookup(configuration, EngineTelemetryCacheLayer.Level1, EngineTelemetryCacheResult.Miss);
            aggregator.RecordCacheLookup(configuration, EngineTelemetryCacheLayer.Level2, EngineTelemetryCacheResult.Hit);
            aggregator.RecordEmbedding(configuration, EngineTelemetryApi.Mcp, EngineTelemetryOutcome.Success);
            RecordRequest(aggregator, configuration);

            EngineTelemetryWindow window = aggregator.Complete().Windows.Single();
            Assert.AreEqual(1L, Total(window, EngineTelemetryMeasurement.Request));
            Assert.AreEqual(2L, Total(window, EngineTelemetryMeasurement.Operation));
            Assert.AreEqual(2, window.Series.Count(s => s.Dimensions.Measurement == EngineTelemetryMeasurement.CacheLookup));
            Assert.AreEqual(1L, Total(window, EngineTelemetryMeasurement.Embedding));
        }

        [TestMethod]
        public void AllFiveLogicalOutcomesPartitionRequestCounts()
        {
            EngineTelemetryAggregator aggregator = Create(out _);
            EngineTelemetryConfiguration configuration = aggregator.AcceptConfiguration();
            foreach (EngineTelemetryOutcome outcome in Enum.GetValues<EngineTelemetryOutcome>())
            {
                RecordRequest(aggregator, configuration, outcome);
            }

            EngineTelemetrySeries series = aggregator.Complete().Windows.Single().Series.Single();
            Assert.AreEqual(5L, series.Count);
            Assert.AreEqual(new EngineTelemetryOutcomeCounts(1, 1, 1, 1, 1), series.Outcomes);
            Assert.AreEqual(series.Count, SumOutcomes(series));
        }

        [TestMethod]
        public void ApiTransportAndRoleKeepRequiredJointCountsWithoutRawNames()
        {
            EngineTelemetryAggregator aggregator = Create(out _);
            EngineTelemetryConfiguration configuration = aggregator.AcceptConfiguration();
            RecordRequest(aggregator, configuration);
            aggregator.RecordRequest(configuration, EngineTelemetryApi.GraphQL, EngineTelemetryTransport.Http,
                EngineTelemetryRole.Custom, EngineTelemetryOutcome.PartialFailure, TimeSpan.FromMilliseconds(5));
            aggregator.RecordRequest(configuration, EngineTelemetryApi.Mcp, EngineTelemetryTransport.Stdio,
                EngineTelemetryRole.Authenticated, EngineTelemetryOutcome.Failure, TimeSpan.FromMilliseconds(8));

            EngineTelemetryWindow window = aggregator.Complete().Windows.Single();
            Assert.AreEqual(3, window.Series.Length);
            Assert.AreEqual(1L, window.Series.Single(s => s.Dimensions.Api == EngineTelemetryApi.GraphQL).Outcomes!.PartialFailure);
            EngineTelemetrySeries stdio = window.Series.Single(s => s.Dimensions.Transport == EngineTelemetryTransport.Stdio);
            Assert.AreEqual(EngineTelemetryApi.Mcp, stdio.Dimensions.Api);
            Assert.AreEqual(1L, stdio.Outcomes!.Failure);
        }

        [TestMethod]
        public void UnknownEnumValuesCannotCreateUnboundedCategoricalDimensions()
        {
            EngineTelemetryAggregator aggregator = Create(out _);
            EngineTelemetryConfiguration configuration = aggregator.AcceptConfiguration();
            for (int value = 100; value < 500; value++)
            {
                aggregator.RecordRequest(configuration, (EngineTelemetryApi)value, (EngineTelemetryTransport)value,
                    (EngineTelemetryRole)value, (EngineTelemetryOutcome)value, TimeSpan.Zero);
            }

            EngineTelemetrySeries series = aggregator.Complete().Windows.Single().Series.Single();
            Assert.AreEqual(400L, series.Count);
            Assert.AreEqual(EngineTelemetryApi.Unknown, series.Dimensions.Api);
            Assert.AreEqual(400L, series.Outcomes!.Unknown);
        }

        [TestMethod]
        public void ReloadRetainsCapturedEpochAndDoesNotFlushTheOpenWindow()
        {
            EngineTelemetryAggregator aggregator = Create(out _);
            EngineTelemetryConfiguration oldConfiguration = aggregator.AcceptConfiguration();
            EngineTelemetryConfiguration newConfiguration = aggregator.AcceptConfiguration();
            Assert.AreEqual(1L, oldConfiguration.Epoch);
            Assert.AreEqual(2L, newConfiguration.Epoch);

            // Work captured before the reload completes after new-configuration work.
            RecordRequest(aggregator, newConfiguration);
            RecordRequest(aggregator, oldConfiguration);
            Assert.AreEqual(0, aggregator.DrainCompletedWindows().Windows.Length);

            EngineTelemetryWindow window = aggregator.Complete().Windows.Single();
            CollectionAssert.AreEquivalent(new long[] { 1, 2 }, window.Series.Select(s => s.ConfigurationEpoch).ToArray());
            Assert.IsTrue(window.Series.All(s => s.Count == 1));
        }

        [TestMethod]
        public void LateOldEpochCompletionUsesNewTimeWindowWithoutReopeningSealedData()
        {
            EngineTelemetryAggregator aggregator = Create(out ManualTimeProvider clock);
            EngineTelemetryConfiguration oldConfiguration = aggregator.AcceptConfiguration();
            RecordRequest(aggregator, oldConfiguration);

            clock.Advance(TimeSpan.FromMinutes(1));
            EngineTelemetryWindow first = aggregator.DrainCompletedWindows().Windows.Single();
            EngineTelemetryConfiguration newConfiguration = aggregator.AcceptConfiguration();
            RecordRequest(aggregator, newConfiguration);
            RecordRequest(aggregator, oldConfiguration);
            clock.Advance(TimeSpan.FromSeconds(1));
            EngineTelemetryWindow final = aggregator.Complete().Windows.Single();

            Assert.AreEqual(_start, first.Start);
            Assert.AreEqual(new DateTimeOffset(2026, 9, 21, 6, 0, 0, TimeSpan.Zero), first.End);
            Assert.AreEqual(first.End, final.Start);
            Assert.AreEqual(1L, first.Series.Single().Count);
            Assert.AreEqual(1L, first.Series.Single().ConfigurationEpoch);
            Assert.AreEqual(2L, Total(final, EngineTelemetryMeasurement.Request));
            Assert.IsTrue(final.Series.Any(s => s.ConfigurationEpoch == oldConfiguration.Epoch));
            Assert.IsFalse(first.IsFinal);
            Assert.IsTrue(final.IsFinal);
        }

        [TestMethod]
        public void CompletionExactlyOnBoundaryBelongsToNextWindow()
        {
            EngineTelemetryAggregator aggregator = Create(out ManualTimeProvider clock);
            EngineTelemetryConfiguration configuration = aggregator.AcceptConfiguration();
            clock.Advance(TimeSpan.FromMinutes(1));
            RecordRequest(aggregator, configuration);
            Assert.AreEqual(0, aggregator.DrainCompletedWindows().Windows.Length, "No request completed in the previous window.");
            clock.Advance(TimeSpan.FromHours(6));
            EngineTelemetryWindow window = aggregator.DrainCompletedWindows().Windows.Single();
            Assert.AreEqual(6, window.Start.Hour);
            Assert.AreEqual(12, window.End.Hour);
            Assert.AreEqual(1L, Total(window, EngineTelemetryMeasurement.Request));
        }

        [TestMethod]
        public void MidnightBoundaryAndFinalShutdownProduceNonoverlappingDeltas()
        {
            ManualTimeProvider clock = new(new DateTimeOffset(2026, 9, 21, 23, 59, 59, TimeSpan.Zero));
            EngineTelemetryAggregator aggregator = EngineTelemetryAggregator.Create(new() { EnableSyntheticCollection = true }, clock, _ => null);
            EngineTelemetryConfiguration configuration = aggregator.AcceptConfiguration();
            RecordRequest(aggregator, configuration);
            clock.Advance(TimeSpan.FromSeconds(1));
            RecordRequest(aggregator, configuration);

            EngineTelemetryWindow previousDay = aggregator.DrainCompletedWindows().Windows.Single();
            Assert.AreEqual(0, aggregator.DrainCompletedWindows().Windows.Length);
            clock.Advance(TimeSpan.FromSeconds(10));
            EngineTelemetryWindow currentDay = aggregator.Complete().Windows.Single();
            Assert.AreEqual(previousDay.End, currentDay.Start);
            Assert.AreEqual(1L, Total(previousDay, EngineTelemetryMeasurement.Request));
            Assert.AreEqual(1L, Total(currentDay, EngineTelemetryMeasurement.Request));
            AssertDisabled(aggregator);
        }

        [TestMethod]
        public void LongSuspensionSkipsEmptyWindowsWithoutAllocatingBacklog()
        {
            EngineTelemetryAggregator aggregator = Create(out ManualTimeProvider clock);
            EngineTelemetryConfiguration configuration = aggregator.AcceptConfiguration();
            RecordRequest(aggregator, configuration);
            clock.Advance(TimeSpan.FromDays(365));
            RecordRequest(aggregator, configuration);

            EngineTelemetryDrain result = aggregator.Complete();
            Assert.AreEqual(2, result.Windows.Length);
            Assert.AreEqual(0L, result.DroppedWindows);
            Assert.IsTrue(result.Windows.All(w => w.Series.Single().Count == 1));
        }

        [TestMethod]
        public void WindowBufferOverflowReportsLossWithoutRepeatingEarlierCounts()
        {
            EngineTelemetryAggregator aggregator = Create(out ManualTimeProvider clock, new() { EnableSyntheticCollection = true, PendingWindowCapacity = 2 });
            EngineTelemetryConfiguration configuration = aggregator.AcceptConfiguration();
            for (int i = 0; i < 3; i++)
            {
                RecordRequest(aggregator, configuration);
                clock.Advance(TimeSpan.FromHours(6));
            }

            EngineTelemetryDrain result = aggregator.DrainCompletedWindows();
            Assert.AreEqual(2, result.Windows.Length);
            Assert.AreEqual(1L, result.DroppedWindows);
            Assert.AreEqual(1L, result.DroppedMeasurements);
            Assert.AreEqual(2L, result.Windows.Sum(w => Total(w, EngineTelemetryMeasurement.Request)));
            Assert.AreEqual(EngineTelemetryDrain.Empty, aggregator.DrainCompletedWindows());

            RecordRequest(aggregator, configuration);
            EngineTelemetryDrain final = aggregator.Complete();
            Assert.AreEqual(0L, final.DroppedWindows);
            Assert.AreEqual(1L, final.Windows.Single().Series.Single().Count);
        }

        [TestMethod]
        public void DroppedWindowRetainsItsKnownCapacityAndClockLosses()
        {
            EngineTelemetryAggregator aggregator = Create(out ManualTimeProvider clock, new()
            {
                EnableSyntheticCollection = true,
                PendingWindowCapacity = 1,
                SeriesCapacity = 1,
                CounterCeiling = 1
            });
            EngineTelemetryConfiguration first = aggregator.AcceptConfiguration();
            RecordRequest(aggregator, first);
            clock.Advance(TimeSpan.FromHours(6));
            // The first window is pending. The next one records one measurement, then loses
            // one each to counter capacity, series capacity and a backward clock.
            RecordRequest(aggregator, first);
            RecordRequest(aggregator, first);
            RecordRequest(aggregator, aggregator.AcceptConfiguration());
            clock.Advance(TimeSpan.FromSeconds(-1));
            RecordRequest(aggregator, first);
            clock.Advance(TimeSpan.FromHours(6));

            EngineTelemetryDrain result = aggregator.DrainCompletedWindows();
            Assert.AreEqual(1, result.Windows.Length);
            Assert.AreEqual(1L, result.DroppedWindows);
            Assert.AreEqual(4L, result.DroppedMeasurements);
        }

        [TestMethod]
        public void NewSeriesAreCappedWithoutEvictingOrRelabelingExistingEpochs()
        {
            EngineTelemetryAggregator aggregator = Create(out _, new() { EnableSyntheticCollection = true, SeriesCapacity = 2 });
            EngineTelemetryConfiguration first = aggregator.AcceptConfiguration();
            RecordRequest(aggregator, first);
            RecordRequest(aggregator, aggregator.AcceptConfiguration());
            RecordRequest(aggregator, aggregator.AcceptConfiguration());
            RecordRequest(aggregator, first);

            EngineTelemetryWindow window = aggregator.Complete().Windows.Single();
            Assert.AreEqual(2, window.Series.Length);
            Assert.AreEqual(1L, window.SeriesCapacityDrops);
            Assert.AreEqual(2L, window.Series.Single(s => s.ConfigurationEpoch == 1).Count);
            Assert.IsFalse(window.Series.Any(s => s.ConfigurationEpoch == 3));
        }

        [TestMethod]
        public void CounterSaturationPreservesOutcomePartitionAndMarksHistogramIncomplete()
        {
            EngineTelemetryAggregator aggregator = Create(out _, new() { EnableSyntheticCollection = true, CounterCeiling = 3 });
            EngineTelemetryConfiguration configuration = aggregator.AcceptConfiguration();
            RecordRequest(aggregator, configuration, EngineTelemetryOutcome.Success);
            RecordRequest(aggregator, configuration, EngineTelemetryOutcome.Failure);
            RecordRequest(aggregator, configuration, EngineTelemetryOutcome.Canceled);
            RecordRequest(aggregator, configuration, EngineTelemetryOutcome.PartialFailure);

            EngineTelemetryWindow window = aggregator.Complete().Windows.Single();
            EngineTelemetrySeries series = window.Series.Single();
            Assert.AreEqual(3L, series.Count);
            Assert.AreEqual(series.Count, SumOutcomes(series));
            Assert.IsTrue(series.IsCapped);
            Assert.AreEqual(1L, window.CounterCapacityDrops);
            Assert.AreEqual(3L, series.Latency!.TimedCount);
            Assert.IsFalse(series.Latency.IsComplete);
            Assert.AreEqual(series.Latency.TimedCount, series.Latency.Buckets.Sum());
        }

        [TestMethod]
        public void HistogramBoundariesAreInclusiveWithAnExplicitOverflowBucket()
        {
            EngineTelemetryAggregator aggregator = Create(out _);
            EngineTelemetryConfiguration configuration = aggregator.AcceptConfiguration();
            foreach (long bound in EngineTelemetryHistogram.UpperBoundsMilliseconds)
            {
                RecordRequest(aggregator, configuration, duration: TimeSpan.FromMilliseconds(bound));
            }

            RecordRequest(aggregator, configuration, duration: TimeSpan.MaxValue);
            EngineTelemetryHistogram histogram = aggregator.Complete().Windows.Single().Series.Single().Latency!;
            Assert.AreEqual(EngineTelemetryHistogram.UpperBoundsMilliseconds.Length + 1, histogram.Buckets.Length);
            Assert.IsTrue(histogram.Buckets.All(count => count == 1));
            Assert.AreEqual((long)histogram.Buckets.Length, histogram.TimedCount);
            Assert.IsTrue(histogram.IsComplete);
        }

        [TestMethod]
        public void MissingOrNegativeTimingsDoNotBecomeZeroDurationSamples()
        {
            EngineTelemetryAggregator aggregator = Create(out _);
            EngineTelemetryConfiguration configuration = aggregator.AcceptConfiguration();
            aggregator.RecordRequest(configuration, EngineTelemetryApi.Rest, EngineTelemetryTransport.Http,
                EngineTelemetryRole.Anonymous, EngineTelemetryOutcome.Success, duration: null);
            RecordRequest(aggregator, configuration, duration: TimeSpan.FromTicks(-1));
            RecordRequest(aggregator, configuration, duration: TimeSpan.Zero);

            EngineTelemetrySeries series = aggregator.Complete().Windows.Single().Series.Single();
            Assert.AreEqual(3L, series.Count);
            Assert.AreEqual(1L, series.Latency!.TimedCount);
            Assert.AreEqual(1L, series.Latency.Buckets[0]);
            Assert.IsFalse(series.Latency.IsComplete);
        }

        [TestMethod]
        public void ConcurrentCompletionsKeepCountsOutcomesAndHistogramsConsistent()
        {
            EngineTelemetryAggregator aggregator = Create(out _);
            EngineTelemetryConfiguration configuration = aggregator.AcceptConfiguration();
            Parallel.For(0, 10000, i => RecordRequest(aggregator, configuration, (EngineTelemetryOutcome)(i % 5)));

            EngineTelemetrySeries series = aggregator.Complete().Windows.Single().Series.Single();
            Assert.AreEqual(10000L, series.Count);
            Assert.AreEqual(series.Count, SumOutcomes(series));
            Assert.AreEqual(new EngineTelemetryOutcomeCounts(2000, 2000, 2000, 2000, 2000), series.Outcomes);
            Assert.AreEqual(series.Count, series.Latency!.Buckets.Sum());
            Assert.IsTrue(series.Latency.IsComplete);
        }

        [TestMethod]
        public void ConcurrentDisableCannotLeavePendingDataOrResumeRecording()
        {
            EngineTelemetryAggregator aggregator = Create(out _);
            EngineTelemetryConfiguration configuration = aggregator.AcceptConfiguration();
            Parallel.Invoke(
                () => Parallel.For(0, 1000, _ => RecordRequest(aggregator, configuration)),
                aggregator.Disable);

            AssertDisabled(aggregator);
        }

        [TestMethod]
        public void DifferentRunOrUnacceptedConfigurationCannotRecordMeasurements()
        {
            EngineTelemetryAggregator aggregator = Create(out _);
            EngineTelemetryAggregator otherRun = Create(out _);
            RecordRequest(aggregator, default);
            RecordRequest(aggregator, otherRun.AcceptConfiguration());
            Assert.AreEqual(EngineTelemetryDrain.Empty, aggregator.Complete());
        }

        [TestMethod]
        public void BackwardClockCorrectionCannotReopenASealedWindow()
        {
            EngineTelemetryAggregator aggregator = Create(out ManualTimeProvider clock);
            EngineTelemetryConfiguration configuration = aggregator.AcceptConfiguration();
            RecordRequest(aggregator, configuration);
            clock.Advance(TimeSpan.FromMinutes(1));
            EngineTelemetryWindow sealedWindow = aggregator.DrainCompletedWindows().Windows.Single();
            clock.Advance(TimeSpan.FromMinutes(-2));
            RecordRequest(aggregator, configuration);
            EngineTelemetryWindow final = aggregator.Complete().Windows.Single();

            Assert.AreEqual(1L, sealedWindow.Series.Single().Count);
            Assert.AreEqual(sealedWindow.End, final.Start);
            Assert.IsTrue(final.End >= final.Start);
            Assert.AreEqual(0, final.Series.Length);
            Assert.AreEqual(1L, final.ClockRegressionDrops);
        }

        [TestMethod]
        public void BackwardClockWithinWindowIsReportedRatherThanSilentlyMisattributed()
        {
            EngineTelemetryAggregator aggregator = Create(out ManualTimeProvider clock);
            EngineTelemetryConfiguration configuration = aggregator.AcceptConfiguration();
            clock.Advance(TimeSpan.FromSeconds(10));
            RecordRequest(aggregator, configuration);
            clock.Advance(TimeSpan.FromSeconds(-5));
            RecordRequest(aggregator, configuration);
            EngineTelemetryWindow window = aggregator.Complete().Windows.Single();

            Assert.AreEqual(_start.AddSeconds(10), window.End);
            Assert.AreEqual(1L, window.Series.Single().Count);
            Assert.AreEqual(1L, window.ClockRegressionDrops);
        }

        [TestMethod]
        public void ValidationCannotIncreaseProductionMemoryBounds()
        {
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => Create(out _, new()
            {
                EnableSyntheticCollection = true,
                SeriesCapacity = EngineTelemetryOptions.MAX_SERIES_PER_WINDOW + 1
            }));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => Create(out _, new()
            {
                EnableSyntheticCollection = true,
                PendingWindowCapacity = EngineTelemetryOptions.MAX_PENDING_WINDOWS + 1
            }));
        }

        private static EngineTelemetryAggregator Create(out ManualTimeProvider clock, EngineTelemetryOptions? options = null)
        {
            clock = new ManualTimeProvider(_start);
            return EngineTelemetryAggregator.Create(options ?? new() { EnableSyntheticCollection = true }, clock, _ => null);
        }

        private static void RecordRequest(EngineTelemetryAggregator aggregator, EngineTelemetryConfiguration configuration,
            EngineTelemetryOutcome outcome = EngineTelemetryOutcome.Success, TimeSpan? duration = null)
            => aggregator.RecordRequest(configuration, EngineTelemetryApi.Rest, EngineTelemetryTransport.Http,
                EngineTelemetryRole.Anonymous, outcome, duration ?? TimeSpan.FromMilliseconds(1));

        private static void RecordRead(EngineTelemetryAggregator aggregator, EngineTelemetryConfiguration configuration)
            => aggregator.RecordOperation(configuration, EngineTelemetryApi.Rest, EngineTelemetryOperation.Read,
                EngineTelemetryProvider.MsSql, EngineTelemetryObject.Table, EngineTelemetryOutcome.Success);

        private static long Total(EngineTelemetryWindow window, EngineTelemetryMeasurement measurement)
            => window.Series.Where(s => s.Dimensions.Measurement == measurement).Sum(s => s.Count);

        private static long SumOutcomes(EngineTelemetrySeries series)
            => series.Outcomes!.Unknown + series.Outcomes.Success + series.Outcomes.Failure + series.Outcomes.PartialFailure + series.Outcomes.Canceled;

        private static void AssertDisabled(EngineTelemetryAggregator aggregator)
        {
            Assert.IsFalse(aggregator.IsEnabled);
            Assert.AreEqual(0L, aggregator.AcceptConfiguration().Epoch);
            RecordRequest(aggregator, default);
            Assert.AreEqual(EngineTelemetryDrain.Empty, aggregator.DrainCompletedWindows());
            Assert.AreEqual(EngineTelemetryDrain.Empty, aggregator.Complete());
        }

        private sealed class ManualTimeProvider : TimeProvider
        {
            private long _ticks;

            public ManualTimeProvider(DateTimeOffset now)
            {
                _ticks = now.UtcTicks;
            }

            public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

            public void Advance(TimeSpan elapsed) => Interlocked.Add(ref _ticks, elapsed.Ticks);
        }

        private sealed class ThrowingTimeProvider : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => throw new InvalidOperationException("A disabled collector must not initialize its clock.");
        }
    }
}
