// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.Extensions.Options;

namespace Azure.DataApiBuilder.Config
{
    public class ConfigFileWatcher
    {
        private string? _configFilePath;
        private FileSystemWatcher? _fileWatcher;
        public event EventHandler<EventArgs>? ConfigChanged;
        IOptionsMonitor<RuntimeOptions>? _optionsMonitor;
        FileSystemRuntimeConfigLoader? _configLoader;

        public ConfigFileWatcher(string configFilePath, IOptionsMonitor<RuntimeOptions> optionsMonitor, FileSystemRuntimeConfigLoader? configLoader)
        {
            _configFilePath = configFilePath;

            _fileWatcher = new FileSystemWatcher(Path.GetDirectoryName(this._configFilePath)!)
            {
                Filter = Path.GetFileName(_configFilePath)
            };

            _optionsMonitor = optionsMonitor;
            _configLoader = configLoader;
            _fileWatcher.Changed += OnConfigFileChange;
            _configLoader = configLoader;
        }

        public ConfigFileWatcher(){}

        private void OnConfigFileChange(object sender, FileSystemEventArgs e)
        {
            // update the runtimeconfig and update the instance of IOptions<TOptions>
            // update the runtimeconfig
            // update the IOptions<RuntimeOptions>.Value

        }
    }
}
