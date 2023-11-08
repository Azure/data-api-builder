// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO.Abstractions;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Path = System.IO.Path;

namespace Azure.DataApiBuilder.Core.Configurations;

public class ConfigFileWatcher
{
    private FileSystemWatcher? _fileWatcher;
    RuntimeConfigProvider? _configProvider;

    public ConfigFileWatcher(RuntimeConfigProvider configProvider)
    {
        FileSystemRuntimeConfigLoader loader = (FileSystemRuntimeConfigLoader)configProvider.ConfigLoader;
        string configFileName = loader.ConfigFilePath;
        IFileSystem fileSystem = (IFileSystem)loader._fileSystem;
        string? currentDirectoryPath = fileSystem.Directory.GetCurrentDirectory();
        string configFilePath = Path.Combine(currentDirectoryPath!, configFileName);
        string path = Path.GetDirectoryName(configFilePath)!;
        _fileWatcher = new FileSystemWatcher(Path.GetDirectoryName(configFilePath)!)
        {
            Filter = Path.GetFileName(configFilePath),
            EnableRaisingEvents = true
        };

        _configProvider = configProvider;
        _fileWatcher.Changed += OnConfigFileChange;
    }

    public ConfigFileWatcher() { }

    /// <summary>
    /// When a change is detected in the Config file being watched this trigger
    /// function is called and handles the hot reload logic when appropriate,
    /// ie: in a local development scenario.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void OnConfigFileChange(object sender, FileSystemEventArgs e)
    {
        try
        {
            if (_configProvider is null)
            {
                throw new ArgumentNullException("_configProvider can not be null.");
            }

            if (!_configProvider!.IsLateConfigured && _configProvider!.GetConfig().Runtime!.Host!.Mode is HostMode.Development)
            {
                _configProvider!.HotReloadConfig();
            }
        }
        catch (Exception ex)
        {
            // Need to remove the dependency configuring authentication has on the RuntimeConfigProvider
            // before we can have an ILogger here.
            Console.WriteLine("Unable to Hot Reload configuration file due to " + ex.Message);
        }
    }
}

