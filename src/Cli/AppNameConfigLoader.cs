// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.Converters;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Exceptions;

namespace Cli;

/// <summary>
/// Reads a configuration for telemetry inspection without starting the runtime, creating watchers,
/// injecting connection strings, or resolving Key Vault secrets. Uses the normal property converters
/// but explicitly owns child-file traversal, because the runtime model's JSON constructor loads
/// children with the runtime's secret-resolution policy.
/// </summary>
internal static class AppNameConfigLoader
{
    internal static bool TryLoadConfig(string path, IFileSystem fileSystem, [NotNullWhen(true)] out RuntimeConfig? config)
    {
        DeserializationVariableReplacementSettings replacements = new(
            doReplaceEnvVar: true,
            doReplaceAkvVar: false,
            envFailureMode: EnvironmentVariableReplacementFailureMode.Ignore)
        {
            SkipApplicationNameInjection = true,
        };

        try
        {
            JsonSerializerOptions options = RuntimeConfigLoader.GetSerializationOptions(replacements);
            HashSet<string> ancestors = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            config = ReadConfig(path, fileSystem, options, ancestors);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or DataApiBuilderException or IOException
            or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            // Exceptions from deserialization may include credential-bearing values. The command
            // reports a generic parse failure instead of exposing those details to the logger.
            config = null;
            return false;
        }
    }

    private static RuntimeConfig ReadConfig(string path, IFileSystem fileSystem, JsonSerializerOptions options, HashSet<string> ancestors)
    {
        const int maxConfigDepth = 64;
        string fullPath = fileSystem.Path.GetFullPath(path);
        if (ancestors.Count >= maxConfigDepth || !ancestors.Add(fullPath))
        {
            throw new JsonException("Cyclic or excessively nested data-source-files.");
        }

        try
        {
            JsonObject document = JsonNode.Parse(
                fileSystem.File.ReadAllText(fullPath),
                documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip }) as JsonObject
                ?? throw new JsonException("Configuration must be a JSON object.");

            DataSourceFiles? files = document["data-source-files"]?.Deserialize<DataSourceFiles>(options);
            document.Remove("data-source-files");
            RuntimeConfig root = document.Deserialize<RuntimeConfig>(options)
                ?? throw new JsonException("Configuration is empty.");

            // Removing only the child-file property preserves the standard converters/defaults for
            // every other property while preventing constructor-driven runtime secret resolution.
            List<(string FileName, RuntimeConfig Config)> children = new();
            foreach (string childPath in files?.SourceFiles ?? Enumerable.Empty<string>())
            {
                // Match runtime semantics: file paths are relative to the process working directory,
                // and absent optional child files are not counted as configured data sources.
                if (fileSystem.File.Exists(childPath))
                {
                    RuntimeConfig child = ReadConfig(childPath, fileSystem, options, ancestors);
                    child.IsChildConfig = true;
                    children.Add((childPath, child));
                }
            }

            IEnumerable<RuntimeConfig> configs = new[] { root }.Concat(children.Select(child => child.Config));
            Dictionary<string, DataSource> sources = configs.SelectMany(config => config.GetDataSourceNamesToDataSourcesIterator())
                .ToDictionary(entry => entry.Key, entry => entry.Value);
            Dictionary<string, Entity> entities = configs.SelectMany(config => config.Entities)
                .ToDictionary(entry => entry.Key, entry => entry.Value);
            Dictionary<string, Autoentity> autoentities = configs.SelectMany(config => config.Autoentities)
                .ToDictionary(entry => entry.Key, entry => entry.Value);
            Dictionary<string, string> entitySources = configs.SelectMany(config => config.Entities
                .Where(_ => config.DataSource is not null || config.ChildConfigs.Count > 0)
                .Select(entity => new KeyValuePair<string, string>(entity.Key, config.GetDataSourceNameFromEntityName(entity.Key))))
                .ToDictionary(entry => entry.Key, entry => entry.Value);

            // Use the existing explicit-data-source-map constructor: it performs no file loading.
            // This snapshot is for inspection only, never published to the runtime/provider.
            RuntimeConfig result = new(root.Schema, root.DataSource!, root.Runtime!, new(entities),
                root.DefaultDataSourceName, sources, entitySources, files, root.AzureKeyVault, new(autoentities));
            result.ChildConfigs.AddRange(children);
            return result;
        }
        finally
        {
            ancestors.Remove(fullPath);
        }
    }
}