// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.Telemetry;

/// <summary>
/// One initialization/reload attempt's failure attribution. Nested boundaries record failures,
/// not progress: a later successful handler cannot overwrite a failure. Concurrent handlers
/// use the first observed failure; separate attempts never share this state.
/// </summary>
internal sealed class TelemetryFailureContext
{
    private static readonly AsyncLocal<TelemetryFailureContext?> _current = new();
    private int _failure = -1;

    internal static TelemetryFailureContext? Current => _current.Value;

    internal bool HasFailure => Volatile.Read(ref _failure) >= 0;

    internal TelemetryFailureStage FailureStage => Volatile.Read(ref _failure) is int value && value >= 0
        ? (TelemetryFailureStage)value : TelemetryFailureStage.Unknown;

    internal void RecordFailure(TelemetryFailureStage stage)
    {
        TelemetryFailureStage normalized = Enum.IsDefined(stage) ? stage : TelemetryFailureStage.Unknown;
        Interlocked.CompareExchange(ref _failure, (int)normalized, -1);
    }

    internal static IDisposable? Enter(TelemetryFailureContext? context)
    {
        TelemetryFailureContext? previous = _current.Value;
        if (ReferenceEquals(previous, context))
        {
            return null;
        }

        _current.Value = context;
        return new ContextScope(previous);
    }

    private sealed class ContextScope(TelemetryFailureContext? previous) : IDisposable
    {
        public void Dispose() => _current.Value = previous;
    }
}
