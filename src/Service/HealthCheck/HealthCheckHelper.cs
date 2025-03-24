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
            StringValues clientRoleHeader = httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER];

            if (clientRoleHeader.Count > 1)
            {
                // When count > 1, multiple header fields with the same field-name
                // are present in a message, but are NOT supported, specifically for the client role header.
                // Valid scenario per HTTP Spec: http://www.w3.org/Protocols/rfc2616/rfc2616-sec4.html#sec4.2
                // Discussion: https://stackoverflow.com/a/3097052/18174950
                return false;
            }

            if (clientRoleHeader.Count == 0)
            {
                // When count = 0, the clientRoleHeader is absent on requests.
                // Consequentially, anonymous requests are only allowed in Development Mode.
                return hostMode == HostMode.Development;
            }

            string clientRoleHeaderValue = clientRoleHeader.ToString().ToLowerInvariant();

            switch (hostMode)
            {
                // In case Mode is Development, then we consider the anonymous role and NA as a valid role.
                case HostMode.Development:
                    return string.IsNullOrEmpty(clientRoleHeaderValue) ||
                    clientRoleHeaderValue == AuthorizationResolver.ROLE_ANONYMOUS ||
                    allowedRoles.Contains(clientRoleHeaderValue);
                case HostMode.Production:
                    return allowedRoles.Contains(clientRoleHeaderValue);
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
                        Exception = !thresholdCheck ? _timeExceededErrorMessage : response.Item2,
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
                        Exception = !thresholdCheck ? _timeExceededErrorMessage : response.Item2,
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
