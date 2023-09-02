// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.Converters;
using Azure.DataApiBuilder.Config.NamingPolicies;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Config;

public abstract class RuntimeConfigLoader
{
    protected readonly string? _connectionString;

    public RuntimeConfigLoader(string? connectionString = null)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    /// Returns RuntimeConfig.
    /// </summary>
    /// <param name="config">The loaded <c>RuntimeConfig</c>, or null if none was loaded.</param>
    /// <param name="replaceEnvVar">Whether to replace environment variable with its
    /// value or not while deserializing.</param>
    /// <returns>True if the config was loaded, otherwise false.</returns>
    public abstract bool TryLoadKnownConfig([NotNullWhen(true)] out RuntimeConfig? config, bool replaceEnvVar = false);

    /// <summary>
    /// Returns the link to the published draft schema.
    /// </summary>
    /// <returns></returns>
    public abstract string GetPublishedDraftSchemaLink();

    /// <summary>
    /// Parses a JSON string into a <c>RuntimeConfig</c> object
    /// </summary>
    /// <param name="json">JSON that represents the config file.</param>
    /// <param name="config">The parsed config, or null if it parsed unsuccessfully.</param>
    /// <returns>True if the config was parsed, otherwise false.</returns>
    /// <param name="replaceEnvVar">Whether to replace environment variable with its
    /// value or not while deserializing. By default, no replacement happens.</param>
    public static bool TryParseConfig(string json,
        [NotNullWhen(true)] out RuntimeConfig? config,
        ILogger? logger = null,
        string? connectionString = null,
        bool replaceEnvVar = false)
    {
        JsonSerializerOptions options = GetSerializationOptions(replaceEnvVar);

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

            config = GetConfigWithDefaultsForNullProps(config);
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

    public static RuntimeConfig GetConfigWithDefaultsForNullProps(RuntimeConfig config)
    {
        // For Cosmos DB NoSQL database type, DAB CLI v0.8.49+ generates a REST property within the Runtime section of the config file. However
        // v0.7.6- does not generate this property. So, when the config file generated using v0.7.6- is used to start the engine with v0.8.49+, the absence
        // of the REST property causes the engine to throw exceptions. This is the only difference in the way Runtime section of the config file is created
        // between these two versions.
        // To avoid the NullReference Exceptions, the REST property is added when absent in the config file.
        // Other properties within the Runtime section are also populated with default values to account for the cases where
        // the properties could be removed manually from the config file.
        if (config.Runtime is not null)
        {
            if (config.Runtime.Rest is null)
            {
                config = config with { Runtime = config.Runtime with { Rest = (config.DataSource.DatabaseType is DatabaseType.CosmosDB_NoSQL) ? new RestRuntimeOptions(Enabled: false) : new RestRuntimeOptions(Enabled: false) } };
            }

            if (config.Runtime.GraphQL is null)
            {
                config = config with { Runtime = config.Runtime with { GraphQL = new GraphQLRuntimeOptions(AllowIntrospection: false) } };
            }

            if (config.Runtime.Host is null)
            {
                config = config with { Runtime = config.Runtime with { Host = new HostOptions(Cors: null, Authentication: new AuthenticationOptions(Provider: EasyAuthType.StaticWebApps.ToString(), Jwt: null), Mode: HostMode.Production) } };
            }
        }

        return config;
    }

    /// <summary>
    /// Get Serializer options for the config file.
    /// </summary>
    /// <param name="replaceEnvVar">Whether to replace environment variable with value or not while deserializing.
    /// By default, no replacement happens.</param>
    public static JsonSerializerOptions GetSerializationOptions(bool replaceEnvVar = false)
    {
        JsonSerializerOptions options = new()
        {
            PropertyNameCaseInsensitive = false,
            PropertyNamingPolicy = new HyphenatedNamingPolicy(),
            ReadCommentHandling = JsonCommentHandling.Skip,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            IncludeFields = true
        };
        options.Converters.Add(new EnumMemberJsonEnumConverterFactory());
        options.Converters.Add(new RestRuntimeOptionsConverterFactory());
        options.Converters.Add(new GraphQLRuntimeOptionsConverterFactory());
        options.Converters.Add(new EntitySourceConverterFactory(replaceEnvVar));
        options.Converters.Add(new EntityGraphQLOptionsConverterFactory(replaceEnvVar));
        options.Converters.Add(new EntityRestOptionsConverterFactory(replaceEnvVar));
        options.Converters.Add(new EntityActionConverterFactory());

        if (replaceEnvVar)
        {
            options.Converters.Add(new StringJsonConverterFactory());
        }

        return options;
    }
}
