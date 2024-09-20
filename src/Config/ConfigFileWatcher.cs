// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config;

/// <summary>
/// This class is responsible for monitoring the config file from the
/// local file system, and if changes are detected, triggering the
/// required logic to begin a hot reload scenario.
/// </summary>
public class ConfigFileWatcher
{
    private FileSystemWatcher? _fileWatcher;
    private FileSystemRuntimeConfigLoader _configLoader;

    public ConfigFileWatcher(FileSystemRuntimeConfigLoader configLoader, string directoryName, string configFileName)
    {
        _fileWatcher = new FileSystemWatcher(directoryName)
        {
            Filter = configFileName,
            EnableRaisingEvents = true
        };

        _configLoader = configLoader;
        _fileWatcher.Changed += OnConfigFileChange;
    }

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
            // When RuntimeConfig is null, we abort hot-reload.
            if (_configLoader.RuntimeConfig is not null && _configLoader.RuntimeConfig.IsDevelopmentMode())
            {
                _configLoader.HotReloadConfig(_configLoader.RuntimeConfig.DefaultDataSourceName);
            }
        }
        catch (Exception ex)
        {
            // Need to remove the dependencies in startup on the RuntimeConfigProvider
            // before we can have an ILogger here.
            Console.WriteLine("Unable to hot reload configuration file due to " + ex.Message);
        }
    }
}
