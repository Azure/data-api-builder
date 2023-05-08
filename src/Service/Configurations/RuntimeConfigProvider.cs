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

        DbConnectionStringBuilder dbConnectionStringBuilder = new()
        {
            ConnectionString = connectionString
        };

        ManagedIdentityAccessToken = accessToken;

        if (RuntimeConfigLoader.TryParseConfig(jsonConfig, out RuntimeConfig? runtimeConfig))
        {
            if (runtimeConfig.DataSource.DatabaseType is DatabaseType.CosmosDB_NoSQL)
            {
                if (graphQLSchema is null)
                {
                    throw new ArgumentNullException(nameof(graphQLSchema));
                }

                // push the "raw" GraphQL schema into the options to pull out later when requested
                runtimeConfig.DataSource.Options[CosmosDbNoSQLDataSourceOptions.GRAPHQL_RAW_KEY] = JsonSerializer.SerializeToElement(graphQLSchema);

                // SWA may provide CosmosDB database name in connectionString
                string? database = dbConnectionStringBuilder.ContainsKey("Database") ? (string)dbConnectionStringBuilder["Database"] : null;

                if (database is not null)
                {
                    // Add or update the options to contain the parsed database
                    runtimeConfig.DataSource.Options["database"] = JsonSerializer.SerializeToElement(database);
                }
            }

            // Update the connection string in the parsed config with the one that was provided to the controller
            _runtimeConfig = runtimeConfig with { DataSource = runtimeConfig.DataSource with { ConnectionString = connectionString } };

            List<Task<bool>> configLoadedTasks = new();
            if (_runtimeConfig is not null)
            {
                foreach (RuntimeConfigLoadedHandler configLoadedHandler in RuntimeConfigLoadedHandlers)
                {
                    configLoadedTasks.Add(configLoadedHandler(this, _runtimeConfig));
                }
            }

            bool[] results = await Task.WhenAll(configLoadedTasks);

            IsLateConfigured = true;

            // Verify that all tasks succeeded.
            return results.All(r => r);
        }

        return false;
    }
}
