// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.Extensions.Options;
using Path = System.IO.Path;

namespace Azure.DataApiBuilder.Core.Configurations;

public class ConfigFileWatcher
{
    private string? _configFilePath;
    private FileSystemWatcher? _fileWatcher;
    private readonly IOptionsMonitor<RuntimeOptions>? _optionsMonitor;
    RuntimeConfigProvider? _configProvider;

    public ConfigFileWatcher(string configFilePath, IOptionsMonitor<RuntimeOptions> optionsMonitor, RuntimeConfigProvider? configProvider)
    {
        _configFilePath = configFilePath;

        _fileWatcher = new FileSystemWatcher(Path.GetDirectoryName(this._configFilePath)!)
        {
            Filter = Path.GetFileName(_configFilePath)
        };

        _optionsMonitor = optionsMonitor;
        _configProvider = configProvider;
        _fileWatcher.Changed += OnConfigFileChange;
    }

    public ConfigFileWatcher(){}

    private void OnConfigFileChange(object sender, FileSystemEventArgs e)
    {
        // update the runtimeconfig and update the instance of IOptions<TOptions>
        // update the runtimeconfig
        _configProvider!.HotReloadConfig();
        // update the IOptions<RuntimeOptions>.Value

    }
}

