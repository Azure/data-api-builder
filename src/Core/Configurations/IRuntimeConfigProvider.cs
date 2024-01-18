// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Core.Configurations
{
    /// <summary>
    /// Interface for interacting with the RuntimeConfig.
    /// </summary>
    public interface IRuntimeConfigProvider
    {
        /// <summary>
        /// Indicates whether the config was loaded after the runtime was initialized.
        /// </summary>
        /// <remarks>This is most commonly used when DAB's config is provided via the <c>ConfigurationController</c>, such as with HostedRuntimeConfigProvider.</remarks>
        public bool IsLateConfigured { get; set; }

        /// <summary>
        /// Handles the loading and access to the RuntimeConfig.
        /// </summary>
        public RuntimeConfigLoader ConfigLoader { get; set; }

        public delegate Task<bool> RuntimeConfigLoadedHandler(IRuntimeConfigProvider sender, RuntimeConfig config);

        public List<RuntimeConfigLoadedHandler> RuntimeConfigLoadedHandlers { get; }

        /// <summary>
        /// The access tokens representing a Managed Identity to connect to the database.
        /// The key is the unique datasource name and the value is the access token.
        /// </summary>
        public Dictionary<string, string?> ManagedIdentityAccessToken { get; set; }

        /// <summary>
        /// Return the previous loaded config, or it will attempt to load the config that
        /// is known by the loader.
        /// </summary>
        public RuntimeConfig GetConfig();

        /// <summary>
        /// Attempt to acquire runtime configuration metadata.
        /// </summary>
        public bool TryGetConfig([NotNullWhen(true)] out RuntimeConfig? runtimeConfig);

        /// <summary>
        /// Attempt to acquire runtime configuration metadata from a previously loaded one.
        /// This method will not load the config if it hasn't been loaded yet.
        /// </summary>
        public bool TryGetLoadedConfig([NotNullWhen(true)] out RuntimeConfig? runtimeConfig);

        /// <summary>
        /// Set the runtime configuration provider with the specified accessToken for the specified datasource.
        /// This initialization method is used to set the access token for the current runtimeConfig.
        /// As opposed to using a json input and regenerating the runtimconfig, it sets the access token for the current runtimeConfig on the provider.
        /// </summary>
        public bool TrySetAccesstoken(
        string? accessToken,
        string dataSourceName);
    }
}
