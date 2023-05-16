// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Exceptions;

namespace Azure.DataApiBuilder.Service.Configurations;

public class RuntimeConfigProvider
{
    public delegate Task<bool> RuntimeConfigLoadedHandler(RuntimeConfigProvider sender, RuntimeConfig config);

    public List<RuntimeConfigLoadedHandler> RuntimeConfigLoadedHandlers { get; } = new List<RuntimeConfigLoadedHandler>();

    /// <summary>
    /// Indicates whether the config was loaded after the runtime was initialized.
    /// </summary>
    /// <remarks>This is most commonly used when DAB's config is provided via the <c>ConfigurationController</c>, such as when it's a hosted service.</remarks>
    public bool IsLateConfigured { get; set; }

    /// <summary>
    /// The access token representing a Managed Identity to connect to the database.
    /// </summary>
    public string? ManagedIdentityAccessToken { get; private set; }

    private readonly RuntimeConfigLoader _runtimeConfigLoader;
    private RuntimeConfig? _runtimeConfig;

    public string RuntimeConfigFileName => _runtimeConfigLoader.ConfigFileName;

    public RuntimeConfigProvider(RuntimeConfigLoader runtimeConfigLoader)
    {
        _runtimeConfigLoader = runtimeConfigLoader;
    }

    public RuntimeConfig GetConfig()
    {
        if (_runtimeConfig is not null)
        {
            return _runtimeConfig;
        }

        if (_runtimeConfigLoader.TryLoadKnownConfig(out RuntimeConfig? config))
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
        if (_runtimeConfig is null)
        {
            if (_runtimeConfigLoader.TryLoadKnownConfig(out RuntimeConfig? config))
            {
                _runtimeConfig = config;
            }
        }

        runtimeConfig = _runtimeConfig;
        return _runtimeConfig is not null;
    }

    /// <summary>
    /// Initialize the runtime configuration provider with the specified configurations.
    /// This initialization method is used when the configuration is sent to the ConfigurationController
    /// in the form of a string instead of reading the configuration from a configuration file.
    /// This method assumes the connection string is provided as part of the configuration.
    /// </summary>
    /// <param name="configuration">The engine configuration.</param>
    /// <param name="schema">The GraphQL Schema. Can be left null for SQL configurations.</param>
    /// <param name="accessToken">The string representation of a managed identity access token</param>
    /// <returns>true if the initialization succeeded, false otherwise.</returns>
    public async Task<bool> Initialize(
        string configuration,
        string? schema,
        string? accessToken)
    {
        if (string.IsNullOrEmpty(configuration))
        {
            throw new ArgumentException($"'{nameof(configuration)}' cannot be null or empty.", nameof(configuration));
        }

        if (RuntimeConfigLoader.TryParseConfig(
                configuration,
                out RuntimeConfig? runtimeConfig))
        {
            _runtimeConfig = runtimeConfig;

            if (string.IsNullOrEmpty(runtimeConfig.DataSource.ConnectionString))
            {
                throw new ArgumentException($"'{nameof(runtimeConfig.DataSource.ConnectionString)}' cannot be null or empty.", nameof(runtimeConfig.DataSource.ConnectionString));
            }

            if (_runtimeConfig.DataSource.DatabaseType == DatabaseType.CosmosDB_NoSQL)
            {
                _runtimeConfig = HandleCosmosNoSqlConfiguration(schema, _runtimeConfig, _runtimeConfig.DataSource.ConnectionString);
            }
        }

        ManagedIdentityAccessToken = accessToken;

        bool configLoadSucceeded = await InvokeConfigLoadedHandlersAsync();

        IsLateConfigured = true;

        return configLoadSucceeded;
    }

    /// <summary>
    /// Initialize the runtime configuration provider with the specified configurations.
    /// This initialization method is used when the configuration is sent to the ConfigurationController
    /// in the form of a string instead of reading the configuration from a configuration file.
    /// </summary>
    /// <param name="configuration">The engine configuration.</param>
    /// <param name="schema">The GraphQL Schema. Can be left null for SQL configurations.</param>
    /// <param name="connectionString">The connection string to the database.</param>
    /// <param name="accessToken">The string representation of a managed identity access token</param>
    /// <returns>true if the initialization succeeded, false otherwise.</returns>
    public async Task<bool> Initialize(string jsonConfig, string? graphQLSchema, string connectionString, string? accessToken)
    {
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new ArgumentException($"'{nameof(connectionString)}' cannot be null or empty.", nameof(connectionString));
        }

        if (string.IsNullOrEmpty(jsonConfig))
        {
            throw new ArgumentException($"'{nameof(jsonConfig)}' cannot be null or empty.", nameof(jsonConfig));
        }

        ManagedIdentityAccessToken = accessToken;

        if (RuntimeConfigLoader.TryParseConfig(jsonConfig, out RuntimeConfig? runtimeConfig))
        {
            _runtimeConfig = runtimeConfig.DataSource.DatabaseType switch
            {
                DatabaseType.CosmosDB_NoSQL => HandleCosmosNoSqlConfiguration(graphQLSchema, runtimeConfig, connectionString),
                _ => runtimeConfig with { DataSource = runtimeConfig.DataSource with { ConnectionString = connectionString } }
            };

            return await InvokeConfigLoadedHandlersAsync();
        }

        return false;
    }

    private async Task<bool> InvokeConfigLoadedHandlersAsync()
    {
        List<Task<bool>> configLoadedTasks = new();
        if (_runtimeConfig is not null)
        {
            foreach (RuntimeConfigLoadedHandler configLoadedHandler in RuntimeConfigLoadedHandlers)
            {
                configLoadedTasks.Add(configLoadedHandler(this, _runtimeConfig));
            }
        }

        bool[] results = await Task.WhenAll(configLoadedTasks);

        // Verify that all tasks succeeded.
        return results.All(x => x);
    }

    private static RuntimeConfig HandleCosmosNoSqlConfiguration(string? schema, RuntimeConfig runtimeConfig, string connectionString)
    {
        DbConnectionStringBuilder dbConnectionStringBuilder = new()
        {
            ConnectionString = connectionString
        };

        if (string.IsNullOrEmpty(schema))
        {
            throw new ArgumentException($"'{nameof(schema)}' cannot be null or empty.", nameof(schema));
        }

        Dictionary<string, JsonElement> options = new(runtimeConfig.DataSource.Options)
        {
            // push the "raw" GraphQL schema into the options to pull out later when requested
            { CosmosDbNoSQLDataSourceOptions.GRAPHQL_RAW_KEY, JsonSerializer.SerializeToElement(schema) }
        };

        // SWA may provide CosmosDB database name in connectionString
        string? database = dbConnectionStringBuilder.ContainsKey("Database") ? (string)dbConnectionStringBuilder["Database"] : null;

        if (database is not null)
        {
            // Add or update the options to contain the parsed database
            options["database"] = JsonSerializer.SerializeToElement(database);
        }

        // Update the connection string in the parsed config with the one that was provided to the controller
        return runtimeConfig
            with
        {
            DataSource = runtimeConfig.DataSource
            with
            { Options = options, ConnectionString = connectionString }
        };
    }

}
