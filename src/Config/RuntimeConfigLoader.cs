// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO.Abstractions;
using System.Text.Json;
using Azure.DataApiBuilder.Config.Converters;
using Azure.DataApiBuilder.Config.NamingPolicies;

namespace Azure.DataApiBuilder.Config;

public class RuntimeConfigLoader
{
    private RuntimeConfig? _runtimeConfig;
    private readonly IFileSystem _fileSystem;

    public const string CONFIGFILE_NAME = "dab-config";
    public const string CONFIG_EXTENSION = ".json";

    public const string ENVIRONMENT_PREFIX = "DAB_";
    public const string RUNTIME_ENVIRONMENT_VAR_NAME = $"{ENVIRONMENT_PREFIX}ENVIRONMENT";

    public static bool CheckPrecedenceForConfigInEngine = true;

    public RuntimeConfigLoader(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Load the runtime config from the specified path.
    /// </summary>
    /// <param name="path">The path to the dab-config.json file.</param>
    /// <param name="config">The loaded <c>RuntimeConfig</c>, or null if none was loaded.</param>
    /// <returns>True if the config was loaded, otherwise false.</returns>
    public bool TryLoadConfig(string path, out RuntimeConfig? config)
    {
        if (_runtimeConfig != null)
        {
            config = _runtimeConfig;
            return true;
        }

        JsonSerializerOptions options = new()
        {
            PropertyNameCaseInsensitive = false,
            PropertyNamingPolicy = new HyphenatedNamingPolicy()
        };
        options.Converters.Add(new HyphenatedJsonEnumConverterFactory());
        options.Converters.Add(new RestRuntimeOptionsConverterFactory());
        options.Converters.Add(new GraphQLRuntimeOptionsConverterFactory());
        options.Converters.Add(new EntitySourceConverterFactory());
        options.Converters.Add(new EntityActionConverterFactory());

        if (_fileSystem.File.Exists(path))
        {
            string json = _fileSystem.File.ReadAllText(path);
            _runtimeConfig = JsonSerializer.Deserialize<RuntimeConfig>(json, options);
            config = _runtimeConfig;
            return true;
        }

        config = null;
        return false;
    }

    /// <summary>
    /// Tries to load the config file using its default name and for the default environment.
    /// </summary>
    /// <param name="config">The loaded <c>RuntimeConfig</c>, or null if none was loaded.</param>
    /// <returns>True if the config was loaded, otherwise false.</returns>
    public bool TryLoadDefaultConfig(out RuntimeConfig? config)
    {
        string filename = GetFileName(Environment.GetEnvironmentVariable(RUNTIME_ENVIRONMENT_VAR_NAME), false);

        return TryLoadConfig(filename, out config);
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
    /// <param name="hostingEnvironmentName">Value of ASPNETCORE_ENVIRONMENT variable</param>
    /// <param name="considerOverrides">whether to look for overrides file or not.</param>
    /// <returns></returns>
    public string GetFileNameForEnvironment(string? hostingEnvironmentName, bool considerOverrides)
    {
        // if precedence check is done in cli, no need to do it again after starting the engine.
        if (!CheckPrecedenceForConfigInEngine)
        {
            return string.Empty;
        }

        string configFileNameWithExtension = string.Empty;
        string?[] environmentPrecedence = new[]
        {
            Environment.GetEnvironmentVariable(RUNTIME_ENVIRONMENT_VAR_NAME),
            hostingEnvironmentName,
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

    // Used for testing
    internal static string DefaultName
    {
        get
        {
            return $"{CONFIGFILE_NAME}{CONFIG_EXTENSION}";
        }
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
    private string GetFileName(string? environmentValue, bool considerOverrides)
    {
        string configFileName =
            !string.IsNullOrEmpty(environmentValue)
            ? $"{CONFIGFILE_NAME}.{environmentValue}"
            : $"{CONFIGFILE_NAME}";
        string configFileNameWithExtension = $"{configFileName}{CONFIG_EXTENSION}";
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

    private bool DoesFileExistInCurrentDirectory(string fileName)
    {
        string currentDir = _fileSystem.Directory.GetCurrentDirectory();
        // Unable to use ILogger because this code is invoked before LoggerFactory
        // is instantiated.
        if (_fileSystem.File.Exists(_fileSystem.Path.Combine(currentDir, fileName)))
        {
            // This config file is logged as being found, but may not actually be used!
            Console.WriteLine($"Found config file: {fileName}.");
            return true;
        }
        else
        {
            // Unable to use ILogger because this code is invoked before LoggerFactory
            // is instantiated.
            Console.WriteLine($"Unable to find config file: {fileName} does not exist.");
            return false;
        }
    }
}

