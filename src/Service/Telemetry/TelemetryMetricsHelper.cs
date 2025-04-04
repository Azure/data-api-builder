// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.Metrics;

namespace Azure.DataApiBuilder.Service.Telemetry
{
    /// <summary>
    /// Helper class for tracking telemetry metrics such as active requests, errors, total requests,
    /// and request durations using the .NET Meter and Counter APIs.
    /// </summary>
    public static class TelemetryMetricsHelper
    {
        public static readonly string MeterName = "DataApiBuilder.Metrics";
        private static readonly Meter _meter = new(MeterName);
        private static readonly UpDownCounter<long> _activeRequests = _meter.CreateUpDownCounter<long>("active_requests");
        private static readonly Counter<long> _errorCounter = _meter.CreateCounter<long>("total_errors");
        private static readonly Counter<long> _totalRequests = _meter.CreateCounter<long>("total_requests");
        private static readonly Histogram<double> _requestDuration = _meter.CreateHistogram<double>("request_duration", "ms");

        public static void IncrementActiveRequests() => _activeRequests.Add(1);

        public static void DecrementActiveRequests() => _activeRequests.Add(-1);

        /// <summary>
        /// Tracks a request by incrementing the total requests counter and associating it with metadata.
        /// </summary>
        /// <param name="method">The HTTP method of the request (e.g., GET, POST).</param>
        /// <param name="statusCode">The HTTP status code of the response.</param>
        /// <param name="endpoint">The endpoint being accessed.</param>
        /// <param name="apiType">The type of API being used (e.g., REST, GraphQL).</param>
        public static void TrackRequest(string method, int statusCode, string endpoint, string apiType)
        {
            _totalRequests.Add(1,
                new("method", method),
                new("status_code", statusCode),
                new("endpoint", endpoint),
                new("api_type", apiType));
        }

        /// <summary>
        /// Tracks an error by incrementing the error counter and associating it with metadata.
        /// </summary>
        /// <param name="method">The HTTP method of the request (e.g., GET, POST).</param>
        /// <param name="statusCode">The HTTP status code of the response.</param>
        /// <param name="endpoint">The endpoint being accessed.</param>
        /// <param name="apiType">The type of API being used (e.g., REST, GraphQL).</param>
        /// <param name="ex">The exception that occurred.</param>
        public static void TrackError(string method, int statusCode, string endpoint, string apiType, Exception ex)
        {
            _errorCounter.Add(1,
                new("method", method),
                new("status_code", statusCode),
                new("endpoint", endpoint),
                new("api_type", apiType),
                new("error_type", ex.GetType().Name));
        }

        /// <summary>
        /// Tracks the duration of a request by recording it in a histogram and associating it with metadata.
        /// </summary>
        /// <param name="method">The HTTP method of the request (e.g., GET, POST).</param>
        /// <param name="statusCode">The HTTP status code of the response.</param>
        /// <param name="endpoint">The endpoint being accessed.</param>
        /// <param name="apiType">The type of API being used (e.g., REST, GraphQL).</param>
        /// <param name="duration">The duration of the request in milliseconds.</param>
        public static void TrackRequestDuration(string method, int statusCode, string endpoint, string apiType, double duration)
        {
            _requestDuration.Record(duration,
                new("method", method),
                new("status_code", statusCode),
                new("endpoint", endpoint),
                new("api_type", apiType));
        }
    }
}
