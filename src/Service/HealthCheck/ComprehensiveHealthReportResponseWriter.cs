// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ZiggyCreatures.Caching.Fusion;

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
        private readonly IFusionCache _cache;
        private const string CACHE_KEY = "HealthCheckResponse";

        public ComprehensiveHealthReportResponseWriter(
            ILogger<ComprehensiveHealthReportResponseWriter> logger,
            RuntimeConfigProvider runtimeConfigProvider,
            HealthCheckHelper healthCheckHelper,
            IFusionCache cache)
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
        public async Task WriteResponseAsync(HttpContext context)
        {
            RuntimeConfig config = _runtimeConfigProvider.GetConfig();

            // Global comprehensive Health Check Enabled
            if (config.IsHealthEnabled)
            {
                _healthCheckHelper.StoreIncomingRoleHeader(context);
                if (!_healthCheckHelper.IsUserAllowedToAccessHealthCheck(context, config.IsDevelopmentMode(), config.AllowedRolesForHealth))
                {
                    _logger.LogError("Comprehensive Health Check Report is not allowed: 403 Forbidden due to insufficient permissions.");
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.CompleteAsync();
                    return;
                }

                string? response;
                // Check if the cache is enabled 
                if (config.CacheTtlSecondsForHealthReport > 0)
                {
                    try
                    {
                        response = await _cache.GetOrSetAsync<string?>(
                            key: CACHE_KEY,
                            async (FusionCacheFactoryExecutionContext<string?> ctx, CancellationToken ct) =>
                            {
                                string? response = await ExecuteHealthCheckAsync(config).ConfigureAwait(false);
                                ctx.Options.SetDuration(TimeSpan.FromSeconds(config.CacheTtlSecondsForHealthReport));
                                return response;
                            });

                        _logger.LogTrace($"Health check response is fetched from cache with key: {CACHE_KEY} and TTL: {config.CacheTtlSecondsForHealthReport} seconds.");
                    }
                    catch (Exception ex)
                    {
                        response = null; // Set response to null in case of an error
                        _logger.LogError($"Error in caching health check response: {ex.Message}");
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
                        _logger.LogError("Error: The health check response is null.");
                        context.Response.StatusCode = 500; // Internal Server Error
                        await context.Response.WriteAsync("Failed to generate health check response.");
                    }
                }
                else
                {
                    response = await ExecuteHealthCheckAsync(config).ConfigureAwait(false);
                    // Return the newly generated response
                    await context.Response.WriteAsync(response);
                }
            }
            else
            {
                _logger.LogError("Comprehensive Health Check Report Not Found: 404 Not Found.");
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.CompleteAsync();
            }

            return;
        }

        private async Task<string> ExecuteHealthCheckAsync(RuntimeConfig config)
        {
            ComprehensiveHealthCheckReport dabHealthCheckReport = await _healthCheckHelper.GetHealthCheckResponseAsync(config);
            string response = JsonSerializer.Serialize(dabHealthCheckReport, options: new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
            _logger.LogTrace($"Health check response writer writing status as: {dabHealthCheckReport.Status}");

            return response;
        }
    }
}
