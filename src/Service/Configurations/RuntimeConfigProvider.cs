// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Exceptions;

namespace Azure.DataApiBuilder.Service.Configurations;

public class RuntimeConfigProvider
{
    public delegate Task<bool> RuntimeConfigLoadedHandler(RuntimeConfigProvider sender, RuntimeConfig config);

    public List<RuntimeConfigLoadedHandler> RuntimeConfigLoadedHandlers { get; } = new List<RuntimeConfigLoadedHandler>();

    private readonly RuntimeConfigLoader _runtimeConfigLoader;
    private RuntimeConfig? _runtimeConfig;
    public string? ConfigFilePath;

    public RuntimeConfigProvider(RuntimeConfigLoader runtimeConfigLoader)
    {
        _runtimeConfigLoader = runtimeConfigLoader;
    }

    public RuntimeConfig? GetConfig(string path)
    {
        if (_runtimeConfig is not null)
        {
            return _runtimeConfig;
        }

        if (_runtimeConfigLoader.TryLoadConfig(path, out RuntimeConfig? config))
        {
            ConfigFilePath = path;
            _runtimeConfig = config;
        }

        return config;
    }

    public RuntimeConfig GetConfig()
    {
        if (_runtimeConfig is not null)
        {
            return _runtimeConfig;
        }

        if (_runtimeConfigLoader.TryLoadDefaultConfig(out RuntimeConfig? config))
        {
            _runtimeConfig = config;
        }

        if (_runtimeConfig is null)
        {
            throw new DataApiBuilderException(
                message: "Runtime config isn't setup.",
                statusCode: HttpStatusCode.InternalServerError,
                subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
        }

        return _runtimeConfig;
    }

    /// <summary>
    /// Attempt to acquire runtime configuration metadata.
    /// </summary>
    /// <param name="runtimeConfig">Populated runtime configuration, if present.</param>
    /// <returns>True when runtime config is provided, otherwise false.</returns>
    public bool TryGetConfig([NotNullWhen(true)] out RuntimeConfig? runtimeConfig)
    {
        runtimeConfig = _runtimeConfig;
        return _runtimeConfig is not null;
    }
}
