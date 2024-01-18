// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Net;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Exceptions;
using static Azure.DataApiBuilder.Core.Configurations.IRuntimeConfigProvider;

namespace Azure.DataApiBuilder.Core.Configurations;

/// <summary>
/// This class is responsible for exposing the runtime config to the rest of the service when in a local scenario.
/// The <c>RuntimeConfigProvider</c> won't directly load the config, but will instead rely on the <see cref="FileSystemRuntimeConfigLoader"/> to do so.
/// </summary>
/// <remarks>
/// The <c>LocalRuntimeConfigProvider</c> will not maintain internal state of the config, it uses the RuntimeConfigLoader
/// to retrieve the config.
///
/// This class should be treated as the sole accessor of the config that is available within the service, and other classes
/// should not access the config directly, or maintain a reference to it, so that we can do hot-reloading by replacing
/// the config that is available from this type.
/// </remarks>
public class LocalRuntimeConfigProvider : IRuntimeConfigProvider
{
    public bool IsLateConfigured { get; set; }

    public List<RuntimeConfigLoadedHandler> RuntimeConfigLoadedHandlers { get; } = new List<RuntimeConfigLoadedHandler>();

    public Dictionary<string, string?> ManagedIdentityAccessToken { get; set; } = new Dictionary<string, string?>();

    public RuntimeConfigLoader ConfigLoader { get; set; }

    public LocalRuntimeConfigProvider(RuntimeConfigLoader runtimeConfigLoader)
    {
        ConfigLoader = runtimeConfigLoader;
    }

    /// <summary>
    /// Return the previous loaded config, or it will attempt to load the config that
    /// is known by the loader.
    /// </summary>
    /// <returns>The RuntimeConfig instance.</returns>
    /// <remark>Dont use this method if environment variable references need to be retained.</remark>
    /// <exception cref="DataApiBuilderException">Thrown when the loader is unable to load an instance of the config from its known location.</exception>
    public RuntimeConfig GetConfig()
    {
        if (ConfigLoader.RuntimeConfig is not null)
        {
            return ConfigLoader.RuntimeConfig;
        }

        // While loading the config file, replace all the environment variables with their values.
        ConfigLoader.TryLoadKnownConfig(out _, replaceEnvVar: true);

        if (ConfigLoader.RuntimeConfig is null)
        {
            throw new DataApiBuilderException(
                message: "Runtime config isn't setup.",
                statusCode: HttpStatusCode.ServiceUnavailable,
                subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
        }

        return ConfigLoader.RuntimeConfig;
    }

    /// <summary>
    /// Attempt to acquire runtime configuration metadata.
    /// </summary>
    /// <param name="runtimeConfig">Populated runtime configuration, if present.</param>
    /// <returns>True when runtime config is provided, otherwise false.</returns>
    public bool TryGetConfig([NotNullWhen(true)] out RuntimeConfig? runtimeConfig)
    {
        if (ConfigLoader.RuntimeConfig is null)
        {
            ConfigLoader.TryLoadKnownConfig(out _, replaceEnvVar: true);
            
        }

        runtimeConfig = ConfigLoader.RuntimeConfig;
        return ConfigLoader.RuntimeConfig is not null;
    }

    /// <summary>
    /// Attempt to acquire runtime configuration metadata from a previously loaded one.
    /// This method will not load the config if it hasn't been loaded yet.
    /// </summary>
    /// <param name="runtimeConfig">Populated runtime configuration, if present.</param>
    /// <returns>True when runtime config is provided, otherwise false.</returns>
    public bool TryGetLoadedConfig([NotNullWhen(true)] out RuntimeConfig? runtimeConfig)
    {
        runtimeConfig = ConfigLoader.RuntimeConfig;
        return ConfigLoader.RuntimeConfig is not null;
    }

    /// <summary>
    /// Set the runtime configuration provider with the specified accessToken for the specified datasource.
    /// This initialization method is used to set the access token for the current runtimeConfig.
    /// As opposed to using a json input and regenerating the runtimconfig, it sets the access token for the current runtimeConfig on the provider.
    /// </summary>
    /// <param name="accessToken">The string representation of a managed identity access token</param>
    /// <param name="dataSourceName">Name of the datasource for which to assign the token.</param>
    /// <returns>true if the initialization succeeded, false otherwise.</returns>
    public bool TrySetAccesstoken(
        string? accessToken,
        string dataSourceName)
    {
        if (ConfigLoader.RuntimeConfig is null)
        {
            // if runtimeConfig is not set up, throw as cannot initialize.
            throw new DataApiBuilderException($"{nameof(RuntimeConfig)} has not been loaded.", HttpStatusCode.BadRequest, DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
        }

        // Validate that the datasource exists in the runtimeConfig and then add or update access token.
        if (ConfigLoader.RuntimeConfig.CheckDataSourceExists(dataSourceName))
        {
            ManagedIdentityAccessToken[dataSourceName] = accessToken;
            return true;
        }

        return false;
    }
}
