// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using Azure.DataApiBuilder.Config.Telemetry;

namespace Azure.DataApiBuilder.Service.Telemetry
{
    /// <summary>Shared, read-only preflight. Never initializes an SDK sender or contacts a destination.</summary>
    internal static class ProductTelemetryBootstrap
    {
        internal const string TEST_MODE_VARIABLE = ProductTelemetryPolicy.TEST_MODE_ENV_VAR;
        internal const string OPT_OUT_VARIABLE = ProductTelemetryPolicy.OPT_OUT_ENV_VAR;
        internal const string CONNECTION_STRING_VARIABLE = "DAB_PRODUCT_TELEMETRY_CONNECTION_STRING";

        internal static bool TryGetDestination([NotNullWhen(true)] out ApplicationInsightsTelemetryDestination? destination)
            => TryGetDestination(Environment.GetEnvironmentVariable, out destination);

        /// <summary>Test seam for optional environment failures and short-circuiting, without changing process settings.</summary>
        internal static bool TryGetDestination(
            Func<string, string?> readEnvironmentVariable,
            [NotNullWhen(true)] out ApplicationInsightsTelemetryDestination? destination)
        {
            destination = null;
            try
            {
                ArgumentNullException.ThrowIfNull(readEnvironmentVariable);
                string? optOut = readEnvironmentVariable(OPT_OUT_VARIABLE);
                if (ProductTelemetryPolicy.IsOptedOut(optOut) ||
                    !ProductTelemetryPolicy.IsSyntheticCollectionEnabled(readEnvironmentVariable(TEST_MODE_VARIABLE), optOut) ||
                    !EngineTelemetryApplicationInsightsExporter.AreSdkStatisticsDisabled(readEnvironmentVariable))
                {
                    return false;
                }

                // Do not even read routing when collection has been vetoed. Customer Application
                // Insights configuration is never a fallback for the product's explicit destination.
                return ApplicationInsightsTelemetryDestination.TryParse(
                    readEnvironmentVariable(CONNECTION_STRING_VARIABLE), out destination);
            }
            catch (Exception)
            {
                // Optional telemetry must not prevent either the CLI or the engine from starting.
                destination = null;
                return false;
            }
        }
    }
}
