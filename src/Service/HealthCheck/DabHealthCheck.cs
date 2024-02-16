// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Product;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Azure.DataApiBuilder.Service.HealthCheck
{
    internal class DabHealthCheck : IHealthCheck
    {
        private RuntimeConfigProvider _runtimeConfigProvider;
        private IMetadataProviderFactory _metadataProviderFactory;
        public DabHealthCheck(RuntimeConfigProvider runtimeConfigProvider, IMetadataProviderFactory metadataProviderFactory)
        {
            _runtimeConfigProvider = runtimeConfigProvider;
            _metadataProviderFactory = metadataProviderFactory;
        }

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            bool isHealthy = true;

            // ...
            Dictionary<string, object> dabVersionMetadata = new()
            {
                { "version", ProductInfo.GetProductVersion() },
                { "appName", ProductInfo.GetDataApiBuilderApplicationName() },
                { "runtimeConfigLoadComplete", _runtimeConfigProvider.TryGetLoadedConfig(out _) },
                { "metadataProviderinitSuccess", _metadataProviderFactory.GetMetadataProviderLoadStatus() }
            };

            if (isHealthy)
            {
                return Task.FromResult(
                    HealthCheckResult.Healthy(
                        description: "A healthy result.",
                        data: dabVersionMetadata));
            }

            return Task.FromResult(
                new HealthCheckResult(
                    status: context.Registration.FailureStatus, 
                    description: "An unhealthy result.",
                    data: dabVersionMetadata));
        }
    }
}
