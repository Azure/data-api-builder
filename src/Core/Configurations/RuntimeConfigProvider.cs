// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
using System.Net;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.Converters;
using Azure.DataApiBuilder.Config.NamingPolicies;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Azure.DataApiBuilder.Core.Configurations;

/// <summary>
/// This class is responsible for exposing the runtime config to the rest of the service.
/// The <c>RuntimeConfigProvider</c> won't directly load the config, but will instead rely on the <see cref="FileSystemRuntimeConfigLoader"/> to do so.
/// </summary>
/// <remarks>
/// The <c>RuntimeConfigProvider</c> will maintain internal state of the config, and will only load it once.
///
/// This class should be treated as the owner of the config that is available within the service, and other classes
/// should not load the config directly, or maintain a reference to it, so that we can do hot-reloading by replacing
/// the config that is available from this type.
/// </remarks>
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
    /// The access tokens representing a Managed Identity to connect to the database.
    /// The key is the unique datasource name and the value is the access token.
    /// </summary>
    public Dictionary<string, string?> ManagedIdentityAccessToken { get; private set; } = new Dictionary<string, string?>();

    private RuntimeConfigLoader _configLoader;
    private DabChangeToken _changeToken = new();
    private readonly IDisposable _changeTokenRegistration;

    public RuntimeConfigProvider(RuntimeConfigLoader runtimeConfigLoader)
    {
        _configLoader = runtimeConfigLoader;
        _changeTokenRegistration = ChangeToken.OnChange(_configLoader.GetChangeToken, RaiseChanged);
    }

    /// <summary>
    /// Swaps out the old change token with a new change token and
    /// signals that a change has occurred.
    /// </summary>
    /// <seealso cref="https://github.com/dotnet/runtime/blob/main/src/libraries/Microsoft.Extensions.Configuration/src/ConfigurationProvider.cs">
    /// Example usage of Interlocked.Exchange(...) to refresh change token.</seealso>
    /// <seealso cref="https://learn.microsoft.com/en-us/dotnet/api/system.threading.interlocked.exchange">
    /// Sets a variable to a specified value as an atomic operation.
    /// </seealso>
    private void RaiseChanged()
    {
        //First use of GetConfig during hot reload, in order to do validation of
        //config file before any changes are made for hot reload.
        //In case validation fails, an exception will be thrown and hot reload will be canceled.
        ValidateConfig();

        DabChangeToken previousToken = Interlocked.Exchange(ref _changeToken, new DabChangeToken());
        previousToken.SignalChange();
    }

    /// <summary>
    /// Change token producer which returns an uncancelled/unsignalled change token.
    /// </summary>
    /// <returns>DabChangeToken</returns>
#pragma warning disable CA1024 // Use properties where appropriate
    public IChangeToken GetChangeToken()
#pragma warning restore CA1024 // Use properties where appropriate
    {
        return _changeToken;
    }

    /// <summary>
    /// Removes all change registration subscriptions.
    /// </summary>
    public void Dispose()
    {
        _changeTokenRegistration.Dispose();
    }

    /// <summary>
    /// Accessor for the ConfigFilePath to avoid exposing the loader. If we are not
    /// loading from the file system, we return empty string.
    /// </summary>
    public string ConfigFilePath
    {
        get
        {
            if (_configLoader is FileSystemRuntimeConfigLoader)
            {
                return ((FileSystemRuntimeConfigLoader)_configLoader).ConfigFilePath;
            }

            return string.Empty;
        }
    }

    /// <summary>
    /// Return the previous loaded config, or it will attempt to load the config that
    /// is known by the loader.
    /// </summary>
    /// <returns>The RuntimeConfig instance.</returns>
    /// <remark>Dont use this method if environment variable references need to be retained.</remark>
    /// <exception cref="DataApiBuilderException">Thrown when the loader is unable to load an instance of the config from its known location.</exception>
    public RuntimeConfig GetConfig()
    {
        if (_configLoader.RuntimeConfig is not null)
        {
            return _configLoader.RuntimeConfig;
        }

        // While loading the config file, replace all the environment variables with their values.
        if (!_configLoader.TryLoadKnownConfig(out RuntimeConfig? runtimeConfig, replaceEnvVar: true))
        {
            throw new DataApiBuilderException(
                message: "Runtime config isn't setup.",
                statusCode: HttpStatusCode.ServiceUnavailable,
                subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
        }

        return runtimeConfig;
    }

    /// <summary>
    /// Attempt to acquire runtime configuration metadata.
    /// </summary>
    /// <param name="runtimeConfig">Populated runtime configuration, if present.</param>
    /// <returns>True when runtime config is provided, otherwise false.</returns>
    public bool TryGetConfig([NotNullWhen(true)] out RuntimeConfig? runtimeConfig)
    {
        RuntimeConfig? config = _configLoader.RuntimeConfig;
        if (config is null)
        {
            _configLoader.TryLoadKnownConfig(out config, replaceEnvVar: true);
        }

        runtimeConfig = config;
        return config is not null;
    }

    /// <summary>
    /// Attempt to acquire runtime configuration metadata from a previously loaded one.
    /// This method will not load the config if it hasn't been loaded yet.
    /// </summary>
    /// <param name="runtimeConfig">Populated runtime configuration, if present.</param>
    /// <returns>True when runtime config is provided, otherwise false.</returns>
    public bool TryGetLoadedConfig([NotNullWhen(true)] out RuntimeConfig? runtimeConfig)
    {
        runtimeConfig = _configLoader.RuntimeConfig;
        return _configLoader.RuntimeConfig is not null;
    }

    /// <summary>
    /// Initialize the runtime configuration provider with the specified configurations.
    /// This initialization method is used when the configuration is sent to the ConfigurationController
    /// in the form of a string instead of reading the configuration from a configuration file.
    /// This method assumes the connection string is provided as part of the configuration.
    /// Initialize the first database within the datasource list.
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
                out RuntimeConfig? runtimeConfig,
                replaceEnvVar: false,
                replacementFailureMode: EnvironmentVariableReplacementFailureMode.Ignore))
        {
            _configLoader.RuntimeConfig = runtimeConfig;

            if (string.IsNullOrEmpty(runtimeConfig.DataSource.ConnectionString))
            {
                throw new ArgumentException($"'{nameof(runtimeConfig.DataSource.ConnectionString)}' cannot be null or empty.", nameof(runtimeConfig.DataSource.ConnectionString));
            }

            if (runtimeConfig.DataSource.DatabaseType == DatabaseType.CosmosDB_NoSQL)
            {
                _configLoader.RuntimeConfig = HandleCosmosNoSqlConfiguration(schema, runtimeConfig, runtimeConfig.DataSource.ConnectionString);
            }

            ManagedIdentityAccessToken[_configLoader.RuntimeConfig.DefaultDataSourceName] = accessToken;
        }

        bool configLoadSucceeded = await InvokeConfigLoadedHandlersAsync();

        IsLateConfigured = true;

        return configLoadSucceeded;
    }

    /// <summary>
    /// Set the runtime configuration provider with the specified accessToken for the specified datasource.
    /// This initialization method is used to set the access token for the current runtimeConfig.
    /// As opposed to using a json input and regenerating the runtimconfig, it sets the access token for the current runtimeConfig on the provider.
    /// </summary>
    /// <param name="accessToken">The string representation of a managed identity access token</param>
    /// <param name="dataSourceName">Name of the datasource for which to assign the token.</param>
    /// <returns>true if the initialization succeeded, false otherwise.</returns>
    public bool TrySetAccesstoken(
        string? accessToken,
        string dataSourceName)
    {
        if (_configLoader.RuntimeConfig is null)
        {
            // if runtimeConfig is not set up, throw as cannot initialize.
            throw new DataApiBuilderException($"{nameof(RuntimeConfig)} has not been loaded.", HttpStatusCode.BadRequest, DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
        }

        // Validate that the datasource exists in the runtimeConfig and then add or update access token.
        if (_configLoader.RuntimeConfig.CheckDataSourceExists(dataSourceName))
        {
            ManagedIdentityAccessToken[dataSourceName] = accessToken;
            return true;
        }

        return false;
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
    public async Task<bool> Initialize(
        string jsonConfig,
        string? graphQLSchema,
        string connectionString,
        string? accessToken,
        bool replaceEnvVar = true,
        EnvironmentVariableReplacementFailureMode replacementFailureMode = EnvironmentVariableReplacementFailureMode.Throw)
    {
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new ArgumentException($"'{nameof(connectionString)}' cannot be null or empty.", nameof(connectionString));
        }

        if (string.IsNullOrEmpty(jsonConfig))
        {
            throw new ArgumentException($"'{nameof(jsonConfig)}' cannot be null or empty.", nameof(jsonConfig));
        }

        IsLateConfigured = true;

        if (RuntimeConfigLoader.TryParseConfig(jsonConfig, out RuntimeConfig? runtimeConfig, replaceEnvVar: replaceEnvVar, replacementFailureMode: replacementFailureMode))
        {
            _configLoader.RuntimeConfig = runtimeConfig.DataSource.DatabaseType switch
            {
                DatabaseType.CosmosDB_NoSQL => HandleCosmosNoSqlConfiguration(graphQLSchema, runtimeConfig, connectionString),
                _ => runtimeConfig with { DataSource = runtimeConfig.DataSource with { ConnectionString = connectionString } }
            };
            ManagedIdentityAccessToken[_configLoader.RuntimeConfig.DefaultDataSourceName] = accessToken;
            _configLoader.RuntimeConfig.UpdateDataSourceNameToDataSource(_configLoader.RuntimeConfig.DefaultDataSourceName, _configLoader.RuntimeConfig.DataSource);

            return await InvokeConfigLoadedHandlersAsync();
        }

        return false;
    }

    /// <summary>
    /// Runtimeconfig is hot-reloadable when the configuration is not in production mode and not late configured.
    /// </summary>
    /// <returns>True when config is hot-reloadable.</returns>
    public bool IsConfigHotReloadable()
    {
        return !IsLateConfigured || !(_configLoader.RuntimeConfig?.Runtime?.Host?.Mode == HostMode.Production);
    }

    /// <summary>
    /// This function checks if there is a new config that needs to be validated
    /// and validates the configuration file as well as the schema file, in the
    /// case that it is not able to validate both then it will return an error.
    /// </summary>
    /// <returns></returns>
    public void ValidateConfig()
    {
        // Only used in hot reload to validate the configuration file
        if (_configLoader.DoesConfigNeedValidation())
        {
            Console.WriteLine("Validating hot-reloaded configuration file.");
            IFileSystem fileSystem = new FileSystem();
            ILoggerFactory loggerFactory = new LoggerFactory();
            ILogger<RuntimeConfigValidator> logger = loggerFactory.CreateLogger<RuntimeConfigValidator>();
            RuntimeConfigValidator runtimeConfigValidator = new(this, fileSystem, logger, true);

            _configLoader.IsNewConfigValidated = runtimeConfigValidator.TryValidateConfig(ConfigFilePath, loggerFactory).Result;

            // Saves the lastValidRuntimeConfig as the new RuntimeConfig if it is validated for hot reload
            if (_configLoader.IsNewConfigValidated)
            {
                _configLoader.SetLkgConfig();
            }
            else
            {
                _configLoader.RestoreLkgConfig();

                throw new DataApiBuilderException(
                    message: "Failed validation of configuration file.",
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

            Console.WriteLine("Validated hot-reloaded configuration file.");
        }
    }

    private async Task<bool> InvokeConfigLoadedHandlersAsync()
    {
        List<Task<bool>> configLoadedTasks = new();
        if (_configLoader.RuntimeConfig is not null)
        {
            foreach (RuntimeConfigLoadedHandler configLoadedHandler in RuntimeConfigLoadedHandlers)
            {
                configLoadedTasks.Add(configLoadedHandler(this, _configLoader.RuntimeConfig));
            }
        }

        bool[] results = await Task.WhenAll(configLoadedTasks);

        // Verify that all tasks succeeded.
        return results.All(x => x);
    }

    private static RuntimeConfig HandleCosmosNoSqlConfiguration(string? schema, RuntimeConfig runtimeConfig, string connectionString, string dataSourceName = "")
    {
        if (string.IsNullOrEmpty(dataSourceName))
        {
            dataSourceName = runtimeConfig.DefaultDataSourceName;
        }

        DbConnectionStringBuilder dbConnectionStringBuilder = new()
        {
            ConnectionString = connectionString
        };

        if (string.IsNullOrEmpty(schema))
        {
            throw new ArgumentException($"'{nameof(schema)}' cannot be null or empty.", nameof(schema));
        }

        HyphenatedNamingPolicy namingPolicy = new();

        DataSource dataSource = runtimeConfig.GetDataSourceFromDataSourceName(dataSourceName);

        Dictionary<string, object?> options;
        if (dataSource.Options is not null)
        {
            options = new(dataSource.Options)
            {
                // push the "raw" GraphQL schema into the options to pull out later when requested
                { namingPolicy.ConvertName(nameof(CosmosDbNoSQLDataSourceOptions.GraphQLSchema)), schema }
            };
        }
        else
        {
            throw new ArgumentException($"'{nameof(CosmosDbNoSQLDataSourceOptions)}' cannot be null or empty.", nameof(CosmosDbNoSQLDataSourceOptions));
        }

        // SWA may provide CosmosDB database name in connectionString
        string? database = dbConnectionStringBuilder.ContainsKey("Database") ? (string)dbConnectionStringBuilder["Database"] : null;

        if (database is not null)
        {
            // Add or update the options to contain the parsed database
            options[namingPolicy.ConvertName(nameof(CosmosDbNoSQLDataSourceOptions.Database))] = database;
        }

        // Update the connection string in the datasource with the one that was provided to the controller
        dataSource = dataSource with { Options = options, ConnectionString = connectionString };

        if (dataSourceName == runtimeConfig.DefaultDataSourceName)
        {
            // update default db.
            runtimeConfig = runtimeConfig with { DataSource = dataSource };
        }

        // update dictionary
        runtimeConfig.UpdateDataSourceNameToDataSource(dataSourceName, dataSource);

        return runtimeConfig;
    }
}
