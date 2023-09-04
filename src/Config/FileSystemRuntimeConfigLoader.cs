// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
using System.Net;
using System.Reflection;
using System.Text.Json;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Exceptions;

namespace Azure.DataApiBuilder.Config;

/// <summary>
/// This class is responsible for loading the runtime config from either a JSON string
/// or a file located on disk, depending on how the service is being run.
/// </summary>
/// <remarks>
/// This class does not maintain any internal state of the loaded config, instead it will
/// always generate a new config when it is requested.
///
/// To support better testability, the <see cref="IFileSystem"/> abstraction is provided
/// which allows for mocking of the file system in tests, providing a way to run the test
/// in isolation of other tests or the actual file system.
/// </remarks>
public class FileSystemRuntimeConfigLoader : RuntimeConfigLoader
{
    // This stores either the default config name e.g. dab-config.json
    // or user provided config file.
    private string _baseConfigFileName;

    private readonly IFileSystem _fileSystem;

    public const string CONFIGFILE_NAME = "dab-config";
    public const string CONFIG_EXTENSION = ".json";
    public const string ENVIRONMENT_PREFIX = "DAB_";
    public const string RUNTIME_ENVIRONMENT_VAR_NAME = $"{ENVIRONMENT_PREFIX}ENVIRONMENT";
    public const string RUNTIME_ENV_CONNECTION_STRING = $"{ENVIRONMENT_PREFIX}CONNSTRING";
    public const string ASP_NET_CORE_ENVIRONMENT_VAR_NAME = "ASPNETCORE_ENVIRONMENT";
    public const string SCHEMA = "dab.draft.schema.json";

    /// <summary>
    /// Returns the default config file name.
    /// </summary>
    public const string DEFAULT_CONFIG_FILE_NAME = $"{CONFIGFILE_NAME}{CONFIG_EXTENSION}";

    /// <summary>
    /// Stores the config file name actually loaded by the engine.
    /// It could be the base config file (e.g. dab-config.json), any of its derivatives with
    /// environment specific suffixes (e.g. dab-config.Development.json) or the user provided
    /// config file name.
    /// </summary>
    public string ConfigFileName { get; internal set; }

    public FileSystemRuntimeConfigLoader(IFileSystem fileSystem, string baseConfigFileName = DEFAULT_CONFIG_FILE_NAME, string? connectionString = null)
        : base(connectionString)
    {
        _fileSystem = fileSystem;
        _baseConfigFileName = baseConfigFileName;
        ConfigFileName = GetFileNameForEnvironment(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), false);
        // if fileName based on environment is empty, then use the default config file name.
        if (string.IsNullOrWhiteSpace(ConfigFileName))
        {
            ConfigFileName = baseConfigFileName;
        }
    }

    /// <summary>
    /// Load the runtime config from the specified path.
    /// </summary>
    /// <param name="path">The path to the dab-config.json file.</param>
    /// <param name="config">The loaded <c>RuntimeConfig</c>, or null if none was loaded.</param>
    /// <param name="replaceEnvVar">Whether to replace environment variable with its
    /// value or not while deserializing.</param>
    /// <returns>True if the config was loaded, otherwise false.</returns>
    public bool TryLoadConfig(
        string path,
        [NotNullWhen(true)] out RuntimeConfig? config,
        bool replaceEnvVar = false)
    {
        if (_fileSystem.File.Exists(path))
        {
            Console.WriteLine($"Loading config file from {path}.");
            string json = _fileSystem.File.ReadAllText(path);
            return TryParseConfig(json, out config, connectionString: _connectionString, replaceEnvVar: replaceEnvVar);
        }
        else
        {
            // Unable to use ILogger because this code is invoked before LoggerFactory
            // is instantiated.
            Console.WriteLine($"Unable to find config file: {path} does not exist.");
        }

        config = null;
        return false;
    }

    /// <summary>
    /// Tries to load the config file using the filename known to the RuntimeConfigLoader and for the default environment.
    /// </summary>
    /// <param name="config">The loaded <c>RuntimeConfig</c>, or null if none was loaded.</param>
    /// <param name="replaceEnvVar">Whether to replace environment variable with its
    /// value or not while deserializing.</param>
    /// <returns>True if the config was loaded, otherwise false.</returns>
    public override bool TryLoadKnownConfig([NotNullWhen(true)] out RuntimeConfig? config, bool replaceEnvVar = false)
    {
        return TryLoadConfig(ConfigFileName, out config, replaceEnvVar);
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
        // if the baseConfigFileName contains a path, we need to insure that is not lost. for example: baseConfigFileName = "config/dab-config.json"
        // in this case, we need to get the directory name and the file name without extension and then combine them back. Else, we will lose the path
        // and the file will be searched in the current directory.
        string fileNameWithoutExtension = _fileSystem.Path.Combine(_fileSystem.Path.GetDirectoryName(_baseConfigFileName) ?? string.Empty, _fileSystem.Path.GetFileNameWithoutExtension(_baseConfigFileName));
        string fileExtension = _fileSystem.Path.GetExtension(_baseConfigFileName);
        string configFileName =
            !string.IsNullOrEmpty(environmentValue)
            ? $"{fileNameWithoutExtension}.{environmentValue}"
            : $"{fileNameWithoutExtension}";
        string configFileNameWithExtension = $"{configFileName}{fileExtension}";
        string overriddenConfigFileNameWithExtension = GetOverriddenName(configFileName);

        if (considerOverrides && DoesFileExistInCurrentDirectory(overriddenConfigFileNameWithExtension))
        {
            return overriddenConfigFileNameWithExtension;
        }

        if (DoesFileExistInCurrentDirectory(configFileNameWithExtension))
        {
            return configFileNameWithExtension;
        }

        return string.Empty;
    }

    private static string GetOverriddenName(string fileName)
    {
        return $"{fileName}.overrides{CONFIG_EXTENSION}";
    }

    /// <summary>
    /// Generates the name of the file based on environment value.
    /// NOTE: Input File name should not contain extension
    /// </summary>
    public static string GetEnvironmentFileName(string fileName, string environmentValue)
    {
        return $"{fileName}.{environmentValue}{CONFIG_EXTENSION}";
    }

    public bool DoesFileExistInCurrentDirectory(string fileName)
    {
        string currentDir = _fileSystem.Directory.GetCurrentDirectory();
        return _fileSystem.File.Exists(_fileSystem.Path.Combine(currentDir, fileName));
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
        Dictionary<string, object>? jsonDictionary = JsonSerializer.Deserialize<Dictionary<string, object>>(schemaFileContent, GetSerializationOptions());

        if (jsonDictionary is null)
        {
            throw new DataApiBuilderException(
                message: "The schema file is misconfigured. Please check the file formatting.",
                statusCode: HttpStatusCode.ServiceUnavailable,
                subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
        }

        object? additionalProperties;
        if (!jsonDictionary.TryGetValue("additionalProperties", out additionalProperties))
        {
            throw new DataApiBuilderException(
                message: "The schema file doesn't have the required field : additionalProperties",
                statusCode: HttpStatusCode.ServiceUnavailable,
                subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
        }

        // properties cannot be null since the property additionalProperties exist in the schema file.
        Dictionary<string, string> properties = JsonSerializer.Deserialize<Dictionary<string, string>>(additionalProperties.ToString()!)!;

        if (!properties.TryGetValue("version", out string? versionNum))
        {
            throw new DataApiBuilderException(message: "Missing required property 'version' in additionalProperties section.",
                statusCode: HttpStatusCode.ServiceUnavailable,
                subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
        }

        return versionNum;
    }

    public static string GetMergedFileNameForEnvironment(string fileName, string environmentValue)
    {
        return $"{fileName}.{environmentValue}.merged{CONFIG_EXTENSION}";
    }

    /// <summary>
    /// Allows the base config file and the actually loaded config file name(tracked by the property ConfigFileName)
    /// to be updated. This is commonly done when the CLI is starting up.
    /// </summary>
    /// <param name="fileName"></param>
    public void UpdateConfigFileName(string fileName)
    {
        _baseConfigFileName = fileName;
        ConfigFileName = fileName;
    }
}
