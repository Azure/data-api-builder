// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Service.HealthCheck
{
    /// <summary>
    /// Creates a JSON response for the health check endpoint using the provided health report.
    /// If the response has already been created, it will be reused.
    /// </summary>
    public class EnhancedHealthReportResponseWriter
    {
        // Dependencies
        private ILogger? _logger;
        private HealthCheckUtlity _healthCheckUtlity;

        // Constants
        private const string JSON_CONTENT_TYPE = "application/json; charset=utf-8";

        public EnhancedHealthReportResponseWriter(ILogger<HealthReportResponseWriter>? logger, HealthCheckUtlity healthCheckUtlity)
        {
            _logger = logger;
            _healthCheckUtlity = healthCheckUtlity;
        }

        /* {
        "runtime" : {
            "health" : {
                "enabled": true, (default: true)
                "cache-ttl": 5, (optional default: 5)
                "max-dop": 5, (optional default: 1)
                "roles": ["anonymous", "authenticated"] (optional default: *)
            }
        },            
        {
        "data-source" : {
            "health" : {
                "moniker": "sqlserver", (optional default: NULL) 
                "enabled": true, (default: true)
                "query": "SELECT TOP 1 1", (option)
                "threshold-ms": 100 (optional default: 10000)
            }
        },
        {
        "<entity-name>": {
            "health": {
                "enabled": true, (default: true)
                "filter": "Id eq 1" (optional default: null),
                "first": 1 (optional default: 1),
                "threshold-ms": 100 (optional default: 10000)
            }
        } */
        /// <summary>
        /// Function provided to the health check middleware to write the response.
        /// </summary>
        /// <param name="context">HttpContext for writing the response.</param>
        /// <param name="healthReport">Result of health check(s).</param>
        /// <param name="config">Result of health check(s).</param>
        /// <returns>Writes the http response to the http context.</returns>
        public Task WriteResponse(HttpContext context, HealthReport healthReport, RuntimeConfig config)
        {
            context.Response.ContentType = JSON_CONTENT_TYPE;
            LogTrace("Writing health report response.");
            DabHealthCheckReport dabHealthCheckReport = _healthCheckUtlity.GetHealthCheckResponse(healthReport, config);
            return context.Response.WriteAsync(JsonSerializer.Serialize(dabHealthCheckReport, options: new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull }));
        }

        /// <summary>
        /// Logs a trace message if a logger is present and the logger is enabled for trace events.
        /// </summary>
        /// <param name="message">Message to emit.</param>
        private void LogTrace(string message)
        {
            if (_logger is not null && _logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogTrace(message);
            }
        }
    }
}
