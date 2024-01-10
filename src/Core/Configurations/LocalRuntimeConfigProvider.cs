// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Net;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Exceptions;
using static Azure.DataApiBuilder.Core.Configurations.HostedRuntimeConfigProvider;

namespace Azure.DataApiBuilder.Core.Configurations;

/// <summary>
/// This class is responsible for exposing the runtime config to the rest of the service.
/// The <c>RuntimeConfigProvider</c> won't directly load the config, but will instead rely on the <see cref="FileSystemRuntimeConfigLoader"/> to do so.
/// </summary>
/// <remarks>
/// The <c>RuntimeConfigProvider</c> will maintain internal state of the config, and will only load it once.
///
/// This class should be treated as the owner of the config that is available within the service, and other classes
/// should not load the config directly, or maintain a reference to it, so that we can do hot-reloading by replacing
/// the config that is available from this type.
/// </remarks>
public class LocalRuntimeConfigProvider : IRuntimeConfigProvider
{
    /// <summary>
    /// Indicates whether the config was loaded after the runtime was initialized.
    /// </summary>
    /// <remarks>This is most commonly used when DAB's config is provided via the <c>ConfigurationController</c>, such as when it's a hosted service.</remarks>
    public bool IsLateConfigured { get; set; }

    public List<RuntimeConfigLoadedHandler> RuntimeConfigLoadedHandlers { get; } = new List<RuntimeConfigLoadedHandler>();

    public Dictionary<string, string?> ManagedIdentityAccessToken { get; set; } = new Dictionary<string, string?>();

    public RuntimeConfigLoader ConfigLoader { get; set; }

    private ConfigFileWatcher? _configFileWatcher;

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
        if (ConfigLoader.TryLoadKnownConfig(out _, replaceEnvVar: true))
        {
            TrySetupConfigFileWatcher();
        }

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
    /// Checks if we have already attempted to configure the file watcher, if not
    /// instantiate the file watcher if we are in the correct scenario. If we
    /// are not in the correct scenario, do not setup a file watcher but remember
    /// that we have attempted to do so to avoid repeat checks in future calls.
    /// Returns true if we instantiate a file watcher.
    /// </summary>
    private bool TrySetupConfigFileWatcher()
    {
        if (!IsLateConfigured && ConfigLoader.RuntimeConfig is not null && ConfigLoader.RuntimeConfig.IsDevelopmentMode())
        {
            try
            {
                FileSystemRuntimeConfigLoader loader = (FileSystemRuntimeConfigLoader)ConfigLoader;
                _configFileWatcher = new(this, loader.GetConfigDirectoryName(), loader.GetConfigFileName());
            }
            catch (Exception ex)
            {
                // Need to remove the dependencies in startup on the RuntimeConfigProvider
                // before we can have an ILogger here.
                Console.WriteLine($"Attempt to configure config file watcher for hot reload failed due to: {ex.Message}.");
            }

            return _configFileWatcher is not null;
        }

        return false;
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
            if (ConfigLoader.TryLoadKnownConfig(out RuntimeConfig? _, replaceEnvVar: true))
            {
                TrySetupConfigFileWatcher();
            }
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
    /// Hot Reloads the runtime config when the file watcher
    /// is active and detects a change to the underlying config file.
    /// </summary>
    public void HotReloadConfig()
    {
        ConfigLoader.TryLoadKnownConfig(out _, replaceEnvVar: true);
    }
}
