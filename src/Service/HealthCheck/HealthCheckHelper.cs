// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
        private ILogger? _logger;
        private HttpUtilities _httpUtility;
        private string _incomingRoleHeader = string.Empty;
        private string _incomingRoleToken = string.Empty;

        private string _timeExceededErrorMessage = "The threshold for executing the request has exceeded.";

        /// <summary>
        /// Constructor to inject the logger and HttpUtility class.
        /// </summary>
        /// <param name="logger">Logger to track the log statements.</param>
        /// <param name="httpUtility">HttpUtility to call methods from the internal class.</param>
        public HealthCheckHelper(ILogger<HealthCheckHelper>? logger, HttpUtilities httpUtility)
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
        /// <returns></returns>
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

        public void UpdateIncomingRoleHeader(HttpContext httpContext)
        {
            StringValues clientRoleHeader = httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER];
            StringValues clientTokenHeader = httpContext.Request.Headers[AuthenticationOptions.CLIENT_PRINCIPAL_HEADER];

            if (clientRoleHeader.Count == 1)
            {
                _incomingRoleHeader = clientRoleHeader.ToString().ToLowerInvariant();
            }

            if (clientRoleHeader.Count == 1)
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
        public bool IsUserAllowedToAccessHealthCheck(HttpContext httpContext, HostMode hostMode, List<string> allowedRoles)
        {
            if (allowedRoles == null || allowedRoles.Count == 0)
            {
                // When allowedRoles is null or empty, all roles are allowed if Mode = Development.
                return hostMode == HostMode.Development;
            }

            switch (hostMode)
            {
                case HostMode.Development:
                case HostMode.Production:
                    return allowedRoles.Contains(_incomingRoleHeader);
            }

            return false;
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
                Rest = runtimeConfig?.Runtime?.Rest != null && runtimeConfig.Runtime.Rest.Enabled,
                GraphQL = runtimeConfig?.Runtime?.GraphQL != null && runtimeConfig.Runtime.GraphQL.Enabled,
                Caching = runtimeConfig?.Runtime?.IsCachingEnabled ?? false,
                Telemetry = runtimeConfig?.Runtime?.Telemetry != null,
                Mode = runtimeConfig?.Runtime?.Host?.Mode ?? HostMode.Development,
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
                (int, string?) response = ExecuteDBQuery(query, runtimeConfig.DataSource.ConnectionString);
                bool thresholdCheck = response.Item1 >= 0 && response.Item1 < runtimeConfig.DataSource.DatasourceThresholdMs;

                // Add DataSource Health Check Results
                ComprehensiveHealthCheckReport.Checks.Add(new HealthCheckResultEntry
                {
                    Name = runtimeConfig?.DataSource?.Health?.Name ?? runtimeConfig?.DataSource?.DatabaseType.ToString(),
                    ResponseTimeData = new ResponseTimeData
                    {
                        ResponseTimeMs = response.Item1,
                        ThresholdMs = runtimeConfig?.DataSource.DatasourceThresholdMs
                    },
                    Exception = !thresholdCheck ? _timeExceededErrorMessage : response.Item2,
                    Tags = [HealthCheckConstants.DATASOURCE],
                    Status = thresholdCheck ? HealthStatus.Healthy : HealthStatus.Unhealthy
                });
            }
        }

        // Executes the DB Query and keeps track of the response time and error message.
        private (int, string?) ExecuteDBQuery(string query, string? connectionString)
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
                    bool thresholdCheck = response.Item1 >= 0 && response.Item1 < entityValue.EntityThresholdMs;

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
                        Exception = response.Item2 ?? (!thresholdCheck ? _timeExceededErrorMessage : null),
                        Status = thresholdCheck ? HealthStatus.Healthy : HealthStatus.Unhealthy
                    });
                }

                if (runtimeOptions.IsGraphQLEnabled && entityValue.IsGraphQLEnabled)
                {
                    ComprehensiveHealthCheckReport.Checks ??= new List<HealthCheckResultEntry>();

                    (int, string?) response = ExecuteGraphQLEntityQuery(runtimeConfig.GraphQLPath, entityValue, entityKeyName);
                    bool thresholdCheck = response.Item1 >= 0 && response.Item1 < entityValue.EntityThresholdMs;

                    ComprehensiveHealthCheckReport.Checks.Add(new HealthCheckResultEntry
                    {
                        Name = entityKeyName,
                        ResponseTimeData = new ResponseTimeData
                        {
                            ResponseTimeMs = response.Item1,
                            ThresholdMs = entityValue.EntityThresholdMs
                        },
                        Tags = [HealthCheckConstants.GRAPHQL, HealthCheckConstants.ENDPOINT],
                        Exception = response.Item2 ?? (!thresholdCheck ? _timeExceededErrorMessage : null),
                        Status = thresholdCheck ? HealthStatus.Healthy : HealthStatus.Unhealthy
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
                errorMessage = _httpUtility.ExecuteRestQuery(restUriSuffix, entityName, first, _incomingRoleHeader, _incomingRoleToken);
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
                errorMessage = _httpUtility.ExecuteGraphQLQuery(graphqlUriSuffix, entityName, entity, _incomingRoleHeader, _incomingRoleToken);
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
            if (_logger is not null && _logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogTrace(message);
            }
            else
            {
                Console.WriteLine(message);
            }
        }
    }
}
