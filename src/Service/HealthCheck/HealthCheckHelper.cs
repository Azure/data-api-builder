// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Azure.DataApiBuilder.Config.HealthCheck;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Product;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

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
        /// <param name="context">HttpContext</param>
        /// <param name="runtimeConfig">RuntimeConfig</param>
        /// <returns>This function returns the comprehensive health report after calculating the response time of each datasource, rest and graphql health queries.</returns>
        public ComprehensiveHealthCheckReport GetHealthCheckResponse(HttpContext context, RuntimeConfig runtimeConfig)
        {
            // Create a JSON response for the comprehensive health check endpoint using the provided basic health report.
            // If the response has already been created, it will be reused.
            _httpUtility.ConfigureApiRoute(context);
            LogTrace("Comprehensive Health check is enabled in the runtime configuration.");

            ComprehensiveHealthCheckReport ComprehensiveHealthCheckReport = new();
            UpdateVersionAndAppName(ref ComprehensiveHealthCheckReport);
            UpdateDabConfigurationDetails(ref ComprehensiveHealthCheckReport, runtimeConfig);
            UpdateHealthCheckDetails(ref ComprehensiveHealthCheckReport, runtimeConfig);
            UpdateOverallHealthStatus(ref ComprehensiveHealthCheckReport);
            return ComprehensiveHealthCheckReport;
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
        private void UpdateHealthCheckDetails(ref ComprehensiveHealthCheckReport ComprehensiveHealthCheckReport, RuntimeConfig runtimeConfig)
        {
            ComprehensiveHealthCheckReport.Checks = new List<HealthCheckResultEntry>();
            UpdateDataSourceHealthCheckResults(ref ComprehensiveHealthCheckReport, runtimeConfig);
            UpdateEntityHealthCheckResults(ref ComprehensiveHealthCheckReport, runtimeConfig);
        }

        // Updates the DataSource Health Check Results in the response.
        private void UpdateDataSourceHealthCheckResults(ref ComprehensiveHealthCheckReport ComprehensiveHealthCheckReport, RuntimeConfig runtimeConfig)
        {
            if (ComprehensiveHealthCheckReport.Checks != null && runtimeConfig.DataSource.IsDatasourceHealthEnabled)
            {
                string query = Utilities.GetDatSourceQuery(runtimeConfig.DataSource.DatabaseType);
                (int, string?) response = ExecuteDatasourceQueryCheck(query, runtimeConfig.DataSource.ConnectionString);
                bool IsResponseTimeWithinThreshold = response.Item1 >= 0 && response.Item1 < runtimeConfig.DataSource.DatasourceThresholdMs;

                // Add DataSource Health Check Results
                ComprehensiveHealthCheckReport.Checks.Add(new HealthCheckResultEntry
                {
                    Name = runtimeConfig?.DataSource?.Health?.Name ?? runtimeConfig?.DataSource?.DatabaseType.ToString(),
                    ResponseTimeData = new ResponseTimeData
                    {
                        ResponseTimeMs = response.Item1,
                        ThresholdMs = runtimeConfig?.DataSource.DatasourceThresholdMs
                    },
                    Exception = !IsResponseTimeWithinThreshold ? TIME_EXCEEDED_ERROR_MESSAGE : response.Item2,
                    Tags = [HealthCheckConstants.DATASOURCE],
                    Status = IsResponseTimeWithinThreshold ? HealthStatus.Healthy : HealthStatus.Unhealthy
                });
            }
        }

        // Executes the DB Query and keeps track of the response time and error message.
        private (int, string?) ExecuteDatasourceQueryCheck(string query, string connectionString)
        {
            string? errorMessage = null;
            if (!string.IsNullOrEmpty(query) && !string.IsNullOrEmpty(connectionString))
            {
                Stopwatch stopwatch = new();
                stopwatch.Start();
                errorMessage = _httpUtility.ExecuteDbQuery(query, connectionString);
                stopwatch.Stop();
                return string.IsNullOrEmpty(errorMessage) ? ((int)stopwatch.ElapsedMilliseconds, errorMessage) : (HealthCheckConstants.ERROR_RESPONSE_TIME_MS, errorMessage);
            }

            return (HealthCheckConstants.ERROR_RESPONSE_TIME_MS, errorMessage);
        }

        // Updates the Entity Health Check Results in the response. 
        // Goes through the entities one by one and executes the rest and graphql checks (if enabled).
        private void UpdateEntityHealthCheckResults(ref ComprehensiveHealthCheckReport ComprehensiveHealthCheckReport, RuntimeConfig runtimeConfig)
        {
            if (runtimeConfig?.Entities != null && runtimeConfig.Entities.Entities.Any())
            {
                foreach (KeyValuePair<string, Entity> Entity in runtimeConfig.Entities.Entities)
                {
                    if (Entity.Value.IsEntityHealthEnabled)
                    {
                        PopulateEntityHealth(ComprehensiveHealthCheckReport, Entity, runtimeConfig);
                    }
                }
            }
        }

        // Populates the Entity Health Check Results in the response for a particular entity.
        // Checks for Rest enabled and executes the rest query.
        // Checks for GraphQL enabled and executes the graphql query.
        private void PopulateEntityHealth(ComprehensiveHealthCheckReport ComprehensiveHealthCheckReport, KeyValuePair<string, Entity> entity, RuntimeConfig runtimeConfig)
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
                    string entityPath = entityValue.Rest.Path != null ? entityValue.Rest.Path.TrimStart('/') : entityKeyName;
                    (int, string?) response = ExecuteRestEntityQuery(runtimeConfig.RestPath, entityPath, entityValue.EntityFirst);
                    bool IsResponseTimeWithinThreshold = response.Item1 >= 0 && response.Item1 < entityValue.EntityThresholdMs;

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
                        Exception = !IsResponseTimeWithinThreshold ? TIME_EXCEEDED_ERROR_MESSAGE : response.Item2,
                        Status = IsResponseTimeWithinThreshold ? HealthStatus.Healthy : HealthStatus.Unhealthy
                    });
                }

                if (runtimeOptions.IsGraphQLEnabled && entityValue.IsGraphQLEnabled)
                {
                    ComprehensiveHealthCheckReport.Checks ??= new List<HealthCheckResultEntry>();

                    (int, string?) response = ExecuteGraphQLEntityQuery(runtimeConfig.GraphQLPath, entityValue, entityKeyName);
                    bool IsResponseTimeWithinThreshold = response.Item1 >= 0 && response.Item1 < entityValue.EntityThresholdMs;

                    ComprehensiveHealthCheckReport.Checks.Add(new HealthCheckResultEntry
                    {
                        Name = entityKeyName,
                        ResponseTimeData = new ResponseTimeData
                        {
                            ResponseTimeMs = response.Item1,
                            ThresholdMs = entityValue.EntityThresholdMs
                        },
                        Tags = [HealthCheckConstants.GRAPHQL, HealthCheckConstants.ENDPOINT],
                        Exception = !IsResponseTimeWithinThreshold ? TIME_EXCEEDED_ERROR_MESSAGE : response.Item2,
                        Status = IsResponseTimeWithinThreshold ? HealthStatus.Healthy : HealthStatus.Unhealthy
                    });
                }
            }
        }

        // Executes the Rest Entity Query and keeps track of the response time and error message.
        private (int, string?) ExecuteRestEntityQuery(string restUriSuffix, string entityName, int first)
        {
            string? errorMessage = null;
            if (!string.IsNullOrEmpty(entityName))
            {
                Stopwatch stopwatch = new();
                stopwatch.Start();
                errorMessage = _httpUtility.ExecuteRestQuery(restUriSuffix, entityName, first);
                stopwatch.Stop();
                return string.IsNullOrEmpty(errorMessage) ? ((int)stopwatch.ElapsedMilliseconds, errorMessage) : (HealthCheckConstants.ERROR_RESPONSE_TIME_MS, errorMessage);
            }

            return (HealthCheckConstants.ERROR_RESPONSE_TIME_MS, errorMessage);
        }

        // Executes the GraphQL Entity Query and keeps track of the response time and error message.
        private (int, string?) ExecuteGraphQLEntityQuery(string graphqlUriSuffix, Entity entity, string entityName)
        {
            string? errorMessage = null;
            if (entity != null)
            {
                Stopwatch stopwatch = new();
                stopwatch.Start();
                errorMessage = _httpUtility.ExecuteGraphQLQuery(graphqlUriSuffix, entityName, entity);
                stopwatch.Stop();
                return string.IsNullOrEmpty(errorMessage) ? ((int)stopwatch.ElapsedMilliseconds, errorMessage) : (HealthCheckConstants.ERROR_RESPONSE_TIME_MS, errorMessage);
            }

            return (HealthCheckConstants.ERROR_RESPONSE_TIME_MS, errorMessage);
        }

        // <summary>
        /// Logs a trace message if a logger is present and the logger is enabled for trace events.
        /// </summary>
        /// <param name="message">Message to emit.</param>
        private void LogTrace(string message)
        {
            _logger.LogTrace(message);
        }
    }
}
