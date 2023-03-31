// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.Converters;
using Azure.DataApiBuilder.Config.NamingPolicies;
using System.IO.Abstractions;
using System.Text.Json;

namespace Azure.DataApiBuilder.Config;
public class RuntimeConfigLoader
{
    public RuntimeConfigLoader(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    private RuntimeConfig? _runtimeConfig;
    private readonly IFileSystem _fileSystem;

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
}

