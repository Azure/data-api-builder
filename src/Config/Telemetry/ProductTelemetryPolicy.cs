// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.Telemetry;

/// <summary>
/// Shared startup switches for DAB-owned product telemetry. Production collection remains off;
/// these switches do not change customer-configured logging, tracing, metrics or destinations.
/// </summary>
public static class ProductTelemetryPolicy
{
    /// <summary>Environment variable that overrides all DAB product telemetry enablement.</summary>
    public const string OPT_OUT_ENV_VAR = "DAB_TELEMETRY_OPT_OUT";

    /// <summary>Explicit validation-only collection; never a production enablement switch.</summary>
    public const string TEST_MODE_ENV_VAR = "DAB_PRODUCT_TELEMETRY_TEST_MODE";

    /// <summary>
    /// Interprets an opt-out value without reading the environment. Only trimmed <c>1</c> and
    /// <c>true</c> (case-insensitive) opt out; missing and unrecognized values do not enable or veto anything.
    /// </summary>
    public static bool IsOptedOut(string? value) => IsTrue(value);

    /// <summary>Pure startup policy shared by configuration capture and standalone hosting.</summary>
    public static bool IsSyntheticCollectionEnabled(string? testMode, string? optOut)
        => IsTrue(testMode) && !IsOptedOut(optOut);

    private static bool IsTrue(string? value)
    {
        string? normalizedValue = value?.Trim();
        return string.Equals(normalizedValue, "1", StringComparison.Ordinal)
            || string.Equals(normalizedValue, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Reads the process environment's product telemetry opt-out.</summary>
    public static bool IsOptedOut() => IsOptedOut(Environment.GetEnvironmentVariable(OPT_OUT_ENV_VAR));
}
