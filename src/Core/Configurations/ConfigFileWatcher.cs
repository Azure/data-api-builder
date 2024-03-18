// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.Configurations;

public class ConfigFileWatcher
{
    private FileSystemWatcher? _fileWatcher;
    private readonly RuntimeConfigProvider? _configProvider;

    public ConfigFileWatcher(RuntimeConfigProvider configProvider, string directoryName, string configFileName)
    {
        _fileWatcher = new FileSystemWatcher(directoryName)
        {
            Filter = configFileName,
            EnableRaisingEvents = true
        };

        _configProvider = configProvider;
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
            if (_configProvider is null)
            {
                throw new ArgumentNullException("_configProvider can not be null.");
            }

            if (!_configProvider.IsLateConfigured && _configProvider.GetConfig().IsDevelopmentMode())
            {
                // pass along the original default data source name for consistency within runtime config's data structures
                _configProvider.HotReloadConfig();
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
