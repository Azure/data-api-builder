// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config;

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
            if (_configLoader is null)
            {
                throw new ArgumentNullException("_configProvider can not be null.");
            }

            if (_configLoader.RuntimeConfig is null)
            {
                throw new ArgumentNullException("RuntimeConfig can not be null.");
            }

            if (_configLoader.RuntimeConfig.IsDevelopmentMode())
            {
                _configLoader.HotReloadConfig();
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
