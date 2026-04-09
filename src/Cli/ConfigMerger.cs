// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO.Abstractions;
using Azure.DataApiBuilder.Config;
using Microsoft.Extensions.Logging;

namespace Cli;

public static class ConfigMerger
{
    /// <summary>
    /// This method will check if DAB_ENVIRONMENT value is set.
    /// If yes, it will try to merge dab-config.json with dab-config.{DAB_ENVIRONMENT}.json
    /// and create a merged file called dab-config.{DAB_ENVIRONMENT}.merged.json
    /// </summary>
    /// <returns>Returns the name of the merged Config if successful.</returns>
    public static bool TryMergeConfigsIfAvailable(IFileSystem fileSystem, FileSystemRuntimeConfigLoader loader, ILogger logger, LogBuffer? cliBuffer, out string? mergedConfigFile)
    {
        string? environmentValue = Environment.GetEnvironmentVariable(FileSystemRuntimeConfigLoader.RUNTIME_ENVIRONMENT_VAR_NAME);
        mergedConfigFile = null;
        if (!string.IsNullOrEmpty(environmentValue))
        {
            string baseConfigFile = FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME;
            string environmentBasedConfigFile = loader.GetFileName(environmentValue, considerOverrides: false);

            if (loader.DoesFileExistInDirectory(baseConfigFile) && !string.IsNullOrEmpty(environmentBasedConfigFile))
            {
                try
                {
                    string baseConfigJson = fileSystem.File.ReadAllText(baseConfigFile);
                    string overrideConfigJson = fileSystem.File.ReadAllText(environmentBasedConfigFile);

                    string currentDir = fileSystem.Directory.GetCurrentDirectory();
                    ConfigGenerator.SendLogToBufferOrLogger(cliBuffer, LogLevel.Information, $"Merging {Path.Combine(currentDir, baseConfigFile)} and {Path.Combine(currentDir, environmentBasedConfigFile)}");
                    string mergedConfigJson = MergeJsonProvider.Merge(baseConfigJson, overrideConfigJson);
                    mergedConfigFile = FileSystemRuntimeConfigLoader.GetMergedFileNameForEnvironment(FileSystemRuntimeConfigLoader.CONFIGFILE_NAME, environmentValue);
                    fileSystem.File.WriteAllText(mergedConfigFile, mergedConfigJson);
                    ConfigGenerator.SendLogToBufferOrLogger(cliBuffer, LogLevel.Information, $"Generated merged config file: {Path.Combine(currentDir, mergedConfigFile)}");
                    return true;
                }
                catch (Exception ex)
                {
                    ConfigGenerator.SendLogToBufferOrLogger(cliBuffer, LogLevel.Error, "Failed to merge the config files.", ex);
                    mergedConfigFile = null;
                    return false;
                }
            }
        }

        return false;
    }
}
