// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using static Azure.DataApiBuilder.Config.FileSystemRuntimeConfigLoader;

namespace Azure.DataApiBuilder.Service.Tests.Configuration;

/// <summary>
/// Provides the methods to read the configuration files from disk for tests.
/// </summary>
internal static class TestConfigFileReader
{
    public static RuntimeConfig ReadCosmosConfigurationFromFile()
    {
        string cosmosFile = $"{CONFIGFILE_NAME}.{TestCategory.COSMOSDBNOSQL}{CONFIG_EXTENSION}";

        string configurationFileContents = File.ReadAllText(cosmosFile);
        if (!RuntimeConfigLoader.TryParseConfig(configurationFileContents, out RuntimeConfig config))
        {
            throw new Exception("Failed to parse configuration file.");
        }

        // The Schema file isn't provided in the configuration file when going through the configuration endpoint so we're removing it.
        _ = config.DataSource.Options.Remove("Schema");
        return config;
    }
}
