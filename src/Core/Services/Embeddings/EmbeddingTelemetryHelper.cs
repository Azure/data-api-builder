// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Azure.DataApiBuilder.Core.Telemetry;
using OpenTelemetry.Trace;

namespace Azure.DataApiBuilder.Core.Services.Embeddings;

/// <summary>
/// Helper class for tracking embedding-related telemetry metrics and traces.
/// </summary>
public static class EmbeddingTelemetryHelper
{
    /// <summary>
    /// Meter name for embedding metrics.
    /// </summary>
    public static readonly string MeterName = "DataApiBuilder.Embeddings";

    // Metrics
    private static readonly Meter _meter = new(MeterName);

    // Counters
    private static readonly Counter<long> _embeddingRequests = _meter.CreateCounter<long>(
        "embedding_requests_total",
        description: "Total number of embedding requests");

    private static readonly Counter<long> _embeddingApiCalls = _meter.CreateCounter<long>(
        "embedding_api_calls_total",
        description: "Total number of embedding API calls (excludes cache hits)");

    private static readonly Counter<long> _embeddingCacheHits = _meter.CreateCounter<long>(
        "embedding_cache_hits_total",
        description: "Total number of embedding cache hits");

    private static readonly Counter<long> _embeddingCacheMisses = _meter.CreateCounter<long>(
        "embedding_cache_misses_total",
        description: "Total number of embedding cache misses");

    private static readonly Counter<long> _embeddingErrors = _meter.CreateCounter<long>(
        "embedding_errors_total",
        description: "Total number of embedding errors");

    private static readonly Counter<long> _embeddingTextsProcessed = _meter.CreateCounter<long>(
        "embedding_texts_processed_total",
        description: "Total number of texts processed for embedding");

    // Histograms for timing and sizing
    private static readonly Histogram<double> _embeddingApiDuration = _meter.CreateHistogram<double>(
        "embedding_api_duration_ms",
        unit: "ms",
        description: "Duration of embedding API calls in milliseconds");

    private static readonly Histogram<double> _embeddingTotalDuration = _meter.CreateHistogram<double>(
        "embedding_total_duration_ms",
        unit: "ms",
        description: "Total duration of embedding operations including cache lookup");

    private static readonly Histogram<long> _embeddingTokens = _meter.CreateHistogram<long>(
        "embedding_tokens_total",
        description: "Total tokens used in embedding requests");

    private static readonly Histogram<int> _embeddingDimensions = _meter.CreateHistogram<int>(
        "embedding_dimensions",
        description: "Number of dimensions in embedding vectors");

    /// <summary>
    /// Tracks an embedding request (entry point, includes cache hits).
    /// </summary>
    /// <param name="provider">The embedding provider (e.g., azure-openai, openai).</param>
    /// <param name="textCount">Number of texts being embedded.</param>
    public static void TrackEmbeddingRequest(string provider, int textCount)
    {
        _embeddingRequests.Add(1,
            new KeyValuePair<string, object?>("provider", provider));
        _embeddingTextsProcessed.Add(textCount,
            new KeyValuePair<string, object?>("provider", provider));
    }

    /// <summary>
    /// Tracks an embedding API call (cache miss, actual API call made).
    /// </summary>
    /// <param name="provider">The embedding provider.</param>
    /// <param name="textCount">Number of texts sent to API.</param>
    public static void TrackApiCall(string provider, int textCount)
    {
        _embeddingApiCalls.Add(1,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("text_count", textCount));
    }

    /// <summary>
    /// Tracks an embedding cache hit.
    /// </summary>
    /// <param name="provider">The embedding provider.</param>
    public static void TrackCacheHit(string provider)
    {
        _embeddingCacheHits.Add(1, new KeyValuePair<string, object?>("provider", provider));
    }

    /// <summary>
    /// Tracks an embedding cache miss.
    /// </summary>
    /// <param name="provider">The embedding provider.</param>
    public static void TrackCacheMiss(string provider)
    {
        _embeddingCacheMisses.Add(1, new KeyValuePair<string, object?>("provider", provider));
    }

    /// <summary>
    /// Tracks an embedding error.
    /// </summary>
    /// <param name="provider">The embedding provider.</param>
    /// <param name="errorType">The type of error that occurred.</param>
    public static void TrackError(string provider, string errorType)
    {
        _embeddingErrors.Add(1,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("error_type", errorType));
    }

    /// <summary>
    /// Tracks the duration of an embedding API call.
    /// </summary>
    /// <param name="provider">The embedding provider.</param>
    /// <param name="duration">The duration of the API call.</param>
    /// <param name="textCount">Number of texts embedded.</param>
    public static void TrackApiDuration(string provider, TimeSpan duration, int textCount)
    {
        _embeddingApiDuration.Record(duration.TotalMilliseconds,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("text_count", textCount));
    }

    /// <summary>
    /// Tracks the total duration of an embedding operation (including cache lookup).
    /// </summary>
    /// <param name="provider">The embedding provider.</param>
    /// <param name="duration">The total duration.</param>
    /// <param name="fromCache">Whether result was from cache.</param>
    public static void TrackTotalDuration(string provider, TimeSpan duration, bool fromCache)
    {
        _embeddingTotalDuration.Record(duration.TotalMilliseconds,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("from_cache", fromCache));
    }

    /// <summary>
    /// Tracks token usage from an embedding request.
    /// </summary>
    /// <param name="provider">The embedding provider.</param>
    /// <param name="totalTokens">Total tokens used.</param>
    public static void TrackTokenUsage(string provider, long totalTokens)
    {
        _embeddingTokens.Record(totalTokens, new KeyValuePair<string, object?>("provider", provider));
    }

    /// <summary>
    /// Tracks embedding vector dimensions.
    /// </summary>
    /// <param name="provider">The embedding provider.</param>
    /// <param name="dimensions">Number of dimensions in the vector.</param>
    public static void TrackDimensions(string provider, int dimensions)
    {
        _embeddingDimensions.Record(dimensions, new KeyValuePair<string, object?>("provider", provider));
    }

    /// <summary>
    /// Starts an activity for embedding operations.
    /// </summary>
    /// <param name="operationName">Name of the operation (e.g., "EmbedAsync", "EmbedBatchAsync").</param>
    /// <returns>The started activity, or null if tracing is not enabled.</returns>
    public static Activity? StartEmbeddingActivity(string operationName)
    {
        return TelemetryTracesHelper.DABActivitySource.StartActivity(
            name: $"Embedding.{operationName}",
            kind: ActivityKind.Client);
    }

    /// <summary>
    /// Sets embedding-specific tags on an activity.
    /// </summary>
    /// <param name="activity">The activity to tag.</param>
    /// <param name="provider">The embedding provider.</param>
    /// <param name="model">The model being used.</param>
    /// <param name="textCount">Number of texts being embedded.</param>
    public static void SetEmbeddingActivityTags(
        this Activity activity,
        string provider,
        string? model,
        int textCount)
    {
        if (activity.IsAllDataRequested)
        {
            activity.SetTag("embedding.provider", provider);
            if (!string.IsNullOrEmpty(model))
            {
                activity.SetTag("embedding.model", model);
            }

            activity.SetTag("embedding.text_count", textCount);
        }
    }

    /// <summary>
    /// Records cache status on an activity.
    /// </summary>
    /// <param name="activity">The activity to tag.</param>
    /// <param name="cacheHits">Number of cache hits.</param>
    /// <param name="cacheMisses">Number of cache misses.</param>
    public static void SetCacheActivityTags(
        this Activity activity,
        int cacheHits,
        int cacheMisses)
    {
        if (activity.IsAllDataRequested)
        {
            activity.SetTag("embedding.cache_hits", cacheHits);
            activity.SetTag("embedding.cache_misses", cacheMisses);
        }
    }

    /// <summary>
    /// Records successful completion of an embedding activity.
    /// </summary>
    /// <param name="activity">The activity to complete.</param>
    /// <param name="durationMs">Duration in milliseconds.</param>
    /// <param name="dimensions">Number of dimensions in the result.</param>
    public static void SetEmbeddingActivitySuccess(
        this Activity activity,
        double durationMs,
        int? dimensions = null)
    {
        if (activity.IsAllDataRequested)
        {
            activity.SetTag("embedding.duration_ms", durationMs);
            if (dimensions.HasValue)
            {
                activity.SetTag("embedding.dimensions", dimensions.Value);
            }

            activity.SetStatus(ActivityStatusCode.Ok);
        }
    }

    /// <summary>
    /// Records an error on an embedding activity.
    /// </summary>
    /// <param name="activity">The activity to record error on.</param>
    /// <param name="ex">The exception that occurred.</param>
    public static void SetEmbeddingActivityError(
        this Activity activity,
        Exception ex)
    {
        if (activity.IsAllDataRequested)
        {
            activity.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity.RecordException(ex);
            activity.SetTag("error.type", ex.GetType().Name);
            activity.SetTag("error.message", ex.Message);
        }
    }
}
