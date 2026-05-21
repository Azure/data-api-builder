// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Azure.DataApiBuilder.Core.Telemetry;

namespace Azure.DataApiBuilder.Core.Services.Embeddings;

/// <summary>
/// Telemetry helper for the auto-embed parameter-substitution pipeline step.
///
/// Distinct from <see cref="EmbeddingTelemetryHelper"/> (Phase 1), which instruments
/// the embedding-service API client (cache hits, API latency, dimensions, etc.).
/// This helper instruments the substitution layer above it: when did substitution
/// fire, on what entity, and did it succeed or fail and why. Both emit to the
/// shared <see cref="TelemetryTracesHelper.DABActivitySource"/> so spans nest naturally
/// in a single request trace.
///
/// Instruments (meter: <see cref="MeterName"/>):
///   - <c>auto_embed_substitutions_total</c> (counter, tags: entity, outcome)
///     One increment per <c>SubstituteEmbedParametersAsync</c> invocation that has
///     at least one auto-embed parameter configured.
///   - <c>auto_embed_params_substituted_total</c> (counter, tag: entity)
///     Successful per-parameter count. For a sproc with 2 auto-embed params called
///     once, increments by 2. Useful for capacity planning and provider-cost projection.
///   - <c>auto_embed_duration_ms</c> (histogram, tags: entity, outcome)
///     Wall-clock duration of <c>SubstituteEmbedParametersAsync</c> including the inner
///     embedding API call. Use p50/p95/p99 to spot latency outliers.
///
/// Outcome values (low cardinality, stable string constants):
///   <see cref="OutcomeSuccess"/>, <see cref="OutcomeEmptyInput"/>,
///   <see cref="OutcomeNonString"/>, <see cref="OutcomeBatchFailure"/>,
///   <see cref="OutcomeServiceDisabled"/>.
/// </summary>
public static class ParameterEmbeddingTelemetryHelper
{
    /// <summary>
    /// Meter name. Operators subscribe their OTEL exporter to this name (or the
    /// wildcard <c>DataApiBuilder.*</c>) to receive these instruments.
    /// </summary>
    public static readonly string MeterName = "DataApiBuilder.AutoEmbedSubstitution";

    // Outcome tag values — kept as constants so callers and queries align.
    public const string OutcomeSuccess = "success";
    public const string OutcomeEmptyInput = "empty_input";
    public const string OutcomeNonString = "non_string";
    public const string OutcomeBatchFailure = "batch_failure";
    public const string OutcomeProviderInvalidResponse = "provider_invalid_response";
    public const string OutcomeServiceDisabled = "service_disabled";
    public const string OutcomeUnexpectedError = "unexpected_error";

    // Tag value used when the entity name is not known to the caller. Should be rare;
    // engines always have it. Kept as a constant rather than null to avoid losing
    // these data points in tag-required backends.
    public const string EntityUnknown = "(unknown)";

    private static readonly Meter _meter = new(MeterName);

    private static readonly Counter<long> _substitutions = _meter.CreateCounter<long>(
        "auto_embed_substitutions_total",
        description: "Total number of auto-embed parameter-substitution operations, by entity and outcome.");

    private static readonly Counter<long> _paramsSubstituted = _meter.CreateCounter<long>(
        "auto_embed_params_substituted_total",
        description: "Total number of individual parameters successfully substituted with embedding vectors, by entity.");

    private static readonly Histogram<double> _duration = _meter.CreateHistogram<double>(
        "auto_embed_duration_ms",
        unit: "ms",
        description: "Duration of auto-embed parameter substitution in milliseconds, including the inner embedding API call.");

    /// <summary>
    /// Starts an Activity (OTEL span) for an auto-embed substitution operation.
    /// Returns null when no listener is registered, which is the common case in
    /// non-instrumented environments; callers should guard with <c>?.</c>.
    /// </summary>
    public static Activity? StartSubstituteActivity(string? entityName, string? sprocName = null)
    {
        Activity? activity = TelemetryTracesHelper.DABActivitySource.StartActivity(
            name: "AutoEmbed.Substitute",
            kind: ActivityKind.Internal);
        if (activity is not null && activity.IsAllDataRequested)
        {
            activity.SetTag("entity", entityName ?? EntityUnknown);
            if (!string.IsNullOrEmpty(sprocName))
            {
                activity.SetTag("sproc", sprocName);
            }
        }

        return activity;
    }

    /// <summary>
    /// Sets the parameter names and provider/model metadata on the activity.
    /// Called after the collect phase determines which params will be embedded,
    /// and when provider metadata is available from the config.
    /// </summary>
    public static void SetActivityParamAndProviderTags(
        Activity? activity,
        IEnumerable<string>? paramNames,
        string? provider = null,
        string? model = null)
    {
        if (activity is null || !activity.IsAllDataRequested)
        {
            return;
        }

        if (paramNames is not null)
        {
            activity.SetTag("param_names", string.Join(",", paramNames));
        }

        if (!string.IsNullOrEmpty(provider))
        {
            activity.SetTag("embedding.provider", provider);
        }

        if (!string.IsNullOrEmpty(model))
        {
            activity.SetTag("embedding.model", model);
        }
    }

    /// <summary>
    /// Records a successful substitution: increments the counter, increments the
    /// per-param counter by <paramref name="paramCount"/>, records duration, and
    /// closes the activity with OK status.
    /// </summary>
    public static void RecordSuccess(Activity? activity, string? entityName, int paramCount, double durationMs)
    {
        string entity = entityName ?? EntityUnknown;
        _substitutions.Add(1,
            new KeyValuePair<string, object?>("entity", entity),
            new KeyValuePair<string, object?>("outcome", OutcomeSuccess));
        _paramsSubstituted.Add(paramCount,
            new KeyValuePair<string, object?>("entity", entity));
        _duration.Record(durationMs,
            new KeyValuePair<string, object?>("entity", entity),
            new KeyValuePair<string, object?>("outcome", OutcomeSuccess));

        if (activity is not null && activity.IsAllDataRequested)
        {
            activity.SetTag("param_count", paramCount);
            activity.SetTag("outcome", OutcomeSuccess);
            activity.SetTag("duration_ms", durationMs);
            activity.SetStatus(ActivityStatusCode.Ok);
        }
    }

    /// <summary>
    /// Records a failed substitution: increments the substitutions counter with the
    /// failure outcome, records duration, and closes the activity with Error status
    /// (including exception details when supplied). The per-param counter is NOT
    /// incremented since no parameter was successfully substituted.
    /// </summary>
    public static void RecordFailure(Activity? activity, string? entityName, string outcome, double durationMs, Exception? exception = null)
    {
        string entity = entityName ?? EntityUnknown;
        _substitutions.Add(1,
            new KeyValuePair<string, object?>("entity", entity),
            new KeyValuePair<string, object?>("outcome", outcome));
        _duration.Record(durationMs,
            new KeyValuePair<string, object?>("entity", entity),
            new KeyValuePair<string, object?>("outcome", outcome));

        if (activity is not null && activity.IsAllDataRequested)
        {
            activity.SetTag("outcome", outcome);
            activity.SetTag("duration_ms", durationMs);
            if (exception is not null)
            {
                activity.SetTag("error.type", exception.GetType().Name);
                activity.SetTag("error.message", exception.Message);
                activity.SetStatus(ActivityStatusCode.Error, exception.Message);
                activity.AddException(exception);
            }
            else
            {
                activity.SetStatus(ActivityStatusCode.Error, outcome);
            }
        }
    }
}
