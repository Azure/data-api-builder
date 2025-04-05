// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Product;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Azure.DataApiBuilder.Service.HealthCheck
{
    /// <summary>
    /// Health check which returns the DAB engine's version and app name (User Agent string).
    /// - version: Major.Minor.Patch
    /// - app-name: dab_oss_Major.Minor.Patch
    /// </summary>
    public class BasicHealthCheck : IHealthCheck
    {
        public const string DAB_VERSION_KEY = "version";
        public const string DAB_APPNAME_KEY = "app-name";

        /// <summary>
        /// Method to check the health of the DAB engine which is executed by dotnet internals when registered as a health check
        /// in startup.cs
        /// </summary>
        /// <param name="context">dotnet provided health check context.</param>
        /// <param name="cancellationToken">cancellation token.</param>
        /// <returns>HealthCheckResult with version and appname/useragent string.</returns>
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            Dictionary<string, object> dabVersionMetadata = new()
            {
                { DAB_VERSION_KEY, ProductInfo.GetProductVersion() },
                { DAB_APPNAME_KEY, ProductInfo.GetDataApiBuilderUserAgent() }
            };

            HealthCheckResult healthCheckResult = HealthCheckResult.Healthy(
                description: "Healthy",
                data: dabVersionMetadata);

            return Task.FromResult(healthCheckResult);
        }
    }
}
