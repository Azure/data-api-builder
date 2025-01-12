// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Service.HealthCheck
{
    /// <summary>
    /// Creates a JSON response for the health check endpoint using the provided health report.
    /// If the response has already been created, it will be reused.
    /// </summary>
    public class HealthReportResponseWriter
    {
        // Dependencies
        private ILogger? _logger;
        private RuntimeConfigProvider _runtimeConfigProvider;
        private OriginalHealthReportResponseWriter _originalHealthReportResponseWriter;
        private EnhancedHealthReportResponseWriter _enhancedHealthReportResponseWriter;

        public HealthReportResponseWriter(
            ILogger<HealthReportResponseWriter>? logger,
            RuntimeConfigProvider runtimeConfigProvider,
            OriginalHealthReportResponseWriter originalHealthReportResponseWriter,
            EnhancedHealthReportResponseWriter enhancedHealthReportResponseWriter)
        {
            _logger = logger;
            _runtimeConfigProvider = runtimeConfigProvider;
            this._originalHealthReportResponseWriter = originalHealthReportResponseWriter;
            this._enhancedHealthReportResponseWriter = enhancedHealthReportResponseWriter;
        }

        /// <summary>
        /// Function provided to the health check middleware to write the response.
        /// </summary>
        /// <param name="context">HttpContext for writing the response.</param>
        /// <param name="healthReport">Result of health check(s).</param>
        /// <returns>Writes the http response to the http context.</returns>
        public Task WriteResponse(HttpContext context, HealthReport healthReport)
        {
            RuntimeConfig config = _runtimeConfigProvider.GetConfig();
            if (config?.Runtime != null && config.Runtime?.Health != null && config.Runtime.Health.Enabled)
            {
                LogTrace("Enhanced Health check is enabled in the runtime configuration.");
                return _enhancedHealthReportResponseWriter.WriteResponse(context, healthReport, config);
            }
            else
            {
                LogTrace("Showing Health check in original format for runtime configuration.");
                return _originalHealthReportResponseWriter.WriteResponse(context, healthReport);
            }
        }

        // <summary>
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
