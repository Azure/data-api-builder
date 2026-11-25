// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;

namespace Azure.DataApiBuilder.Core.Telemetry.Product
{
    internal enum EngineTelemetryApi { Unknown, Rest, GraphQL, Mcp }

    internal enum EngineTelemetryTransport { Unknown, Http, Stdio, InProcess }

    internal enum EngineTelemetryRole { Unknown, Anonymous, Authenticated, Custom }

    internal enum EngineTelemetryOperation { Unknown, Read, Write, Execute }

    internal enum EngineTelemetryProvider { Unknown, MsSql, DwSql, PostgreSql, MySql, CosmosDb }

    internal enum EngineTelemetryObject { Unknown, Table, View, StoredProcedure, Document }

    internal enum EngineTelemetryCacheLayer { Unknown, Level1, Level2 }

    internal enum EngineTelemetryCacheResult { Unknown, Hit, Miss }

    internal enum EngineTelemetryOutcome { Unknown, Success, Failure, PartialFailure, Canceled }

    internal enum EngineTelemetryMeasurement { Request, Operation, DatabaseAttempt, CacheLookup, Embedding, HttpOutcome }

    internal enum EngineTelemetryHttpStatus { Unknown, Informational, Success, Redirect, ClientError, ServerError }

    /// <summary>
    /// An opaque accepted-configuration handle. Capture it when work starts and retain it until
    /// completion; neither a reload nor a window transition changes its epoch.
    /// The owner token prevents mixing measurements from different engine runs.
    /// </summary>
    internal readonly record struct EngineTelemetryConfiguration(object Owner, long Epoch);

    /// <summary>
    /// Closed, categorical dimensions only. Factory methods select a small, family-specific
    /// set of joint dimensions; unused fields are not additional measurement dimensions.
    /// No customer-defined string is accepted by the aggregation API.
    /// </summary>
    internal readonly record struct EngineTelemetryDimensions
    {
        public EngineTelemetryMeasurement Measurement { get; private init; }
        public EngineTelemetryApi Api { get; private init; }
        public EngineTelemetryTransport Transport { get; private init; }
        public EngineTelemetryRole Role { get; private init; }
        public EngineTelemetryOperation Operation { get; private init; }
        public EngineTelemetryProvider Provider { get; private init; }
        public EngineTelemetryObject ObjectType { get; private init; }
        public EngineTelemetryCacheLayer CacheLayer { get; private init; }
        public EngineTelemetryCacheResult CacheResult { get; private init; }
        public EngineTelemetryHttpStatus HttpStatusClass { get; private init; }

        public static EngineTelemetryDimensions ForRequest(EngineTelemetryApi api, EngineTelemetryTransport transport, EngineTelemetryRole role)
            => new() { Measurement = EngineTelemetryMeasurement.Request, Api = Normalize(api), Transport = Normalize(transport), Role = Normalize(role) };

        public static EngineTelemetryDimensions ForOperation(EngineTelemetryApi api, EngineTelemetryOperation operation, EngineTelemetryProvider provider, EngineTelemetryObject objectType)
            => new() { Measurement = EngineTelemetryMeasurement.Operation, Api = Normalize(api), Operation = Normalize(operation), Provider = Normalize(provider), ObjectType = Normalize(objectType) };

        public static EngineTelemetryDimensions ForDatabaseAttempt(EngineTelemetryProvider provider)
            => new() { Measurement = EngineTelemetryMeasurement.DatabaseAttempt, Provider = Normalize(provider) };

        public static EngineTelemetryDimensions ForCacheLookup(EngineTelemetryCacheLayer layer, EngineTelemetryCacheResult result)
            => new() { Measurement = EngineTelemetryMeasurement.CacheLookup, CacheLayer = Normalize(layer), CacheResult = Normalize(result) };

        public static EngineTelemetryDimensions ForEmbedding(EngineTelemetryApi api)
            => new() { Measurement = EngineTelemetryMeasurement.Embedding, Api = Normalize(api) };

        public static EngineTelemetryDimensions ForHttpOutcome(EngineTelemetryApi api, int? status)
            => new()
            {
                Measurement = EngineTelemetryMeasurement.HttpOutcome,
                Api = Normalize(api),
                Transport = EngineTelemetryTransport.Http,
                HttpStatusClass = status switch
                {
                    >= 100 and < 200 => EngineTelemetryHttpStatus.Informational,
                    >= 200 and < 300 => EngineTelemetryHttpStatus.Success,
                    >= 300 and < 400 => EngineTelemetryHttpStatus.Redirect,
                    >= 400 and < 500 => EngineTelemetryHttpStatus.ClientError,
                    >= 500 and < 600 => EngineTelemetryHttpStatus.ServerError,
                    _ => EngineTelemetryHttpStatus.Unknown
                }
            };

        internal static T Normalize<T>(T value) where T : struct, Enum => Enum.IsDefined(value) ? value : default;
    }

    internal sealed record EngineTelemetryOutcomeCounts(long Unknown, long Success, long Failure, long PartialFailure, long Canceled);

    /// <summary>
    /// Noncumulative histogram counts. Upper bounds are inclusive, in milliseconds; the final
    /// bucket has no upper bound. Missing/invalid timings do not become zero-duration samples.
    /// </summary>
    internal sealed record EngineTelemetryHistogram(ImmutableArray<long> Buckets, long TimedCount, bool IsComplete)
    {
        public static ImmutableArray<long> UpperBoundsMilliseconds { get; } = [1, 5, 10, 50, 100, 500, 1000, 5000, 30000];

        public const string BUCKET_SCHEMA = "request-latency-ms-v1";
    }

    internal sealed record EngineTelemetrySeries(
        long ConfigurationEpoch,
        EngineTelemetryDimensions Dimensions,
        long Count,
        EngineTelemetryOutcomeCounts? Outcomes,
        EngineTelemetryHistogram? Latency,
        bool IsCapped);

    internal sealed record EngineTelemetryWindow(
        DateTimeOffset Start,
        DateTimeOffset End,
        bool IsFinal,
        ImmutableArray<EngineTelemetrySeries> Series,
        long SeriesCapacityDrops,
        long CounterCapacityDrops,
        long ClockRegressionDrops,
        bool LossCountsCapped);

    /// <summary>
    /// Ownership of immutable completed windows passes to the caller once. This is an internal
    /// aggregation result, not the versioned event envelope or an exporter payload.
    /// </summary>
    internal sealed record EngineTelemetryDrain(
        ImmutableArray<EngineTelemetryWindow> Windows,
        long DroppedWindows,
        long DroppedMeasurements,
        bool LossCountsCapped)
    {
        public static EngineTelemetryDrain Empty { get; } = new([], 0, 0, false);

        // Only explicit synthetic validation can construct an enabled aggregator in this phase.
        public bool IsSynthetic { get; } = true;
    }
}
