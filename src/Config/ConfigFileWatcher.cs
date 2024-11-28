// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO.Abstractions;
using Azure.DataApiBuilder.Config.Utilities;

namespace Azure.DataApiBuilder.Config;

/// <summary>
/// This class is responsible for monitoring the config file from the
/// local file system. This watcher maintains a file hash to only emit
/// one event for each file change occurrence. Because .NET may raise >1
/// event for 1 file change instance, this class swallows extraneous events.
/// - "OnChanged is called when changes are made to the size, system attributes,
/// last write time, last access time, or security permissions of a file or directory
/// in the directory being monitored."
/// - The file hashing mechanism is a solution suggested by .NET documentation
/// to handle duplicate events evented for the same file change event.
/// </summary>
/// <seealso cref="https://learn.microsoft.com/en-us/dotnet/api/system.io.filesystemwatcher.onchanged#remarks"/>
/// <seealso cref="https://learn.microsoft.com/en-us/dotnet/api/system.io.filesystemwatcher.notifyfilter"/>
/// <seealso cref="https://learn.microsoft.com/en-us/aspnet/core/fundamentals/change-tokens#:~:text=exponential%20back%2Doff.-,Utilities/Utilities.cs%3A,-C%23"/>
public class ConfigFileWatcher
{
    /// <summary>
    /// Watches a specific file for modifications and alerts
    /// this class when a change is detected.
    /// </summary>
    private IFileSystemWatcher? _fileWatcher;

    /// <summary>
    /// Hash of runtime config file.
    /// Used to determine whether hot-reload should proceed due
    /// to detecting new runtime config file content.
    /// </summary>
    private byte[] _runtimeConfigHash;

    /// <summary>
    /// Orchestrates sending NewFileContentsDetected events to all subscribers.
    /// </summary>
    public event EventHandler? NewFileContentsDetected;

    /// <summary>
    /// Path of the directory being watched by the file watcher.
    /// </summary>
    public string WatchedDirectory { get; private set; }

    /// <summary>
    /// Name of the file being watched by the file watcher.
    /// </summary>
    public string WatchedFile { get; private set; }

    /// <summary>
    /// Starts watching the specified directory and file for changes.
    /// - Explicitly enables raising events on the file watcher, otherwise, filechanges
    /// will not be detected.
    /// - Calculates the filehash of the config being watched so that the watcher knows
    /// when a file change event is alerting the file watcher to new file content.
    /// - Registers the OnConfigFileChange function to be called when a file change is detected.
    /// </summary>
    public ConfigFileWatcher(IFileSystemWatcher fileWatcher, string directoryName, string configFileName)
    {
        WatchedDirectory = directoryName;
        WatchedFile = configFileName;
        _fileWatcher = fileWatcher;
        _fileWatcher.Path = WatchedDirectory;
        _fileWatcher.EnableRaisingEvents = true;
        _fileWatcher.Changed += OnConfigFileChange;
        _runtimeConfigHash = FileUtilities.ComputeHash(_fileWatcher.FileSystem, filePath: Path.Combine(WatchedDirectory, WatchedFile));
    }

    /// <summary>
    /// Raises the NewFileContentsDetected event which signals to listeners
    /// that a new configuration has been detected. This will only be raised
    /// for the first file-change event detected for a single file change.
    /// </summary>
    protected virtual void OnNewFileContentsDetected()
    {
        NewFileContentsDetected?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// When a change is detected in the Config file being watched this trigger
    /// function is called. It will compare the hash of the file to the cached hash
    /// value in order to discard duplicate notifications for a file which has
    /// only changed once.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void OnConfigFileChange(object sender, FileSystemEventArgs e)
    {
        try
        {
            if (_fileWatcher is not null)
            {
                // Multiple file change notifications may be raised for a single file change.
                // Use file hashes to ensure that HotReload operation is only executed when a net-new
                // runtime config is detected.
                byte[] updatedRuntimeConfigFileHash = FileUtilities.ComputeHash(_fileWatcher.FileSystem, filePath: Path.Combine(WatchedDirectory, WatchedFile));
                if (!_runtimeConfigHash.SequenceEqual(updatedRuntimeConfigFileHash))
                {
                    _runtimeConfigHash = updatedRuntimeConfigFileHash;
                    OnNewFileContentsDetected();
                }
            }
        }
        catch (AggregateException ex)
        {
            // Need to remove the dependencies in startup on the RuntimeConfigProvider
            // before we can have an ILogger here.
            foreach (Exception exception in ex.InnerExceptions)
            {
                Console.WriteLine("Unable to hot reload configuration file due to " + exception.Message);
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
