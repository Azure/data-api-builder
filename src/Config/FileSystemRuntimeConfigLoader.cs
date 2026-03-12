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
public class FileSystemRuntimeConfigLoader : RuntimeConfigLoader
{
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
    private ConfigFileWatcher? _configFileWatcher;

    /// <summary>
    /// File system abstraction used to interact with the runtime config file.
    /// </summary>
    private readonly IFileSystem _fileSystem;

    /// <summary>
    /// Logger used to log events inside <see cref="FileSystemRuntimeConfigLoader"/>.
    /// Null until the DI container has been built (i.e. before <see cref="SetLogger"/> is called).
    /// When null, messages are routed through <see cref="_logBuffer"/> if available, or written
    /// directly to <see cref="Console.Error"/> for errors.
    /// </summary>
    private ILogger<FileSystemRuntimeConfigLoader>? _logger;

    /// <summary>
    /// Early-startup log buffer used before a proper <see cref="ILogger"/> is available.
    /// </summary>
    private StartupLogBuffer? _logBuffer;

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

    public FileSystemRuntimeConfigLoader(
        IFileSystem fileSystem,
        HotReloadEventHandler<HotReloadEventArgs>? handler = null,
        string baseConfigFilePath = DEFAULT_CONFIG_FILE_NAME,
        string? connectionString = null,
        bool isCliLoader = false,
        ILogger<FileSystemRuntimeConfigLoader>? logger = null,
        StartupLogBuffer? logBuffer = null)
        : base(handler, connectionString)
    {
        _fileSystem = fileSystem;
        _baseConfigFilePath = baseConfigFilePath;
        ConfigFilePath = GetFinalConfigFilePath();
        _isCliLoader = isCliLoader;
        _logger = logger;
        _logBuffer = logBuffer;
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
        // File watching / hot-reload isn't used for the CLI.
        if (_isCliLoader)
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
                _configFileWatcher = new(new FileSystemWatcherWrapper(_fileSystem), GetConfigDirectoryName(), GetConfigFileName());
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

    /// <summary>
    /// When a change is detected in the Config file being watched this trigger
    /// function is called and handles the hot reload logic when appropriate,
    /// ie: in a local development scenario.
    /// </summary>
    private void OnNewFileContentsDetected(object? sender, EventArgs e)
    {
        try
        {
            if (RuntimeConfig is not null)
            {
                HotReloadConfig(RuntimeConfig.IsDevelopmentMode());
            }
        }
        catch (Exception ex)
        {
            // Need to remove the dependencies in startup on the RuntimeConfigProvider
            // before we can have an ILogger here.
            Console.WriteLine("Unable to hot reload configuration file due to " + ex.Message);
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
        DeserializationVariableReplacementSettings? replacementSettings = null)
    {
        if (_fileSystem.File.Exists(path))
        {
            LogInformation($"Loading config file from {_fileSystem.Path.GetFullPath(path)}.");

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
                try
                {
                    json = _fileSystem.File.ReadAllText(path);
                    break;
                }
                catch (IOException ex)
                {
                    LogWarning($"IO Exception, retrying due to {ex.Message}");
                    if (runCount == FileUtilities.RunLimit)
                    {
                        throw;
                    }

                    Thread.Sleep(TimeSpan.FromSeconds(Math.Pow(FileUtilities.ExponentialRetryBase, runCount)));
                    runCount++;
                }
            }

            // Use default replacement settings if none provided
            replacementSettings ??= new DeserializationVariableReplacementSettings();

            if (!string.IsNullOrEmpty(json) && TryParseConfig(
                json,
                out RuntimeConfig,
                replacementSettings,
                logger: null,
                connectionString: _connectionString))
            {
                if (TrySetupConfigFileWatcher())
                {
                    // Use LogInformation (routes through _logger, buffer, or Console) to avoid
                    // duplicate entries when both _logger and the caller's logger are set.
                    LogInformation($"Monitoring config: {ConfigFilePath} for hot-reloading.");
                }

                // When isDevMode is not null it means we are in a hot-reload scenario, and need to save the previous
                // mode in the new RuntimeConfig since we do not support hot-reload of the mode.
                if (isDevMode is not null && RuntimeConfig.Runtime is not null && RuntimeConfig.Runtime.Host is not null)
                {
                    // Log error when the mode is changed during hot-reload.
                    if (isDevMode != this.RuntimeConfig.IsDevelopmentMode())
                    {
                        LogError("Hot-reload doesn't support switching mode. Please restart the service to switch the mode.");
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

            config = null;
            return false;
        }

        string configMissingError = $"Unable to find config file: {path} does not exist.";
        LogError(configMissingError);

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
    private void HotReloadConfig(bool isDevMode, ILogger? logger = null)
    {
        logger?.LogInformation(message: "Starting hot-reload process for config: {ConfigFilePath}", ConfigFilePath);

        // Use default replacement settings for hot reload
        DeserializationVariableReplacementSettings replacementSettings = new(azureKeyVaultOptions: null, doReplaceEnvVar: true, doReplaceAkvVar: true);

        if (!TryLoadConfig(ConfigFilePath, out _, logger: logger, isDevMode: isDevMode, replacementSettings: replacementSettings))
        {
            throw new DataApiBuilderException(
                message: "Deserialization of the configuration file failed.",
                statusCode: HttpStatusCode.ServiceUnavailable,
                subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
        }

        IsNewConfigDetected = true;
        IsNewConfigValidated = false;
        SignalConfigChanged();

        logger?.LogInformation("Hot-reload process finished.");
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

    /// <summary>
    /// Wires the DI-provided logger into this loader.
    /// Called from <c>Startup.Configure</c> once the DI container has been fully built.
    /// After this call, subsequent log writes use <paramref name="logger"/> directly.
    /// </summary>
    /// <param name="logger">The resolved logger for this class.</param>
    public void SetLogger(ILogger<FileSystemRuntimeConfigLoader> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Flushes all log entries that were buffered before the logger was available.
    /// Should be called immediately after <see cref="SetLogger"/> in
    /// <c>Startup.Configure</c> so that early-startup messages are not lost.
    /// </summary>
    public void FlushLogBuffer()
    {
        _logBuffer?.FlushToLogger(_logger);
    }

    /// <summary>
    /// Routes an Information-level message to the logger, the buffer, or (as a last resort)
    /// to <see cref="Console"/> so that it is never silently dropped.
    /// </summary>
    private void LogInformation(string message)
    {
        if (_logger is not null)
        {
            _logger.LogInformation(message);
        }
        else if (_logBuffer is not null)
        {
            _logBuffer.BufferLog(LogLevel.Information, message);
        }
        else
        {
            Console.WriteLine(message);
        }
    }

    /// <summary>
    /// Routes a Warning-level message to the logger, the buffer, or (as a last resort)
    /// to <see cref="Console"/> so that it is never silently dropped.
    /// </summary>
    private void LogWarning(string message)
    {
        if (_logger is not null)
        {
            _logger.LogWarning(message);
        }
        else if (_logBuffer is not null)
        {
            _logBuffer.BufferLog(LogLevel.Warning, message);
        }
        else
        {
            Console.WriteLine(message);
        }
    }

    /// <summary>
    /// Routes an Error-level message to the logger, the buffer, or (as a last resort)
    /// to <see cref="Console.Error"/> so that it is never silently dropped.
    /// </summary>
    private void LogError(string message)
    {
        if (_logger is not null)
        {
            _logger.LogError(message);
        }
        else if (_logBuffer is not null)
        {
            _logBuffer.BufferLog(LogLevel.Error, message);
        }
        else
        {
            Console.Error.WriteLine(message);
        }
    }
}
