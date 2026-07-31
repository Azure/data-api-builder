// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Mcp.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.DataApiBuilder.Service.Utilities
{
    /// <summary>
    /// Coordinates initial configuration-dependent service construction for HTTP and stdio.
    /// </summary>
    internal static class RuntimeInitializationHelper
    {
        /// <summary>
        /// Captures and validates the active configuration, initializes its database metadata,
        /// and publishes the initial MCP registry while excluding file-triggered hot reloads.
        /// </summary>
        /// <param name="serviceProvider">The application service provider.</param>
        /// <returns>The configuration generation initialized by this operation.</returns>
        public static async Task<RuntimeConfig> InitializeRuntimeDependenciesAsync(
            IServiceProvider serviceProvider)
        {
            ArgumentNullException.ThrowIfNull(serviceProvider);

            FileSystemRuntimeConfigLoader configLoader =
                serviceProvider.GetRequiredService<FileSystemRuntimeConfigLoader>();
            RuntimeConfig? initializedConfig = null;

            await configLoader.ExecuteWithHotReloadSerializationAsync(async () =>
            {
                RuntimeConfigProvider runtimeConfigProvider =
                    serviceProvider.GetRequiredService<RuntimeConfigProvider>();
                initializedConfig = runtimeConfigProvider.GetConfig();

                RuntimeConfigValidator runtimeConfigValidator =
                    serviceProvider.GetRequiredService<RuntimeConfigValidator>();
                runtimeConfigValidator.ValidateConfigProperties();

                IMetadataProviderFactory metadataProviderFactory =
                    serviceProvider.GetRequiredService<IMetadataProviderFactory>();
                await metadataProviderFactory.InitializeAsync().ConfigureAwait(false);

                // MCP services are absent when MCP was disabled at startup.
                IMcpToolRegistryRefreshService? mcpToolRegistryRefreshService =
                    serviceProvider.GetService<IMcpToolRegistryRefreshService>();
                mcpToolRegistryRefreshService?.EnsureInitialized();
            }).ConfigureAwait(false);

            return initializedConfig!;
        }
    }
}
