// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Service.HealthCheck
{
    /// <summary>
    /// Creates a JSON response for the health check endpoint using the provided health report.
    /// If the response has already been created, it will be reused.
    /// </summary>
    public class HealthCheckUtility
    {
        // Dependencies
        private ILogger? _logger;
        private HttpUtilities _httpUtility;

        public HealthCheckUtility(ILogger<HealthCheckUtility>? logger, HttpUtilities httpUtility)
        {
            _logger = logger;
            _httpUtility = httpUtility;
        }

        public async Task<DabHealthCheckReport> GetHealthCheckResponse(HealthReport healthReport, RuntimeConfig runtimeConfig)
        {
            // Create a JSON response for the health check endpoint using the provided health report.
            // If the response has already been created, it will be reused.
            if (runtimeConfig?.Runtime != null && runtimeConfig.Runtime?.Health != null && runtimeConfig.Runtime.Health.Enabled)
            {
                LogTrace("Enhanced Health check is enabled in the runtime configuration.");
                DabHealthCheckReport dabHealthCheckReport = new()
                {
                    HealthStatus = Config.ObjectModel.HealthStatus.Healthy
                };
                UpdateVersionAndAppName(ref dabHealthCheckReport, healthReport);
                UpdateDabConfigurationDetails(ref dabHealthCheckReport, runtimeConfig);
                await UpdateHealthCheckDetails(dabHealthCheckReport, runtimeConfig);
                return dabHealthCheckReport;
            }

            return new DabHealthCheckReport
            {
                HealthStatus = Config.ObjectModel.HealthStatus.Unhealthy
            };
        }

        private static void UpdateDabConfigurationDetails(ref DabHealthCheckReport dabHealthCheckReport, RuntimeConfig runtimeConfig)
        {
            dabHealthCheckReport.DabConfigurationDetails = new DabConfigurationDetails
            {
                Rest = runtimeConfig?.Runtime?.Rest != null && runtimeConfig.Runtime.Rest.Enabled,
                GraphQL = runtimeConfig?.Runtime?.GraphQL != null && runtimeConfig.Runtime.GraphQL.Enabled,
                Caching = runtimeConfig?.Runtime?.IsCachingEnabled ?? false,
                Telemetry = runtimeConfig?.Runtime?.Telemetry != null,
                Mode = runtimeConfig?.Runtime?.Host?.Mode ?? HostMode.Development,
            };
        }

        private async Task UpdateHealthCheckDetails(DabHealthCheckReport dabHealthCheckReport, RuntimeConfig runtimeConfig)
        {
            if (dabHealthCheckReport != null)
            {
                dabHealthCheckReport.HealthCheckResults = new DabHealthCheckResults()
                {
                    DataSourceHealthCheckResults = new List<HealthCheckDetailsResultEntry>(),
                    EntityHealthCheckResults = new List<HealthCheckEntityResultEntry>(),
                };

                if (runtimeConfig != null)
                {
                    UpdateDataSourceHealthCheckResults(ref dabHealthCheckReport, runtimeConfig);
                    await UpdateEntityHealthCheckResults(dabHealthCheckReport, runtimeConfig);
                }
            }
        }

        private async Task UpdateEntityHealthCheckResults(DabHealthCheckReport dabHealthCheckReport, RuntimeConfig runtimeConfig)
        {
            if (runtimeConfig?.Entities != null && dabHealthCheckReport?.HealthCheckResults?.EntityHealthCheckResults != null)
            {
                foreach (KeyValuePair<string, Entity> Entity in runtimeConfig.Entities.Entities)
                {
                    DabHealthCheckConfig? healthConfig = Entity.Value?.Health;
                    if (healthConfig != null && healthConfig.Enabled)
                    {
                        await PopulateEntityQuery(dabHealthCheckReport, Entity, runtimeConfig);
                    }
                }
            }
        }

        private async Task PopulateEntityQuery(DabHealthCheckReport dabHealthCheckReport, KeyValuePair<string, Entity> entity, RuntimeConfig runtimeConfig)
        {
            Dictionary<string, HealthCheckDetailsResultEntry> entityHealthCheckResults = new();
            if (runtimeConfig?.Runtime?.Rest?.Enabled ?? false)
            {
                string restSuffixPath = (entity.Value.Rest?.Path ?? entity.Key).TrimStart('/');
                int responseTime = ExecuteSqlEntityQuery(runtimeConfig.Runtime.Rest.Path, restSuffixPath, entity.Value?.Health?.First);
                if (responseTime >= 0 && responseTime <= entity.Value?.Health?.ThresholdMs)
                {
                    entityHealthCheckResults.Add("Rest", new HealthCheckDetailsResultEntry
                    {
                        ResponseTimeData = new ResponseTimeData
                        {
                            ResponseTimeMs = responseTime,
                            MaxAllowedResponseTimeMs = entity.Value?.Health?.ThresholdMs
                        },
                        HealthStatus = Config.ObjectModel.HealthStatus.Healthy
                    });
                }
                else
                {
                    entityHealthCheckResults.Add("Rest", new HealthCheckDetailsResultEntry
                    {
                        Exception = "The Entity is unavailable or response time exceeded the threshold.",
                        ResponseTimeData = new ResponseTimeData
                        {
                            ResponseTimeMs = responseTime,
                            MaxAllowedResponseTimeMs = entity.Value?.Health?.ThresholdMs
                        },
                        HealthStatus = Config.ObjectModel.HealthStatus.Unhealthy
                    });
                }
            }

            if (runtimeConfig?.Runtime?.GraphQL?.Enabled ?? false)
            {
                int responseTime = await ExecuteSqlGraphQLEntityQuery(runtimeConfig.Runtime.GraphQL.Path, entity.Key, entity.Value?.Source.Object, entity.Value?.Health?.First).ConfigureAwait(false);
                if (responseTime >= 0 && responseTime <= entity.Value?.Health?.ThresholdMs)
                {
                    entityHealthCheckResults.Add("GraphQL", new HealthCheckDetailsResultEntry
                    {
                        ResponseTimeData = new ResponseTimeData
                        {
                            ResponseTimeMs = responseTime,
                            MaxAllowedResponseTimeMs = entity.Value?.Health?.ThresholdMs
                        },
                        HealthStatus = Config.ObjectModel.HealthStatus.Healthy
                    });
                }
                else
                {
                    entityHealthCheckResults.Add("GraphQL", new HealthCheckDetailsResultEntry
                    {
                        Exception = "The Entity is unavailable or response time exceeded the threshold.",
                        ResponseTimeData = new ResponseTimeData
                        {
                            ResponseTimeMs = responseTime,
                            MaxAllowedResponseTimeMs = entity.Value?.Health?.ThresholdMs
                        },
                        HealthStatus = Config.ObjectModel.HealthStatus.Unhealthy
                    });
                }
            }

#pragma warning disable CS8602 // Dereference of a possibly null reference.
            dabHealthCheckReport?.HealthCheckResults?.EntityHealthCheckResults.Add(new HealthCheckEntityResultEntry
            {
                Name = entity.Key,
                EntityHealthCheckResults = entityHealthCheckResults
            });
#pragma warning restore CS8602 // Dereference of a possibly null reference.
        }

        private void UpdateDataSourceHealthCheckResults(ref DabHealthCheckReport dabHealthCheckReport, RuntimeConfig runtimeConfig)
        {
            if (runtimeConfig?.DataSource != null && runtimeConfig.DataSource?.Health != null && runtimeConfig.DataSource.Health.Enabled)
            {
                string query = runtimeConfig.DataSource?.Health.Query ?? string.Empty;
                int responseTime = ExecuteSqlDBQuery(query, runtimeConfig.DataSource?.ConnectionString);
                if (dabHealthCheckReport?.HealthCheckResults?.DataSourceHealthCheckResults != null)
                {
                    if (responseTime >= 0 && responseTime <= runtimeConfig?.DataSource?.Health.ThresholdMs)
                    {
                        dabHealthCheckReport.HealthCheckResults.DataSourceHealthCheckResults.Add(new HealthCheckDetailsResultEntry
                        {
                            Name = runtimeConfig?.DataSource?.Health.Moniker ?? Utilities.SqlServerMoniker,
                            ResponseTimeData = new ResponseTimeData
                            {
                                ResponseTimeMs = responseTime,
                                MaxAllowedResponseTimeMs = runtimeConfig?.DataSource?.Health.ThresholdMs
                            },
                            HealthStatus = Config.ObjectModel.HealthStatus.Healthy
                        });
                    }
                    else
                    {
                        dabHealthCheckReport.HealthCheckResults.DataSourceHealthCheckResults.Add(new HealthCheckDetailsResultEntry
                        {
                            Name = runtimeConfig?.DataSource?.Health.Moniker ?? Utilities.SqlServerMoniker,
                            Exception = "The response time exceeded the threshold.",
                            ResponseTimeData = new ResponseTimeData
                            {
                                ResponseTimeMs = responseTime,
                                MaxAllowedResponseTimeMs = runtimeConfig?.DataSource?.Health.ThresholdMs
                            },
                            HealthStatus = Config.ObjectModel.HealthStatus.Unhealthy
                        });
                    }
                }
            }

        }

        private int ExecuteSqlDBQuery(string query, string? connectionString)
        {
            if (!string.IsNullOrEmpty(query) && !string.IsNullOrEmpty(connectionString))
            {
                Stopwatch stopwatch = new();
                stopwatch.Start();
                bool isSuccess = _httpUtility.ExecuteDbQuery(query, connectionString);
                stopwatch.Stop();
                return isSuccess ? (int)stopwatch.ElapsedMilliseconds : -1;
            }

            return -1;
        }

        private int ExecuteSqlEntityQuery(string UriSuffix, string EntityName, int? First)
        {
            if (!string.IsNullOrEmpty(EntityName))
            {
                Stopwatch stopwatch = new();
                stopwatch.Start();
                bool isSuccess = _httpUtility.ExecuteEntityRestQuery(UriSuffix, EntityName, First ?? 1);
                stopwatch.Stop();
                return isSuccess ? (int)stopwatch.ElapsedMilliseconds : -1;
            }

            return -1;
        }
        private async Task<int> ExecuteSqlGraphQLEntityQuery(string UriSuffix, string EntityName, string? TableName, int? First)
        {
            if (!string.IsNullOrEmpty(EntityName))
            {
                Stopwatch stopwatch = new();
                stopwatch.Start();
                bool isSuccess = await _httpUtility.ExecuteEntityGraphQLQueryAsync(UriSuffix, EntityName, TableName ?? EntityName, First ?? 1);
                stopwatch.Stop();
                return isSuccess ? (int)stopwatch.ElapsedMilliseconds : -1;
            }

            return -1;
        }

        private void UpdateVersionAndAppName(ref DabHealthCheckReport response, HealthReport healthReport)
        {
            // Update the version and app name to the response.
            if (healthReport.Entries.TryGetValue(key: typeof(DabHealthCheck).Name, out HealthReportEntry healthReportEntry))
            {
                if (healthReportEntry.Data.TryGetValue(DabHealthCheck.DAB_VERSION_KEY, out object? versionValue) && versionValue is string versionNumber)
                {
                    response.Version = versionNumber;
                }
                else
                {
                    LogTrace("DabHealthCheck did not contain the version number in the HealthReport.");
                }

                if (healthReportEntry.Data.TryGetValue(DabHealthCheck.DAB_APPNAME_KEY, out object? appNameValue) && appNameValue is string appName)
                {
                    response.AppName = appName;
                }
                else
                {
                    LogTrace("DabHealthCheck did not contain the app name in the HealthReport.");
                }
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
            else
            {
                Console.WriteLine(message);
            }
        }
    }
}
