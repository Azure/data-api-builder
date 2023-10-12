// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO.Abstractions;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
//using Microsoft.Extensions.Logging;
using Path = System.IO.Path;

namespace Azure.DataApiBuilder.Core.Configurations;

public class ConfigFileWatcher
{
    private FileSystemWatcher? _fileWatcher;
    RuntimeConfigProvider? _configProvider;
    // ILogger<ConfigFileWatcher>? _logger;

    public ConfigFileWatcher(RuntimeConfigProvider configProvider)
    {
        FileSystemRuntimeConfigLoader loader = (FileSystemRuntimeConfigLoader)configProvider.ConfigLoader;
        string configFileName = loader.ConfigFilePath;
        FileSystem fileSystem = (FileSystem)loader._fileSystem;
        string? currentDirectoryPath = fileSystem.Directory.GetCurrentDirectory();
        string configFilePath = Path.Combine(currentDirectoryPath!, configFileName);
        _fileWatcher = new FileSystemWatcher(Path.GetDirectoryName(configFilePath)!)
        {
            Filter = Path.GetFileName(configFilePath)
        };

        _configProvider = configProvider;
        _fileWatcher.Changed += OnConfigFileChange;
    }

    public ConfigFileWatcher() { }

    private void OnConfigFileChange(object sender, FileSystemEventArgs e)
    {
        try
        {
            if (!_configProvider!.IsLateConfigured && _configProvider!.GetConfig().Runtime.Host.Mode is HostMode.Development)
            {
                _configProvider!.HotReloadConfig();
            }
        }
        catch (Exception ex) // improve exception handling based on errors that come back in tests
        {
            // replace with logger
            // use dependancy injection when first constructing ConfigFileWatcher to get logger
            Console.WriteLine("Unable to Hot Reload configuration file due to " + ex.Message); // add better messaging
        }
    }
}

