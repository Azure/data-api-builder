// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Service.HealthCheck
{
    /// <summary>
    /// Creates a JSON response for the comprehensive health check endpoint using the provided health report.
    /// If the response has already been created, it will be reused.
    /// </summary>
    public class ComprehensiveHealthReportResponseWriter
    {
        // Dependencies
        private readonly ILogger<ComprehensiveHealthReportResponseWriter> _logger;
        private readonly RuntimeConfigProvider _runtimeConfigProvider;
        private readonly HealthCheckHelper _healthCheckHelper;

        public ComprehensiveHealthReportResponseWriter(
            ILogger<ComprehensiveHealthReportResponseWriter> logger,
            RuntimeConfigProvider runtimeConfigProvider,
            HealthCheckHelper healthCheckHelper)
        {
            _logger = logger;
            _runtimeConfigProvider = runtimeConfigProvider;
            _healthCheckHelper = healthCheckHelper;
        }

        /* {
        Sample Config JSON updated with Health Properties:
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
                "enabled": true, (default: true),
                "threshold-ms": 100 (optional default: 10000)
            }
        },
        {
        "<entity-name>": {
            "health": {
                "enabled": true, (default: true)
                "first": 1 (optional default: 1),
                "threshold-ms": 100 (optional default: 10000)
            }
        } */
        /// <summary>
        /// Function provided to the health check middleware to write the response.
        /// </summary>
        /// <param name="context">HttpContext for writing the response.</param>
        /// <returns>Writes the http response to the http context.</returns>
        public Task WriteResponse(HttpContext context)
        {
            RuntimeConfig config = _runtimeConfigProvider.GetConfig();

            // Global comprehensive Health Check Enabled
            if (config.IsHealthEnabled)
            {
                ComprehensiveHealthCheckReport dabHealthCheckReport = _healthCheckHelper.GetHealthCheckResponse(context, config);
                string response = JsonSerializer.Serialize(dabHealthCheckReport, options: new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
                LogTrace($"Health check response writer writing status as: {dabHealthCheckReport.Status}");
                return context.Response.WriteAsync(response);
            }
            else
            {
                LogTrace("Comprehensive Health Check Report Not Found: 404 Not Found.");
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return context.Response.CompleteAsync();
            }
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
