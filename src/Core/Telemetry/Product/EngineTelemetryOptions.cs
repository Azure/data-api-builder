// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.Telemetry;

namespace Azure.DataApiBuilder.Core.Telemetry.Product
{
    /// <summary>
    /// Internal validation controls, not customer configuration or production enablement.
    /// Shipping collection remains disabled pending privacy review and exporter validation.
    /// </summary>
    internal sealed record EngineTelemetryOptions
    {
        public const string OPT_OUT_ENVIRONMENT_VARIABLE = ProductTelemetryPolicy.OPT_OUT_ENV_VAR;
        public const int MAX_SERIES_PER_WINDOW = 256;
        public const int MAX_PENDING_WINDOWS = 4;

        public bool EnableSyntheticCollection { get; init; }

        public int SeriesCapacity { get; init; } = MAX_SERIES_PER_WINDOW;

        public int PendingWindowCapacity { get; init; } = MAX_PENDING_WINDOWS;

        // A lower ceiling permits deterministic saturation tests without billions of observations.
        public long CounterCeiling { get; init; } = long.MaxValue;

        internal void Validate()
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(SeriesCapacity, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(SeriesCapacity, MAX_SERIES_PER_WINDOW);
            ArgumentOutOfRangeException.ThrowIfLessThan(PendingWindowCapacity, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(PendingWindowCapacity, MAX_PENDING_WINDOWS);
            ArgumentOutOfRangeException.ThrowIfLessThan(CounterCeiling, 1);
        }

        internal static bool IsOptedOut(string? value)
        {
            return ProductTelemetryPolicy.IsOptedOut(value);
        }
    }
}
