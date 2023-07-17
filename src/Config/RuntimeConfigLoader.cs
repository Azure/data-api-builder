// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.Converters;
using Azure.DataApiBuilder.Config.NamingPolicies;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Config.ObjectModel;

public abstract class RunTimeConfigLoader
{
    protected readonly string? _connectionString;

    public RunTimeConfigLoader(string? connectionString = null)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    /// Returns RunTimeConfig. Needs to be implemented by the derived class.
    /// </summary>
    /// <param name="config">The loaded <c>RuntimeConfig</c>, or null if none was loaded.</param>
    /// <returns>True if the config was loaded, otherwise false.</returns>
    public abstract bool TryLoadKnownConfig([NotNullWhen(true)] out RuntimeConfig? config);

    /// <summary>
    /// Parses a JSON string into a <c>RuntimeConfig</c> object
    /// </summary>
    /// <param name="json">JSON that represents the config file.</param>
    /// <param name="config">The parsed config, or null if it parsed unsuccessfully.</param>
    /// <returns>True if the config was parsed, otherwise false.</returns>
    public static bool TryParseConfig(string json, [NotNullWhen(true)] out RuntimeConfig? config, ILogger? logger = null, string? connectionString = null)
    {
        JsonSerializerOptions options = GetSerializationOptions();

        try
        {
            config = JsonSerializer.Deserialize<RuntimeConfig>(json, options);

            if (config is null)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(connectionString))
            {
                config = config with { DataSource = config.DataSource with { ConnectionString = connectionString } };
            }
        }
        catch (JsonException ex)
        {
            string errorMessage = $"Deserialization of the configuration file failed.\n" +
                        $"Message:\n {ex.Message}\n" +
                        $"Stack Trace:\n {ex.StackTrace}";

            if (logger is null)
            {
                // logger can be null when called from CLI
                Console.Error.WriteLine(errorMessage);
            }
            else
            {
                logger.LogError(ex, errorMessage);
            }

            config = null;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Get Serializer options for the config file.
    /// </summary>
    public static JsonSerializerOptions GetSerializationOptions()
    {
        JsonSerializerOptions options = new()
        {
            PropertyNameCaseInsensitive = false,
            PropertyNamingPolicy = new HyphenatedNamingPolicy(),
            ReadCommentHandling = JsonCommentHandling.Skip,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new EnumMemberJsonEnumConverterFactory());
        options.Converters.Add(new RestRuntimeOptionsConverterFactory());
        options.Converters.Add(new GraphQLRuntimeOptionsConverterFactory());
        options.Converters.Add(new EntitySourceConverterFactory());
        options.Converters.Add(new EntityActionConverterFactory());
        options.Converters.Add(new StringJsonConverterFactory());
        return options;
    }
}
