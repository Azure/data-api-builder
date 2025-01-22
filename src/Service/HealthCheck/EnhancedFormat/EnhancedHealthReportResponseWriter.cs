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
        private HealthCheckUtility _healthCheckUtility;
        

        public EnhancedHealthReportResponseWriter(ILogger<HealthReportResponseWriter>? logger, HealthCheckUtility healthCheckUtility)
        {
            _logger = logger;
            _healthCheckUtility = healthCheckUtility;
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
        public async Task<Task> WriteResponse(HttpContext context, HealthReport healthReport, RuntimeConfig config)
        {
            context.Response.ContentType = Utilities.JSON_CONTENT_TYPE;
            LogTrace("Writing health report response.");
            DabHealthCheckReport dabHealthCheckReport = await _healthCheckUtility.GetHealthCheckResponse(healthReport, config).ConfigureAwait(false);
            FormatDabHealthCheckReport(ref dabHealthCheckReport);
            dabHealthCheckReport.HealthCheckResults = null;
            
            string response = JsonSerializer.Serialize(dabHealthCheckReport, options: new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
            return context.Response.WriteAsync(response);
        }

        private static void FormatDabHealthCheckReport(ref DabHealthCheckReport dabHealthCheckReport)
        {
            if (dabHealthCheckReport.HealthCheckResults == null) { return; }

            dabHealthCheckReport.Checks = new();
            if (dabHealthCheckReport.HealthCheckResults.DataSourceHealthCheckResults != null)
            {
                dabHealthCheckReport.Checks.DataSourceHealthCheckResults = new();
                foreach (HealthCheckDetailsResultEntry dataSourceList in dabHealthCheckReport.HealthCheckResults.DataSourceHealthCheckResults)
                {
                    if (dataSourceList.Name != null)
                    {
                        dabHealthCheckReport.Checks.DataSourceHealthCheckResults.Add(dataSourceList.Name, dataSourceList);
                    }
                }
            }

            if (dabHealthCheckReport.HealthCheckResults.EntityHealthCheckResults != null)
            {
                dabHealthCheckReport.Checks.EntityHealthCheckResults = new();
                foreach (HealthCheckEntityResultEntry EntityList in dabHealthCheckReport.HealthCheckResults.EntityHealthCheckResults)
                {
                    if (EntityList.Name != null)
                    {
                        dabHealthCheckReport.Checks.EntityHealthCheckResults.Add(EntityList.Name, EntityList.EntityHealthCheckResults);
                    }
                }
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
