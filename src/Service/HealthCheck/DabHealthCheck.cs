// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Product;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Azure.DataApiBuilder.Service.HealthCheck
{
    internal class DabHealthCheck : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            Dictionary<string, object> dabVersionMetadata = new()
            {
                { "version", ProductInfo.GetMajorMinorPatchVersion() },
                { "appName", ProductInfo.GetDataApiBuilderUserAgent(includeCommitHash: false) }
            };

            HealthCheckResult healthCheckResult = HealthCheckResult.Healthy(
                description: "Healthy",
                data: dabVersionMetadata);

            return Task.FromResult(healthCheckResult);
        }
    }
}
