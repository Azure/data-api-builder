// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
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
        private readonly IMemoryCache _cache;
        private const string CACHE_KEY = "HealthCheckResponse";

        public ComprehensiveHealthReportResponseWriter(
            ILogger<ComprehensiveHealthReportResponseWriter> logger,
            RuntimeConfigProvider runtimeConfigProvider,
            HealthCheckHelper healthCheckHelper,
            IMemoryCache cache)
        {
            _logger = logger;
            _runtimeConfigProvider = runtimeConfigProvider;
            _healthCheckHelper = healthCheckHelper;
            _cache = cache;
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
        public async Task WriteResponse(HttpContext context)
        {
            RuntimeConfig config = _runtimeConfigProvider.GetConfig();

            // Global comprehensive Health Check Enabled
            if (config != null && config.IsHealthEnabled)
            {
                if (!_healthCheckHelper.IsUserAllowedToAccessHealthCheck(context, config.HostMode, config.AllowedRolesForHealth))
                {
                    LogTrace("Comprehensive Health Check Report is not allowed: 403 Forbidden due to insufficient permissions.");
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.CompleteAsync();
                    return;
                }

                string? response;
                // Check if the cache is enabled 
                if (config.CacheTtlSeconds != null && config.CacheTtlSeconds > 0)
                {
                    if (!_cache.TryGetValue(CACHE_KEY, out response))
                    {
                        ComprehensiveHealthCheckReport dabHealthCheckReport = _healthCheckHelper.GetHealthCheckResponse(context, config);

                        response = JsonSerializer.Serialize(dabHealthCheckReport, options: new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
                        LogTrace($"Health check response writer writing status as: {dabHealthCheckReport.Status}");

                        // Cache the response for 5 minutes (or any other duration you prefer)
                        MemoryCacheEntryOptions cacheEntryOptions = new MemoryCacheEntryOptions()
                            .SetAbsoluteExpiration(TimeSpan.FromSeconds((double)config.CacheTtlSeconds));

                        _cache.Set(CACHE_KEY, response, cacheEntryOptions);
                        LogTrace($"Health check response writer writing status as: {dabHealthCheckReport.Status}");
                    }
                    
                    // Ensure cachedResponse is not null before calling WriteAsync
                    if (response != null)
                    {
                        // Return the cached or newly generated response
                        await context.Response.WriteAsync(response);
                    }
                    else
                    {
                        // Handle the case where cachedResponse is still null
                        LogTrace("Error: The cached health check response is null.");
                        context.Response.StatusCode = 500; // Internal Server Error
                        await context.Response.WriteAsync("Failed to generate health check response.");
                    }
                }
                else
                {
                    ComprehensiveHealthCheckReport dabHealthCheckReport = _healthCheckHelper.GetHealthCheckResponse(context, config);
                    response = JsonSerializer.Serialize(dabHealthCheckReport, options: new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
                    LogTrace($"Health check response writer writing status as: {dabHealthCheckReport.Status}");
                    await context.Response.WriteAsync(response);

                }                
            }
            else
            {
                LogTrace("Comprehensive Health Check Report Not Found: 404 Not Found.");
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.CompleteAsync();
            }

            return;
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
