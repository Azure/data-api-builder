// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.HealthCheck;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Product;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Azure.DataApiBuilder.Service.HealthCheck
{
    /// <summary>
    /// Creates a JSON response of health report by executing the Datasource, Rest and Graphql endpoints.
    /// Checks the response time with the threshold given to formulate the comprehensive report.
    /// </summary>
    public class HealthCheckHelper
    {
        // Dependencies
        private ILogger<HealthCheckHelper> _logger;
        private HttpUtilities _httpUtility;
        private string _incomingRoleHeader = string.Empty;
        private string _incomingRoleToken = string.Empty;

        private const string TIME_EXCEEDED_ERROR_MESSAGE = "The threshold for executing the request has exceeded.";

        /// <summary>
        /// Constructor to inject the logger and HttpUtility class.
        /// </summary>
        /// <param name="logger">Logger to track the log statements.</param>
        /// <param name="httpUtility">HttpUtility to call methods from the internal class.</param>
        public HealthCheckHelper(ILogger<HealthCheckHelper> logger, HttpUtilities httpUtility)
        {
            _logger = logger;
            _httpUtility = httpUtility;
        }

        /// <summary>
        /// GetHealthCheckResponse is the main function which fetches the HttpContext and then creates the comprehensive health check report.
        /// Serializes the report to JSON and returns the response.  
        /// </summary>
        /// <param name="runtimeConfig">RuntimeConfig</param>
        /// <returns>This function returns the comprehensive health report after calculating the response time of each datasource, rest and graphql health queries.</returns>
        public async Task<ComprehensiveHealthCheckReport> GetHealthCheckResponseAsync(RuntimeConfig runtimeConfig)
        {
            // Create a JSON response for the comprehensive health check endpoint using the provided basic health report.
            // If the response has already been created, it will be reused.
            _logger.LogTrace("Comprehensive Health check is enabled in the runtime configuration.");

            ComprehensiveHealthCheckReport ComprehensiveHealthCheckReport = new();
            UpdateVersionAndAppName(ref ComprehensiveHealthCheckReport);
            UpdateTimestampOfResponse(ref ComprehensiveHealthCheckReport);
            UpdateDabConfigurationDetails(ref ComprehensiveHealthCheckReport, runtimeConfig);
            await UpdateHealthCheckDetailsAsync(ComprehensiveHealthCheckReport, runtimeConfig);
            UpdateOverallHealthStatus(ref ComprehensiveHealthCheckReport);
            return ComprehensiveHealthCheckReport;
        }

        // Updates the incoming role header with the appropriate value from the request headers.
        public void StoreIncomingRoleHeader(HttpContext httpContext)
        {
            StringValues clientRoleHeader = httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER];
            StringValues clientTokenHeader = httpContext.Request.Headers[AuthenticationOptions.CLIENT_PRINCIPAL_HEADER];

            if (clientRoleHeader.Count > 1 || clientTokenHeader.Count > 1)
            {
                throw new ArgumentException("Multiple values for the client role or token header are not allowed.");
            }

            // Role Header is not present in the request, set it to anonymous.
            if (clientRoleHeader.Count == 1)
            {
                _incomingRoleHeader = clientRoleHeader.ToString().ToLowerInvariant();
            }

            if (clientTokenHeader.Count == 1)
            {
                _incomingRoleToken = clientTokenHeader.ToString();
            }
        }

        /// <summary>
        /// Checks if the incoming request is allowed to access the health check endpoint.
        /// Anonymous requests are only allowed in Development Mode.
        /// </summary>
        /// <param name="httpContext">HttpContext to get the headers.</param>
        /// <param name="hostMode">Compare with the HostMode of DAB</param>
        /// <param name="allowedRoles">AllowedRoles in the Runtime.Health config</param>
        /// <returns></returns>
        public bool IsUserAllowedToAccessHealthCheck(HttpContext httpContext, bool isDevelopmentMode, HashSet<string> allowedRoles)
        {
            if (allowedRoles == null || allowedRoles.Count == 0)
            {
                // When allowedRoles is null or empty, all roles are allowed if Mode = Development.
                return isDevelopmentMode;
            }

            return allowedRoles.Contains(_incomingRoleHeader);
        }

        // Updates the overall status by comparing all the internal HealthStatuses in the response.
        private static void UpdateOverallHealthStatus(ref ComprehensiveHealthCheckReport comprehensiveHealthCheckReport)
        {
            if (comprehensiveHealthCheckReport.Checks == null)
            {
                comprehensiveHealthCheckReport.Status = HealthStatus.Healthy;
                return;
            }

            comprehensiveHealthCheckReport.Status = comprehensiveHealthCheckReport.Checks?.Any(check => check.Status == HealthStatus.Unhealthy) == true
                ? HealthStatus.Unhealthy
                : HealthStatus.Healthy;
        }

        // Updates the AppName and Version for the Health report.
        private static void UpdateVersionAndAppName(ref ComprehensiveHealthCheckReport response)
        {
            // Update the version and app name to the response.
            response.Version = ProductInfo.GetProductVersion();
            response.AppName = ProductInfo.GetDataApiBuilderUserAgent();
        }

        // Updates the timestamp for the Health report.
        private static void UpdateTimestampOfResponse(ref ComprehensiveHealthCheckReport response)
        {
            response.TimeStamp = DateTime.UtcNow;
        }

        // Updates the DAB configuration details coming from RuntimeConfig for the Health report.
        private static void UpdateDabConfigurationDetails(ref ComprehensiveHealthCheckReport ComprehensiveHealthCheckReport, RuntimeConfig runtimeConfig)
        {
            ComprehensiveHealthCheckReport.ConfigurationDetails = new ConfigurationDetails
            {
                Rest = runtimeConfig.IsRestEnabled,
                GraphQL = runtimeConfig.IsGraphQLEnabled,
                Caching = runtimeConfig.IsCachingEnabled,
                Telemetry = runtimeConfig?.Runtime?.Telemetry != null,
                Mode = runtimeConfig?.Runtime?.Host?.Mode ?? HostMode.Production, // Modify to runtimeConfig.HostMode in Roles PR
            };
        }

        // Main function to internally call for data source and entities health check.
        private async Task UpdateHealthCheckDetailsAsync(ComprehensiveHealthCheckReport ComprehensiveHealthCheckReport, RuntimeConfig runtimeConfig)
        {
            ComprehensiveHealthCheckReport.Checks = new List<HealthCheckResultEntry>();
            await UpdateDataSourceHealthCheckResultsAsync(ComprehensiveHealthCheckReport, runtimeConfig);
            await UpdateEntityHealthCheckResultsAsync(ComprehensiveHealthCheckReport, runtimeConfig);
        }

        // Updates the DataSource Health Check Results in the response.
        private async Task UpdateDataSourceHealthCheckResultsAsync(ComprehensiveHealthCheckReport ComprehensiveHealthCheckReport, RuntimeConfig runtimeConfig)
        {
            if (ComprehensiveHealthCheckReport.Checks != null && runtimeConfig.DataSource.IsDatasourceHealthEnabled)
            {
                string query = Utilities.GetDatSourceQuery(runtimeConfig.DataSource.DatabaseType);
                (int, string?) response = await ExecuteDatasourceQueryCheckAsync(query, runtimeConfig.DataSource.ConnectionString);
                bool isResponseTimeWithinThreshold = response.Item1 >= 0 && response.Item1 < runtimeConfig.DataSource.DatasourceThresholdMs;

                // Add DataSource Health Check Results
                ComprehensiveHealthCheckReport.Checks.Add(new HealthCheckResultEntry
                {
                    Name = runtimeConfig?.DataSource?.Health?.Name ?? runtimeConfig?.DataSource?.DatabaseType.ToString(),
                    ResponseTimeData = new ResponseTimeData
                    {
                        ResponseTimeMs = response.Item1,
                        ThresholdMs = runtimeConfig?.DataSource.DatasourceThresholdMs
                    },
                    Exception = !isResponseTimeWithinThreshold ? TIME_EXCEEDED_ERROR_MESSAGE : response.Item2,
                    Tags = [HealthCheckConstants.DATASOURCE],
                    Status = isResponseTimeWithinThreshold ? HealthStatus.Healthy : HealthStatus.Unhealthy
                });
            }
        }

        // Executes the DB Query and keeps track of the response time and error message.
        private async Task<(int, string?)> ExecuteDatasourceQueryCheckAsync(string query, string connectionString)
        {
            string? errorMessage = null;
            if (!string.IsNullOrEmpty(query) && !string.IsNullOrEmpty(connectionString))
            {
                Stopwatch stopwatch = new();
                stopwatch.Start();
                errorMessage = await _httpUtility.ExecuteDbQueryAsync(query, connectionString);
                stopwatch.Stop();
                return string.IsNullOrEmpty(errorMessage) ? ((int)stopwatch.ElapsedMilliseconds, errorMessage) : (HealthCheckConstants.ERROR_RESPONSE_TIME_MS, errorMessage);
            }

            return (HealthCheckConstants.ERROR_RESPONSE_TIME_MS, errorMessage);
        }

        // Updates the Entity Health Check Results in the response. 
        // Goes through the entities one by one and executes the rest and graphql checks (if enabled).
        private async Task UpdateEntityHealthCheckResultsAsync(ComprehensiveHealthCheckReport report, RuntimeConfig runtimeConfig)
        {
            List<KeyValuePair<string, Entity>> enabledEntities = runtimeConfig.Entities.Entities
                .Where(e => e.Value.IsEntityHealthEnabled)
                .ToList();

            if (enabledEntities.Count == 0)
            {
                _logger.LogInformation("No enabled entities found for health checks. Skipping entity health checks.");
                return;
            }

            ConcurrentBag<HealthCheckResultEntry> concurrentChecks = new();

            // Use MaxQueryParallelism from RuntimeConfig or default to RuntimeHealthCheckConfig.DEFAULT_MAX_QUERY_PARALLELISM
            int maxParallelism = runtimeConfig.Runtime?.Health?.MaxQueryParallelism ?? RuntimeHealthCheckConfig.DEFAULT_MAX_QUERY_PARALLELISM;

            _logger.LogInformation("Executing health checks for {Count} enabled entities with parallelism of {MaxParallelism}.", enabledEntities.Count, maxParallelism);

            // Executes health checks for all enabled entities in parallel, with a maximum degree of parallelism
            // determined by configuration (or a default). Each entity's health check runs as an independent task.
            // Results are collected in a thread-safe ConcurrentBag. This approach significantly improves performance
            // for large numbers of entities by utilizing available CPU and I/O resources efficiently.
            await Parallel.ForEachAsync(enabledEntities, new ParallelOptions { MaxDegreeOfParallelism = maxParallelism }, async (entity, _) =>
            {
                try
                {
                    ComprehensiveHealthCheckReport localReport = new()
                    {
                        Checks = new List<HealthCheckResultEntry>()
                    };

                    await PopulateEntityHealthAsync(localReport, entity, runtimeConfig);

                    if (localReport.Checks != null)
                    {
                        foreach (HealthCheckResultEntry check in localReport.Checks)
                        {
                            concurrentChecks.Add(check);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing entity '{EntityKey}'", entity.Key);
                }
            });

            report.Checks ??= new List<HealthCheckResultEntry>();
            report.Checks.AddRange(concurrentChecks);
        }

        // Populates the Entity Health Check Results in the response for a particular entity.
        // Checks for Rest enabled and executes the rest query.
        // Checks for GraphQL enabled and executes the graphql query.
        private async Task PopulateEntityHealthAsync(ComprehensiveHealthCheckReport ComprehensiveHealthCheckReport, KeyValuePair<string, Entity> entity, RuntimeConfig runtimeConfig)
        {
            // Global Rest and GraphQL Runtime Options
            RuntimeOptions? runtimeOptions = runtimeConfig.Runtime;

            string entityKeyName = entity.Key;
            // Entity Health Check and Runtime Options
            Entity entityValue = entity.Value;

            if (runtimeOptions != null && entityValue != null)
            {
                if (runtimeOptions.IsRestEnabled && entityValue.IsRestEnabled)
                {
                    ComprehensiveHealthCheckReport.Checks ??= new List<HealthCheckResultEntry>();

                    // In case of REST API, use the path specified in [entity.path] (if present).
                    // The path is trimmed to remove the leading '/' character.
                    // If the path is not present, use the entity key name as the path.
                    string entityPath = entityValue.Rest.Path != null ? entityValue.Rest.Path.TrimStart('/') : entityKeyName;
                    (int, string?) response = await ExecuteRestEntityQueryAsync(runtimeConfig.RestPath, entityPath, entityValue.EntityFirst);
                    bool isResponseTimeWithinThreshold = response.Item1 >= 0 && response.Item1 < entityValue.EntityThresholdMs;

                    // Add Entity Health Check Results
                    ComprehensiveHealthCheckReport.Checks.Add(new HealthCheckResultEntry
                    {
                        Name = entityKeyName,
                        ResponseTimeData = new ResponseTimeData
                        {
                            ResponseTimeMs = response.Item1,
                            ThresholdMs = entityValue.EntityThresholdMs
                        },
                        Tags = [HealthCheckConstants.REST, HealthCheckConstants.ENDPOINT],
                        Exception = response.Item2 ?? (!isResponseTimeWithinThreshold ? TIME_EXCEEDED_ERROR_MESSAGE : null),
                        Status = isResponseTimeWithinThreshold ? HealthStatus.Healthy : HealthStatus.Unhealthy
                    });
                }

                if (runtimeOptions.IsGraphQLEnabled && entityValue.IsGraphQLEnabled)
                {
                    ComprehensiveHealthCheckReport.Checks ??= new List<HealthCheckResultEntry>();

                    (int, string?) response = await ExecuteGraphQLEntityQueryAsync(runtimeConfig.GraphQLPath, entityValue, entityKeyName);
                    bool isResponseTimeWithinThreshold = response.Item1 >= 0 && response.Item1 < entityValue.EntityThresholdMs;

                    ComprehensiveHealthCheckReport.Checks.Add(new HealthCheckResultEntry
                    {
                        Name = entityKeyName,
                        ResponseTimeData = new ResponseTimeData
                        {
                            ResponseTimeMs = response.Item1,
                            ThresholdMs = entityValue.EntityThresholdMs
                        },
                        Tags = [HealthCheckConstants.GRAPHQL, HealthCheckConstants.ENDPOINT],
                        Exception = response.Item2 ?? (!isResponseTimeWithinThreshold ? TIME_EXCEEDED_ERROR_MESSAGE : null),
                        Status = isResponseTimeWithinThreshold ? HealthStatus.Healthy : HealthStatus.Unhealthy
                    });
                }
            }
        }

        // Executes the Rest Entity Query and keeps track of the response time and error message.
        private async Task<(int, string?)> ExecuteRestEntityQueryAsync(string restUriSuffix, string entityName, int first)
        {
            string? errorMessage = null;
            if (!string.IsNullOrEmpty(entityName))
            {
                Stopwatch stopwatch = new();
                stopwatch.Start();
                errorMessage = await _httpUtility.ExecuteRestQueryAsync(restUriSuffix, entityName, first, _incomingRoleHeader, _incomingRoleToken);
                stopwatch.Stop();
                return string.IsNullOrEmpty(errorMessage) ? ((int)stopwatch.ElapsedMilliseconds, errorMessage) : (HealthCheckConstants.ERROR_RESPONSE_TIME_MS, errorMessage);
            }

            return (HealthCheckConstants.ERROR_RESPONSE_TIME_MS, errorMessage);
        }

        // Executes the GraphQL Entity Query and keeps track of the response time and error message.
        private async Task<(int, string?)> ExecuteGraphQLEntityQueryAsync(string graphqlUriSuffix, Entity entity, string entityName)
        {
            string? errorMessage = null;
            if (entity != null)
            {
                Stopwatch stopwatch = new();
                stopwatch.Start();
                errorMessage = await _httpUtility.ExecuteGraphQLQueryAsync(graphqlUriSuffix, entityName, entity, _incomingRoleHeader, _incomingRoleToken);
                stopwatch.Stop();
                return string.IsNullOrEmpty(errorMessage) ? ((int)stopwatch.ElapsedMilliseconds, errorMessage) : (HealthCheckConstants.ERROR_RESPONSE_TIME_MS, errorMessage);
            }

            return (HealthCheckConstants.ERROR_RESPONSE_TIME_MS, errorMessage);
        }
    }
}
