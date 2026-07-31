// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
using System.Net;
using System.Reflection;
using System.Text.Json;
using Azure.DataApiBuilder.Config.Converters;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Config.Utilities;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Config;

/// <summary>
/// This class is responsible for loading the runtime config from either a JSON string
/// or a file located on disk, depending on how the service is being run.
/// </summary>
/// <remarks>
/// This class derives from RuntimeConfigLoader and therefore maintains an internal copy of
/// the RuntimeConfig. The functions which load and parse the RuntimeConfig do not save
/// this state, and it is the responsibility of the class that instantiates and uses the loader
/// to manage how the RuntimeConfig is saved. This is a target for future refactor work which
/// will move the responsibility of saving the RuntimeConfig entirely to this class.
/// See: https://github.com/Azure/data-api-builder/issues/2362 for more information.
///
/// To support better testability, the <see cref="IFileSystem"/> abstraction is provided
/// which allows for mocking of the file system in tests, providing a way to run the test
/// in isolation of other tests or the actual file system.
/// </remarks>
public class FileSystemRuntimeConfigLoader : RuntimeConfigLoader, IDisposable
{
    private readonly SemaphoreSlim _hotReloadGate = new(initialCount: 1, maxCount: 1);
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly object _operationLock = new();
    private readonly object _watcherLock = new();
    private readonly Func<IFileSystem, string, string, IConfigFileWatcher> _configFileWatcherFactory;
    private TaskCompletionSource _activeOperationsDrained = CreateCompletedDrainSignal();
    private int _activeOperationCount;
    private int _disposed;
    /// <summary>
    /// This stores either the default config name e.g. dab-config.json
    /// or user provided config file which could be a relative file path,
    /// absolute file path or simply the file name assumed to be in current directory.
    /// </summary>
    private string _baseConfigFilePath;

    /// <summary>
    /// This field is used to determine if the loader is being used by the CLI.
    /// CLI usage of the loader should not set up the file watcher for hot reload
    /// because:
    /// 1. Hot reload isn't needed for the CLI.
    /// 2. The CLI doesn't set _baseConfigFilePath using the user supplied config file name
    /// resulting in failed config file lookups within the file watcher.
    /// </summary>
    private bool _isCliLoader;

    /// <summary>
    /// Watches the config file for changes and triggers hot-reload when a change is detected.
    /// </summary>
    private IConfigFileWatcher? _configFileWatcher;

    /// <summary>
    /// File system abstraction used to interact with the runtime config file.
    /// </summary>
    private readonly IFileSystem _fileSystem;

    /// <summary>
    /// Logger used to log all the events that occur inside of FileSystemRuntimeConfigLoader
    /// </summary>
    private ILogger<FileSystemRuntimeConfigLoader>? _logger;

    public const string CONFIGFILE_NAME = "dab-config";
    public const string CONFIG_EXTENSION = ".json";
    public const string ENVIRONMENT_PREFIX = "DAB_";
    public const string RUNTIME_ENVIRONMENT_VAR_NAME = $"{ENVIRONMENT_PREFIX}ENVIRONMENT";
    public const string RUNTIME_ENV_CONNECTION_STRING = $"{ENVIRONMENT_PREFIX}CONNSTRING";
    public const string ASP_NET_CORE_ENVIRONMENT_VAR_NAME = "ASPNETCORE_ENVIRONMENT";
    public const string SCHEMA = "dab.draft.schema.json";
    public const string DEFAULT_CONFIG_FILE_NAME = $"{CONFIGFILE_NAME}{CONFIG_EXTENSION}";

    /// <summary>
    /// Stores the config file actually loaded by the engine.
    /// It could be the base config file (e.g. dab-config.json), any of its derivatives with
    /// environment specific suffixes (e.g. dab-config.Development.json) or the user provided
    /// config file name.
    /// It could also be the config file provided by the user.
    /// </summary>
    public string ConfigFilePath { get; internal set; }

    /// <summary>
    /// Indicates whether the most recent TryLoadConfig call encountered a parse error
    /// that was already emitted to Console.Error.
    /// </summary>
    public bool IsParseErrorEmitted { get; private set; }

    public FileSystemRuntimeConfigLoader(
        IFileSystem fileSystem,
        HotReloadEventHandler<HotReloadEventArgs>? handler = null,
        string baseConfigFilePath = DEFAULT_CONFIG_FILE_NAME,
        string? connectionString = null,
        bool isCliLoader = false,
        ILogger<FileSystemRuntimeConfigLoader>? logger = null)
        : this(
            fileSystem,
            handler,
            baseConfigFilePath,
            connectionString,
            isCliLoader,
            logger,
            static (watcherFileSystem, directoryName, configFileName) =>
                new ConfigFileWatcher(
                    new FileSystemWatcherWrapper(watcherFileSystem),
                    directoryName,
                    configFileName))
    {
    }

    internal FileSystemRuntimeConfigLoader(
        IFileSystem fileSystem,
        HotReloadEventHandler<HotReloadEventArgs>? handler,
        string baseConfigFilePath,
        string? connectionString,
        bool isCliLoader,
        ILogger<FileSystemRuntimeConfigLoader>? logger,
        Func<IFileSystem, string, string, IConfigFileWatcher> configFileWatcherFactory)
        : base(handler, connectionString)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _configFileWatcherFactory = configFileWatcherFactory ??
            throw new ArgumentNullException(nameof(configFileWatcherFactory));
        _baseConfigFilePath = baseConfigFilePath;
        ConfigFilePath = GetFinalConfigFilePath();
        _isCliLoader = isCliLoader;
        _logger = logger;
    }

    /// <summary>
    /// Disposes the config file watcher to release file handles and stop
    /// monitoring the config file for changes. Active serialized work is canceled and drained
    /// before this method returns so it cannot outlive host-owned dependencies.
    /// </summary>
    public void Dispose()
    {
        StopAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Stops accepting hot-reload work, requests cancellation of the active operation, and waits
    /// until all serialized work has exited. Host shutdown calls this before singleton disposal.
    /// </summary>
    internal async Task StopAsync(CancellationToken cancellationToken)
    {
        Task activeOperationsDrained = BeginShutdown();
        await activeOperationsDrained.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private Task BeginShutdown()
    {
        bool firstShutdownRequest = Interlocked.Exchange(ref _disposed, 1) == 0;
        if (firstShutdownRequest)
        {
            _disposeCancellation.Cancel();
            StopAndDisposeConfigFileWatcher();
        }

        lock (_operationLock)
        {
            return _activeOperationsDrained.Task;
        }
    }

    private void StopAndDisposeConfigFileWatcher()
    {

        IConfigFileWatcher? configFileWatcher;
        lock (_watcherLock)
        {
            configFileWatcher = _configFileWatcher;
            _configFileWatcher = null;

            if (configFileWatcher is not null)
            {
                configFileWatcher.NewFileContentsDetected -= OnNewFileContentsDetected;
            }
        }

        if (configFileWatcher is not null)
        {
            try
            {
                configFileWatcher.StopWatching();
            }
            catch (Exception ex)
            {
                SendLogToBufferOrLogger(
                    LogLevel.Warning,
                    $"Unable to disable the configuration file watcher during shutdown due to {ex.Message}");
            }

            // Underlying FileSystemWatcher disposal can block while an OS callback completes.
            // Dispose it on a background worker so host shutdown never waits for an active reload.
            ScheduleConfigFileWatcherDisposal(configFileWatcher);
        }
    }

    /// <summary>
    /// Get the directory name of the config file and
    /// return as a string.
    /// </summary>
    /// <returns>String representing the full file path
    /// of the config up to but not including the filename.</returns>
    public string GetConfigDirectoryName()
    {
        string? directoryName = Path.GetDirectoryName(ConfigFilePath);
        directoryName = string.IsNullOrWhiteSpace(directoryName) ?
                    _fileSystem.Directory.GetCurrentDirectory() :
                    directoryName;
        return directoryName;
    }

    /// <summary>
    /// Get the config file name and return it
    /// as a string.
    /// </summary>
    /// <returns>String representing the file name and extension.</returns>
    public string GetConfigFileName()
    {
        string configFileName = Path.GetFileName(ConfigFilePath);
        return configFileName;
    }

    /// <summary>
    /// Checks if we have already attempted to configure the file watcher, if not
    /// instantiate the file watcher if we are in the development mode.
    /// Returns true if we instantiate a new file watcher.
    /// </summary>
    private bool TrySetupConfigFileWatcher()
    {
        lock (_watcherLock)
        {
            // File watching / hot-reload isn't used for the CLI and must not start once disposal
            // begins, including when disposal races with initial configuration loading.
            if (_isCliLoader || IsDisposed)
            {
                return false;
            }

            // If the file watcher is already set up, we don't need to do it again.
            if (_configFileWatcher is not null)
            {
                return false;
            }

            if (RuntimeConfig is not null)
            {
                try
                {
                    _configFileWatcher = _configFileWatcherFactory(
                        _fileSystem,
                        GetConfigDirectoryName(),
                        GetConfigFileName());
                    _configFileWatcher.NewFileContentsDetected += OnNewFileContentsDetected;
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
    }

    /// <summary>
    /// When a change is detected in the Config file being watched this trigger
    /// function is called and handles the hot reload logic when appropriate,
    /// ie: in a local development scenario.
    /// </summary>
    private void OnNewFileContentsDetected(object? sender, EventArgs e)
    {
        ProcessHotReloadNotification();
    }

    /// <summary>
    /// Processes one file-change notification while serializing the complete hot-reload pipeline
    /// for this loader instance. The gate begins before the current configuration is inspected and
    /// remains held through all synchronous <see cref="RuntimeConfigLoader.SignalConfigChanged"/>
    /// handlers so dependencies cannot be mixed across generations.
    /// </summary>
    /// <param name="beforeEnteringGate">
    /// Optional observer invoked immediately before waiting for the serialization gate. This is
    /// used by deterministic concurrency tests to prove a second notification reached the gate.
    /// </param>
    internal void ProcessHotReloadNotification(Action? beforeEnteringGate = null)
    {
        beforeEnteringGate?.Invoke();

        if (IsDisposed)
        {
            return;
        }

        try
        {
            _hotReloadGate.Wait(_disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (IsDisposed)
        {
            return;
        }

        if (!TryBeginSerializedOperation())
        {
            _hotReloadGate.Release();
            return;
        }

        try
        {
            try
            {
                if (RuntimeConfig is not null)
                {
                    HotReloadConfig(
                        RuntimeConfig.IsDevelopmentMode(),
                        _disposeCancellation.Token);
                }
            }
            catch (OperationCanceledException) when (IsDisposed)
            {
                // Host shutdown canceled this generation before it could finish publication.
            }
            catch (Exception ex)
            {
                SendLogToBufferOrLogger(
                    LogLevel.Error,
                    $"Unable to hot reload configuration file due to {ex.Message}");
            }
        }
        finally
        {
            EndSerializedOperation();
            _hotReloadGate.Release();
        }
    }

    /// <summary>
    /// Executes initial runtime dependency construction under the same per-loader gate used by
    /// file-triggered hot reload. The operation may be asynchronous, and the gate remains held
    /// until it completes so a configuration cannot change between metadata initialization and
    /// dependent component publication.
    /// </summary>
    /// <param name="operation">The complete initial configuration operation to serialize.</param>
    public Task ExecuteWithHotReloadSerializationAsync(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return ExecuteWithHotReloadSerializationAsync(_ => operation());
    }

    /// <summary>
    /// Executes initial runtime dependency construction under the same per-loader gate used by
    /// file-triggered hot reload, with cooperative shutdown cancellation.
    /// </summary>
    /// <param name="operation">
    /// The complete initial configuration operation to serialize. The supplied token is canceled
    /// when loader shutdown begins.
    /// </param>
    public async Task ExecuteWithHotReloadSerializationAsync(
        Func<CancellationToken, Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (IsDisposed)
        {
            throw new ObjectDisposedException(nameof(FileSystemRuntimeConfigLoader));
        }

        try
        {
            await _hotReloadGate.WaitAsync(_disposeCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (IsDisposed)
        {
            throw new ObjectDisposedException(nameof(FileSystemRuntimeConfigLoader));
        }

        if (!TryBeginSerializedOperation())
        {
            _hotReloadGate.Release();
            throw new ObjectDisposedException(nameof(FileSystemRuntimeConfigLoader));
        }

        try
        {
            await operation(_disposeCancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            EndSerializedOperation();
            _hotReloadGate.Release();
        }
    }

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    private bool TryBeginSerializedOperation()
    {
        lock (_operationLock)
        {
            if (IsDisposed)
            {
                return false;
            }

            if (_activeOperationCount++ == 0)
            {
                _activeOperationsDrained = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }

            return true;
        }
    }

    private void EndSerializedOperation()
    {
        TaskCompletionSource? drainedSignal = null;
        lock (_operationLock)
        {
            if (--_activeOperationCount == 0)
            {
                drainedSignal = _activeOperationsDrained;
            }
        }

        drainedSignal?.TrySetResult();
    }

    private static TaskCompletionSource CreateCompletedDrainSignal()
    {
        TaskCompletionSource signal = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        signal.SetResult();
        return signal;
    }

    private void ScheduleConfigFileWatcherDisposal(IConfigFileWatcher configFileWatcher)
    {
        Action disposeWatcher = () =>
        {
            try
            {
                configFileWatcher.Dispose();
            }
            catch (Exception ex)
            {
                SendLogToBufferOrLogger(
                    LogLevel.Warning,
                    $"Unable to dispose the configuration file watcher due to {ex.Message}");
            }
        };

        if (ThreadPool.QueueUserWorkItem(
                static callback => callback(),
                disposeWatcher,
                preferLocal: false))
        {
            return;
        }

        try
        {
            Thread fallbackWorker = new(
                static callback => ((Action)callback!).Invoke())
            {
                IsBackground = true,
                Name = "DAB configuration watcher disposal"
            };
            fallbackWorker.Start(disposeWatcher);
        }
        catch (Exception ex)
        {
            SendLogToBufferOrLogger(
                LogLevel.Warning,
                $"Unable to schedule configuration file watcher disposal due to {ex.Message}");
        }
    }

    /// <summary>
    /// Load the runtime config from the specified path.
    /// </summary>
    /// <param name="path">The path to the dab-config.json file.</param>
    /// <param name="config">The loaded <c>RuntimeConfig</c>, or null if none was loaded.</param>
    /// <param name="logger">ILogger for logging errors.</param>
    /// <param name="isDevMode">When not null indicates we need to overwrite mode and how to do so.</param>
    /// <param name="replacementSettings">Settings for variable replacement during deserialization. If null, uses default settings with environment variable replacement disabled.</param>
    /// <returns>True if the config was loaded, otherwise false.</returns>
    public bool TryLoadConfig(
        string path,
        [NotNullWhen(true)] out RuntimeConfig? config,
        ILogger? logger = null,
        bool? isDevMode = null,
        DeserializationVariableReplacementSettings? replacementSettings = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsParseErrorEmitted = false;
        if (_fileSystem.File.Exists(path))
        {
            SendLogToBufferOrLogger(LogLevel.Information, $"Loading config file from {_fileSystem.Path.GetFullPath(path)}.");

            // Use File.ReadAllText because DAB doesn't need write access to the file
            // and ensures the file handle is released immediately after reading.
            // Previous usage of File.Open may cause file locking issues when
            // actively using hot-reload and modifying the config file in a text editor.
            // Includes an exponential back-off retry mechanism to accommodate
            // circumstances where the file may be in use by another process.
            int runCount = 1;
            string json = string.Empty;
            while (runCount <= FileUtilities.RunLimit)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    json = _fileSystem.File.ReadAllText(path);
                    break;
                }
                catch (IOException ex)
                {
                    SendLogToBufferOrLogger(LogLevel.Warning, $"IO Exception, retrying due to {ex.Message}");

                    if (runCount == FileUtilities.RunLimit)
                    {
                        throw;
                    }

                    TimeSpan retryDelay = TimeSpan.FromSeconds(
                        Math.Pow(FileUtilities.ExponentialRetryBase, runCount));
                    if (cancellationToken.WaitHandle.WaitOne(retryDelay))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    runCount++;
                }
            }

            // Use default replacement settings if none provided
            replacementSettings ??= new DeserializationVariableReplacementSettings();

            string? parseError = null;
            if (!string.IsNullOrEmpty(json) && TryParseConfig(
                json,
                out RuntimeConfig,
                out parseError,
                replacementSettings,
                connectionString: _connectionString))
            {
                if (TrySetupConfigFileWatcher())
                {
                    SendLogToBufferOrLogger(LogLevel.Information, $"Monitoring config: {ConfigFilePath} for hot-reloading.");
                }

                // When isDevMode is not null it means we are in a hot-reload scenario, and need to save the previous
                // mode in the new RuntimeConfig since we do not support hot-reload of the mode.
                if (isDevMode is not null && RuntimeConfig.Runtime is not null && RuntimeConfig.Runtime.Host is not null)
                {
                    // Log error when the mode is changed during hot-reload.
                    if (isDevMode != this.RuntimeConfig.IsDevelopmentMode())
                    {
                        SendLogToBufferOrLogger(LogLevel.Error, "Hot-reload doesn't support switching mode. Please restart the service to switch the mode.");
                    }

                    RuntimeConfig.Runtime.Host.Mode = (bool)isDevMode ? HostMode.Development : HostMode.Production;
                }

                config = RuntimeConfig;

                if (LastValidRuntimeConfig is null)
                {
                    LastValidRuntimeConfig = RuntimeConfig;
                }

                return true;
            }

            if (LastValidRuntimeConfig is not null)
            {
                RuntimeConfig = LastValidRuntimeConfig;
            }

            if (parseError is not null)
            {
                SendLogToBufferOrLogger(LogLevel.Error, parseError);
                IsParseErrorEmitted = true;
            }

            config = null;
            return false;
        }

        string errorMessage = $"Unable to find config file: {path} does not exist.";
        SendLogToBufferOrLogger(LogLevel.Error, errorMessage);

        config = null;
        return false;
    }

    /// <summary>
    /// Tries to load the config file using the filename known to the RuntimeConfigLoader and for the default environment.
    /// </summary>
    /// <param name="config">The loaded <c>RuntimeConfig</c>, or null if none was loaded.</param>
    /// <param name="replacementSettings">Settings for variable replacement during deserialization. If null, uses default settings with environment variable replacement disabled.</param>
    /// <returns>True if the config was loaded, otherwise false.</returns>
    public override bool TryLoadKnownConfig([NotNullWhen(true)] out RuntimeConfig? config, bool replaceEnvVar = false)
    {
        // Convert legacy replaceEnvVar parameter to replacement settings for backward compatibility
        DeserializationVariableReplacementSettings? replacementSettings = new(azureKeyVaultOptions: null, doReplaceEnvVar: replaceEnvVar, doReplaceAkvVar: replaceEnvVar, envFailureMode: EnvironmentVariableReplacementFailureMode.Ignore);
        return TryLoadConfig(ConfigFilePath, out config, replacementSettings: replacementSettings);
    }

    /// <summary>
    /// Hot Reloads the runtime config when the file watcher
    /// is active and detects a change to the underlying config file.
    /// </summary>
    private void HotReloadConfig(bool isDevMode, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SendLogToBufferOrLogger(
            LogLevel.Information,
            $"Starting hot-reload process for config: {ConfigFilePath}");

        // Use default replacement settings for hot reload
        DeserializationVariableReplacementSettings replacementSettings = new(azureKeyVaultOptions: null, doReplaceEnvVar: true, doReplaceAkvVar: true);

        if (!TryLoadConfig(
            ConfigFilePath,
            out _,
            isDevMode: isDevMode,
            replacementSettings: replacementSettings,
            cancellationToken: cancellationToken))
        {
            throw new DataApiBuilderException(
                message: "Deserialization of the configuration file failed.",
                statusCode: HttpStatusCode.ServiceUnavailable,
                subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
        }

        IsNewConfigDetected = true;
        IsNewConfigValidated = false;
        SignalConfigChanged(cancellationToken: cancellationToken);

        SendLogToBufferOrLogger(LogLevel.Information, "Hot-reload process finished.");
    }

    /// <summary>
    /// Precedence of environments is
    /// 1) Value of DAB_ENVIRONMENT.
    /// 2) Value of ASPNETCORE_ENVIRONMENT.
    /// 3) Default config file name.
    /// In each case, overridden file name takes precedence.
    /// The first file name that exists in current directory is returned.
    /// The fall back options are dab-config.overrides.json/dab-config.json
    /// If no file exists, this will return an empty string.
    /// </summary>
    /// <param name="aspnetEnvironment">Value of ASPNETCORE_ENVIRONMENT variable</param>
    /// <param name="considerOverrides">whether to look for overrides file or not.</param>
    /// <returns></returns>
    public string GetFileNameForEnvironment(string? aspnetEnvironment, bool considerOverrides)
    {
        string configFileNameWithExtension = string.Empty;
        string?[] environmentPrecedence = new[]
        {
            Environment.GetEnvironmentVariable(RUNTIME_ENVIRONMENT_VAR_NAME),
            aspnetEnvironment,
            string.Empty
        };

        for (short index = 0;
            index < environmentPrecedence.Length
            && string.IsNullOrEmpty(configFileNameWithExtension);
            index++)
        {
            if (!string.IsNullOrWhiteSpace(environmentPrecedence[index])
                // The last index is for the default case - the last fallback option
                // where environmentPrecedence[index] is string.Empty
                // for that case, we still need to get the file name considering overrides
                // so need to do an OR on the last index here
                || index == environmentPrecedence.Length - 1)
            {
                configFileNameWithExtension = GetFileName(environmentPrecedence[index], considerOverrides);
            }
        }

        return configFileNameWithExtension;
    }

    /// <summary>
    /// This method returns the final config file name that will be used by the runtime engine.
    /// </summary>
    private string GetFinalConfigFilePath()
    {
        if (!string.Equals(_baseConfigFilePath, DEFAULT_CONFIG_FILE_NAME))
        {
            // user provided config file is honoured.
            return _baseConfigFilePath;
        }

        // ConfigFile not explicitly provided by user, so we need to get the config file name based on environment.
        string configFilePath = GetFileNameForEnvironment(Environment.GetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME), false);

        // If file for environment is not found, then the baseConfigFile is used as the final configFile for runtime engine.
        if (string.IsNullOrWhiteSpace(configFilePath))
        {
            return _baseConfigFilePath;
        }

        return configFilePath;
    }

    /// <summary>
    /// Generates the config file name and a corresponding overridden file name,
    /// With precedence given to overridden file name, returns that name
    /// if the file exists in the current directory, else an empty string.
    /// </summary>
    /// <param name="environmentValue">Name of the environment to
    /// generate the config file name for.</param>
    /// <param name="considerOverrides">whether to look for overrides file or not.</param>
    /// <returns></returns>
    public string GetFileName(string? environmentValue, bool considerOverrides)
    {
        // If the baseConfigFilePath contains directory info, we need to ensure that it is not lost. for example: baseConfigFilePath = "config/dab-config.json"
        // in this case, we need to get the directory name and the file name without extension and then combine them back. Else, we will lose the path
        // and the file will be searched in the current directory.
        string filePathWithoutExtension = _fileSystem.Path.Combine(_fileSystem.Path.GetDirectoryName(_baseConfigFilePath) ?? string.Empty, _fileSystem.Path.GetFileNameWithoutExtension(_baseConfigFilePath));
        string fileExtension = _fileSystem.Path.GetExtension(_baseConfigFilePath);
        string configFilePath =
            !string.IsNullOrEmpty(environmentValue)
            ? $"{filePathWithoutExtension}.{environmentValue}"
            : $"{filePathWithoutExtension}";
        string configFileNameWithExtension = $"{configFilePath}{fileExtension}";
        string overriddenConfigFileNameWithExtension = GetOverriddenName(configFilePath);

        if (considerOverrides && DoesFileExistInDirectory(overriddenConfigFileNameWithExtension))
        {
            return overriddenConfigFileNameWithExtension;
        }

        if (DoesFileExistInDirectory(configFileNameWithExtension))
        {
            return configFileNameWithExtension;
        }

        return string.Empty;
    }

    private static string GetOverriddenName(string filePath)
    {
        return $"{filePath}.overrides{CONFIG_EXTENSION}";
    }

    /// <summary>
    /// Generates the name of the file based on environment value.
    /// NOTE: Input File name should not contain extension
    /// </summary>
    public static string GetEnvironmentFileName(string fileName, string environmentValue)
    {
        return $"{fileName}.{environmentValue}{CONFIG_EXTENSION}";
    }

    /// <summary>
    /// Checks if the file exists in the directory.
    /// Works for both relative and absolute paths.
    /// </summary>
    /// <param name="filePath"></param>
    /// <returns>True if file is found, else false.</returns>
    public bool DoesFileExistInDirectory(string filePath)
    {
        string currentDir = _fileSystem.Directory.GetCurrentDirectory();
        return _fileSystem.File.Exists(_fileSystem.Path.Combine(currentDir, filePath));
    }

    /// <summary>
    /// This method reads the dab.draft.schema.json which contains the link for online published
    /// schema for dab, based on the version of dab being used to generate the runtime config.
    /// </summary>
    public override string GetPublishedDraftSchemaLink()
    {
        string? assemblyDirectory = _fileSystem.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        if (assemblyDirectory is null)
        {
            throw new DataApiBuilderException(
                message: "Could not get the link for DAB draft schema.",
                statusCode: HttpStatusCode.ServiceUnavailable,
                subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
        }

        string? schemaPath = _fileSystem.Path.Combine(assemblyDirectory, "dab.draft.schema.json");
        string schemaFileContent = _fileSystem.File.ReadAllText(schemaPath);
        Dictionary<string, object>? jsonDictionary = JsonSerializer.Deserialize<Dictionary<string, object>>(schemaFileContent, GetSerializationOptions(replacementSettings: null));

        if (jsonDictionary is null)
        {
            throw new DataApiBuilderException(
                message: "The schema file is misconfigured. Please check the file formatting.",
                statusCode: HttpStatusCode.ServiceUnavailable,
                subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
        }

        if (!jsonDictionary.TryGetValue("$id", out object? id))
        {
            throw new DataApiBuilderException(
                message: "The schema file doesn't have the required field : $id",
                statusCode: HttpStatusCode.ServiceUnavailable,
                subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
        }

        return id.ToString()!;
    }

    public static string GetMergedFileNameForEnvironment(string fileName, string environmentValue)
    {
        return $"{fileName}.{environmentValue}.merged{CONFIG_EXTENSION}";
    }

    /// <summary>
    /// Allows the base config file and the actually loaded config file name(tracked by the property ConfigFileName)
    /// to be updated. This is commonly done when the CLI is starting up.
    /// </summary>
    /// <param name="filePath"></param>
    public void UpdateConfigFilePath(string filePath)
    {
        _baseConfigFilePath = filePath;
        ConfigFilePath = filePath;
    }

    public void SetLogger(ILogger<FileSystemRuntimeConfigLoader> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Flush all logs from the buffer after the log level is set from the RuntimeConfig.
    /// Logger needs to be present, or else the logs will be lost.
    /// </summary>
    public void FlushLogBuffer()
    {
        _logBuffer.FlushToLogger(_logger!);
    }

    /// <summary>
    /// Helper method that sends the log to the buffer if the logger has not being set up.
    /// Else, it will send the log to the logger.
    /// </summary>
    /// <param name="logLevel">LogLevel of the log.</param>
    /// <param name="message">Message that will be printed in the log.</param>
    private void SendLogToBufferOrLogger(LogLevel logLevel, string message)
    {
        if (_logger is null)
        {
            _logBuffer.BufferLog(logLevel, message);
        }
        else
        {
            _logger?.Log(logLevel, message);
        }
    }
}
