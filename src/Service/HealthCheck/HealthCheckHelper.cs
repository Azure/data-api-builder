// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Product;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Service.HealthCheck
{
    /// <summary>
    /// Creates a JSON response for the health check endpoint using the provided health report.
    /// If the response has already been created, it will be reused.
    /// </summary>
    public class HealthCheckHelper
    {
        // Dependencies
        private ILogger? _logger;
        private HttpUtilities _httpUtility;

        private string _timeExceededErrorMessage = "The threshold for executing the request exceeded";

        public HealthCheckHelper(ILogger<HealthCheckHelper>? logger, HttpUtilities httpUtility)
        {
            _logger = logger;
            _httpUtility = httpUtility;
        }

        public ComprehensiveHealthCheckReport GetHealthCheckResponse(HttpContext context, RuntimeConfig runtimeConfig)
        {
            // Create a JSON response for the comprehensive health check endpoint using the provided basic health report.
            // If the response has already been created, it will be reused.
            _httpUtility.ConfigureApiRoute(context);
            LogTrace("Comprehensive Health check is enabled in the runtime configuration.");
            // TODO: Update the overall health based on all individual health statuses
            ComprehensiveHealthCheckReport ComprehensiveHealthCheckReport = new()
            {
                Status = HealthStatus.Healthy,
            };
            UpdateVersionAndAppName(ref ComprehensiveHealthCheckReport);
            UpdateDabConfigurationDetails(ref ComprehensiveHealthCheckReport, runtimeConfig);
            UpdateHealthCheckDetails(ComprehensiveHealthCheckReport, runtimeConfig);
            return ComprehensiveHealthCheckReport;
        }

        private static void UpdateVersionAndAppName(ref ComprehensiveHealthCheckReport response)
        {
            // Update the version and app name to the response.
            response.Version = ProductInfo.GetProductVersion();
            response.AppName = ProductInfo.GetDataApiBuilderUserAgent();
        }

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

        private void UpdateHealthCheckDetails(ComprehensiveHealthCheckReport ComprehensiveHealthCheckReport, RuntimeConfig runtimeConfig)
        {
            ComprehensiveHealthCheckReport.Checks = new List<HealthCheckResultEntry>();
            UpdateDataSourceHealthCheckResults(ref ComprehensiveHealthCheckReport, runtimeConfig);
            UpdateEntityHealthCheckResults(ComprehensiveHealthCheckReport, runtimeConfig);
        }

        private void UpdateDataSourceHealthCheckResults(ref ComprehensiveHealthCheckReport ComprehensiveHealthCheckReport, RuntimeConfig runtimeConfig)
        {
            if (ComprehensiveHealthCheckReport.Checks != null && runtimeConfig.DataSource?.Health != null && runtimeConfig.DataSource.Health.Enabled)
            {
                string query = Utilities.GetDatSourceQuery(runtimeConfig.DataSource.DatabaseType);
                (int, string?) response = ExecuteSqlDBQuery(query, runtimeConfig.DataSource?.ConnectionString);
                bool thresholdCheck = response.Item1 >= 0 && response.Item1 < runtimeConfig?.DataSource?.Health.ThresholdMs;
                ComprehensiveHealthCheckReport.Checks.Add(new HealthCheckResultEntry
                {
                    Name = runtimeConfig?.DataSource?.Health?.Name ?? runtimeConfig?.DataSource?.DatabaseType.ToString(),
                    ResponseTimeData = new ResponseTimeData
                    {
                        DurationMs = response.Item1,
                        ThresholdMs = runtimeConfig?.DataSource?.Health?.ThresholdMs
                    },
                    Exception = !thresholdCheck ? _timeExceededErrorMessage : response.Item2,
                    Tags = ["data-source"],
                    Status = thresholdCheck ? HealthStatus.Healthy : HealthStatus.Unhealthy
                });
            }
        }

        private (int, string?) ExecuteSqlDBQuery(string query, string? connectionString)
        {
            string? errorMessage = null;
            if (!string.IsNullOrEmpty(query) && !string.IsNullOrEmpty(connectionString))
            {
                Stopwatch stopwatch = new();
                stopwatch.Start();
                errorMessage = _httpUtility.ExecuteDbQuery(query, connectionString);
                stopwatch.Stop();
                return string.IsNullOrEmpty(errorMessage) ? ((int)stopwatch.ElapsedMilliseconds, errorMessage) : (-1, errorMessage);
            }

            return (-1, errorMessage);
        }

        private void UpdateEntityHealthCheckResults(ComprehensiveHealthCheckReport ComprehensiveHealthCheckReport, RuntimeConfig runtimeConfig)
        {
            if (runtimeConfig?.Entities != null && runtimeConfig.Entities.Entities.Any())
            {
                foreach (KeyValuePair<string, Entity> Entity in runtimeConfig.Entities.Entities)
                {
                    EntityHealthCheckConfig? healthConfig = Entity.Value?.Health;
                    if (healthConfig != null && healthConfig.Enabled)
                    {
                        PopulateEntityHealth(ComprehensiveHealthCheckReport, Entity, runtimeConfig);
                    }
                }
            }
        }

        public bool IsFeatureEnabled<T>(T? featureOptions) where T : class
        {
            return featureOptions != null &&
                typeof(T).GetProperty("Enabled")?.GetValue(featureOptions) as bool? == true;
        }

#pragma warning disable CS8602 // Dereference of a possibly null reference.
        private void PopulateEntityHealth(ComprehensiveHealthCheckReport ComprehensiveHealthCheckReport, KeyValuePair<string, Entity> entity, RuntimeConfig runtimeConfig)
        {
            // Global Rest and GraphQL Runtime Options
            RestRuntimeOptions? restRuntimeOptions = runtimeConfig?.Runtime?.Rest;
            GraphQLRuntimeOptions? graphQLRuntimeOptions = runtimeConfig?.Runtime?.GraphQL;

            string entityKeyName = entity.Key;
            // Entity Health Check and Runtime Options
            EntityHealthCheckConfig? healthOptions = entity.Value.Health;
            EntityRestOptions? restEntityOptions = entity.Value.Rest;
            EntityGraphQLOptions? graphqlEntityOptions = entity.Value.GraphQL;

            if (healthOptions != null && healthOptions.Enabled)
            {
                if (IsFeatureEnabled(restRuntimeOptions) && IsFeatureEnabled(restEntityOptions))
                {
                    ComprehensiveHealthCheckReport.Checks ??= new List<HealthCheckResultEntry>();
                    string entityPath = restEntityOptions?.Path != null ? restEntityOptions.Path.TrimStart('/') : entityKeyName;
                    (int, string?) response = ExecuteSqlEntityQuery(restRuntimeOptions.Path, entityPath, healthOptions.First);
                    bool thresholdCheck = response.Item1 >= 0 && response.Item1 < healthOptions.ThresholdMs;

                    ComprehensiveHealthCheckReport.Checks.Add(new HealthCheckResultEntry
                    {
                        Name = entityKeyName,
                        ResponseTimeData = new ResponseTimeData
                        {
                            DurationMs = response.Item1,
                            ThresholdMs = healthOptions.ThresholdMs
                        },
                        Tags = ["rest", "endpoint"],
                        Exception = !thresholdCheck ? _timeExceededErrorMessage : response.Item2,
                        Status = thresholdCheck ? HealthStatus.Healthy : HealthStatus.Unhealthy
                    });
                }

                if (IsFeatureEnabled(graphQLRuntimeOptions) && IsFeatureEnabled(graphqlEntityOptions))
                {
                    ComprehensiveHealthCheckReport.Checks ??= new List<HealthCheckResultEntry>();

                    (int, string?) response = ExecuteSqlGraphQLEntityQuery(graphQLRuntimeOptions.Path, entity.Value, entityKeyName);
                    bool thresholdCheck = response.Item1 >= 0 && response.Item1 < healthOptions.ThresholdMs;

                    ComprehensiveHealthCheckReport.Checks.Add(new HealthCheckResultEntry
                    {
                        Name = entityKeyName,
                        ResponseTimeData = new ResponseTimeData
                        {
                            DurationMs = response.Item1,
                            ThresholdMs = healthOptions.ThresholdMs
                        },
                        Tags = ["graphql", "endpoint"],
                        Exception = !thresholdCheck ? _timeExceededErrorMessage : response.Item2,
                        Status = thresholdCheck ? HealthStatus.Healthy : HealthStatus.Unhealthy
                    });
                }
            }
        }
#pragma warning restore CS8602 // Dereference of a possibly null reference.

        private (int, string?) ExecuteSqlEntityQuery(string UriSuffix, string EntityName, int First)
        {
            string? errorMessage = null;
            if (!string.IsNullOrEmpty(EntityName))
            {
                Stopwatch stopwatch = new();
                stopwatch.Start();
                errorMessage = _httpUtility.ExecuteEntityRestQuery(UriSuffix, EntityName, First);
                stopwatch.Stop();
                return string.IsNullOrEmpty(errorMessage) ? ((int)stopwatch.ElapsedMilliseconds, errorMessage) : (-1, errorMessage);
            }

            return (-1, errorMessage);
        }

        private (int, string?) ExecuteSqlGraphQLEntityQuery(string UriSuffix, Entity entity, string entityName)
        {
            string? errorMessage = null;
            if (entity != null)
            {
                Stopwatch stopwatch = new();
                stopwatch.Start();
                errorMessage = _httpUtility.ExecuteEntityGraphQLQueryAsync(UriSuffix, entityName, entity);
                stopwatch.Stop();
                return string.IsNullOrEmpty(errorMessage) ? ((int)stopwatch.ElapsedMilliseconds, errorMessage) : (-1, errorMessage);
            }

            return (-1, errorMessage);
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
