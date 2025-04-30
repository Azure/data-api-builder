// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.Metrics;
using System.Net;
using Azure.DataApiBuilder.Config.ObjectModel;
using Kestral = Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http.HttpMethod;

namespace Azure.DataApiBuilder.Core.Telemetry
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

        public static void IncrementActiveRequests(ApiType kind) => _activeRequests.Add(1, new KeyValuePair<string, object?>("api_type", kind));

        public static void DecrementActiveRequests(ApiType kind) => _activeRequests.Add(-1, new KeyValuePair<string, object?>("api_type", kind));

        /// <summary>
        /// Tracks a request by incrementing the total requests counter and associating it with metadata.
        /// </summary>
        /// <param name="method">The HTTP method of the request (e.g., GET, POST).</param>
        /// <param name="statusCode">The HTTP status code of the response.</param>
        /// <param name="endpoint">The endpoint being accessed.</param>
        /// <param name="apiType">The type of API being used (e.g., REST, GraphQL).</param>
        public static void TrackRequest(Kestral method, HttpStatusCode statusCode, string endpoint, ApiType apiType)
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
        public static void TrackError(Kestral method, HttpStatusCode statusCode, string endpoint, ApiType apiType, Exception ex)
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
        public static void TrackRequestDuration(Kestral method, HttpStatusCode statusCode, string endpoint, ApiType apiType, TimeSpan duration)
        {
            _requestDuration.Record(duration.TotalMilliseconds,
                new("method", method),
                new("status_code", statusCode),
                new("endpoint", endpoint),
                new("api_type", apiType));
        }
    }
}
