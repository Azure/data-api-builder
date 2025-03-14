// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.Metrics;

namespace Azure.DataApiBuilder.Service.Telemetry
{
    public static class TelemetryMetricsHelper
    {
        public static readonly string MeterName = "DataApiBuilder.Metrics";
        private static readonly Meter _meter = new(MeterName);
        private static readonly Counter<long> _activeRequests = _meter.CreateCounter<long>("active_requests");
        private static readonly Counter<long> _errorCounter = _meter.CreateCounter<long>("total_errors");
        private static readonly Counter<long> _totalRequests = _meter.CreateCounter<long>("total_requests");
        private static readonly Histogram<double> _requestDuration = _meter.CreateHistogram<double>("request_duration", "ms");

        public static void IncrementActiveRequests() => _activeRequests.Add(1);

        public static void DecrementActiveRequests() => _activeRequests.Add(-1);

        public static void TrackRequest(string method, int statusCode, string endpoint, string apiType)
        {
            _totalRequests.Add(1,
                new("method", method),
                new("status_code", statusCode),
                new("endpoint", endpoint),
                new("api_type", apiType));
        }

        public static void TrackError(string method, int statusCode, string endpoint, string apiType, Exception ex)
        {
            _errorCounter.Add(1,
                new("method", method),
                new("status_code", statusCode),
                new("endpoint", endpoint),
                new("api_type", apiType),
                new("error_type", ex.GetType().Name));
        }

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
