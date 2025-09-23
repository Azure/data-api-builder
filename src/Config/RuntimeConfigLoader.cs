// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.Converters;
using Azure.DataApiBuilder.Config.NamingPolicies;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Product;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Npgsql;
using static Azure.DataApiBuilder.Config.DabConfigEvents;

[assembly: InternalsVisibleTo("Azure.DataApiBuilder.Service.Tests")]
namespace Azure.DataApiBuilder.Config;

public abstract class RuntimeConfigLoader
{
    private DabChangeToken _changeToken;
    private HotReloadEventHandler<HotReloadEventArgs>? _handler;
    protected readonly string? _connectionString;

    // Public to allow the RuntimeProvider and other users of class to set via out param.
    // May be candidate to refactor by changing all of the Parse/Load functions to save
    // state in place of using out params.
    public RuntimeConfig? RuntimeConfig;

    public RuntimeConfig? LastValidRuntimeConfig;

    public bool IsNewConfigDetected;

    public bool IsNewConfigValidated;

    public RuntimeConfigLoader(HotReloadEventHandler<HotReloadEventArgs>? handler = null, string? connectionString = null)
    {
        _changeToken = new DabChangeToken();
        _handler = handler;
        _connectionString = connectionString;
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
        DabChangeToken previousToken = Interlocked.Exchange(ref _changeToken, new DabChangeToken());
        previousToken.SignalChange();
    }

    protected virtual void OnConfigChangedEvent(HotReloadEventArgs args)
    {
        _handler?.OnConfigChangedEvent(this, args);
    }

    /// <summary>
    /// Notifies event handler and change token subscribers that a hot-reload has occurred.
    /// Order here matters because some dependencies must be updated before others.
    /// When modifying this function:
    /// - Ensure that you add new event trigger(s) after any required dependencies have
    /// been refreshed by previously called event triggers.
    /// </summary>
    /// <param name="message"></param>
    protected void SignalConfigChanged(string message = "")
    {
        // Signal that a change has occurred to all change token listeners.
        RaiseChanged();

        // All the data inside of the if statement should only update when DAB is in development mode.
        if (RuntimeConfig!.IsDevelopmentMode())
        {
            OnConfigChangedEvent(new HotReloadEventArgs(QUERY_MANAGER_FACTORY_ON_CONFIG_CHANGED, message));
            OnConfigChangedEvent(new HotReloadEventArgs(METADATA_PROVIDER_FACTORY_ON_CONFIG_CHANGED, message));
            OnConfigChangedEvent(new HotReloadEventArgs(QUERY_ENGINE_FACTORY_ON_CONFIG_CHANGED, message));
            OnConfigChangedEvent(new HotReloadEventArgs(MUTATION_ENGINE_FACTORY_ON_CONFIG_CHANGED, message));
            OnConfigChangedEvent(new HotReloadEventArgs(DOCUMENTOR_ON_CONFIG_CHANGED, message));

            // Order of event firing matters: Authorization rules can only be updated after the
            // MetadataProviderFactory has been updated with latest database object metadata.
            // RuntimeConfig must already be updated and is implied to have been updated by the time
            // this function is called.
            OnConfigChangedEvent(new HotReloadEventArgs(AUTHZ_RESOLVER_ON_CONFIG_CHANGED, message));

            // Order of event firing matters: Eviction must be done before creating a new schema and then updating the schema.
            OnConfigChangedEvent(new HotReloadEventArgs(GRAPHQL_SCHEMA_EVICTION_ON_CONFIG_CHANGED, message));
            OnConfigChangedEvent(new HotReloadEventArgs(GRAPHQL_SCHEMA_CREATOR_ON_CONFIG_CHANGED, message));
            OnConfigChangedEvent(new HotReloadEventArgs(GRAPHQL_SCHEMA_REFRESH_ON_CONFIG_CHANGED, message));
        }

        // Log Level Initializer is outside of if statement as it can be updated on both development and production mode.
        OnConfigChangedEvent(new HotReloadEventArgs(LOG_LEVEL_INITIALIZER_ON_CONFIG_CHANGE, message));
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
    /// Parses a JSON string into a <c>RuntimeConfig</c> object for single database scenario.
    /// </summary>
    /// <param name="json">JSON that represents the config file.</param>
    /// <param name="config">The parsed config, or null if it parsed unsuccessfully.</param>
    /// <returns>True if the config was parsed, otherwise false.</returns>
    /// <param name="logger">logger to log messages</param>
    /// <param name="connectionString">connectionString to add to config if specified</param>
    /// <param name="replaceEnvVar">Whether to replace environment variable with its
    /// value or not while deserializing. By default, no replacement happens.</param>
    /// <param name="replacementFailureMode">Determines failure mode for env variable replacement.</param>
    public static bool TryParseConfig(string json,
        [NotNullWhen(true)] out RuntimeConfig? config,
        ILogger? logger = null,
        string? connectionString = null,
        bool replaceEnvVar = false,
        EnvironmentVariableReplacementFailureMode replacementFailureMode = EnvironmentVariableReplacementFailureMode.Throw)
    {
        JsonSerializerOptions options = GetSerializationOptions(replaceEnvVar, replacementFailureMode);

        try
        {
            config = JsonSerializer.Deserialize<RuntimeConfig>(json, options);

            if (config is null)
            {
                return false;
            }

            // retreive current connection string from config
            string updatedConnectionString = config.DataSource.ConnectionString;

            if (!string.IsNullOrEmpty(connectionString))
            {
                // update connection string if provided.
                updatedConnectionString = connectionString;
            }

            Dictionary<string, string> datasourceNameToConnectionString = new();

            // add to dictionary if datasourceName is present
            datasourceNameToConnectionString.TryAdd(config.DefaultDataSourceName, updatedConnectionString);

            // iterate over dictionary and update runtime config with connection strings.
            foreach ((string dataSourceKey, string connectionValue) in datasourceNameToConnectionString)
            {
                string updatedConnection = connectionValue;

                DataSource ds = config.GetDataSourceFromDataSourceName(dataSourceKey);

                // Add Application Name for telemetry for MsSQL or PgSql
                if (ds.DatabaseType is DatabaseType.MSSQL && replaceEnvVar)
                {
                    updatedConnection = GetConnectionStringWithApplicationName(connectionValue);
                }
                else if (ds.DatabaseType is DatabaseType.PostgreSQL && replaceEnvVar)
                {
                    updatedConnection = GetPgSqlConnectionStringWithApplicationName(connectionValue);
                }

                ds = ds with { ConnectionString = updatedConnection };
                config.UpdateDataSourceNameToDataSource(config.DefaultDataSourceName, ds);

                if (string.Equals(dataSourceKey, config.DefaultDataSourceName, StringComparison.OrdinalIgnoreCase))
                {
                    config = config with { DataSource = ds };
                }
            }
        }
        catch (Exception ex) when (
            ex is JsonException ||
            ex is DataApiBuilderException)
        {
            string errorMessage = ex is JsonException ? "Deserialization of the configuration file failed." :
                "Deserialization of the configuration file failed during a post-processing step.";

            // logger can be null when called from CLI
            if (logger is null)
            {
                Console.Error.WriteLine(errorMessage + $"\n" + $"Message:\n {ex.Message}\n" + $"Stack Trace:\n {ex.StackTrace}");
            }
            else
            {
                logger.LogError(exception: ex, message: errorMessage);
            }

            config = null;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Get Serializer options for the config file.
    /// </summary>
    /// <param name="replaceEnvVar">Whether to replace environment variable with value or not while deserializing.
    /// By default, no replacement happens.</param>
    public static JsonSerializerOptions GetSerializationOptions(
        bool replaceEnvVar = false,
        EnvironmentVariableReplacementFailureMode replacementFailureMode = EnvironmentVariableReplacementFailureMode.Throw)
    {
        JsonSerializerOptions options = new()
        {
            PropertyNameCaseInsensitive = false,
            PropertyNamingPolicy = new HyphenatedNamingPolicy(),
            ReadCommentHandling = JsonCommentHandling.Skip,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        options.Converters.Add(new EnumMemberJsonEnumConverterFactory());
        options.Converters.Add(new RuntimeHealthOptionsConvertorFactory(replaceEnvVar));
        options.Converters.Add(new DataSourceHealthOptionsConvertorFactory(replaceEnvVar));
        options.Converters.Add(new EntityHealthOptionsConvertorFactory());
        options.Converters.Add(new RestRuntimeOptionsConverterFactory());
        options.Converters.Add(new GraphQLRuntimeOptionsConverterFactory(replaceEnvVar));
        options.Converters.Add(new McpRuntimeOptionsConverterFactory(replaceEnvVar));
        options.Converters.Add(new DmlToolsConfigConverter());
        options.Converters.Add(new EntitySourceConverterFactory(replaceEnvVar));
        options.Converters.Add(new EntityGraphQLOptionsConverterFactory(replaceEnvVar));
        options.Converters.Add(new EntityRestOptionsConverterFactory(replaceEnvVar));
        options.Converters.Add(new EntityActionConverterFactory());
        options.Converters.Add(new DataSourceFilesConverter());
        options.Converters.Add(new EntityCacheOptionsConverterFactory(replaceEnvVar));
        options.Converters.Add(new RuntimeCacheOptionsConverterFactory());
        options.Converters.Add(new RuntimeCacheLevel2OptionsConverterFactory());
        options.Converters.Add(new MultipleCreateOptionsConverter());
        options.Converters.Add(new MultipleMutationOptionsConverter(options));
        options.Converters.Add(new DataSourceConverterFactory(replaceEnvVar));
        options.Converters.Add(new HostOptionsConvertorFactory());
        options.Converters.Add(new AKVRetryPolicyOptionsConverterFactory(replaceEnvVar));
        options.Converters.Add(new AzureLogAnalyticsOptionsConverterFactory(replaceEnvVar));
        options.Converters.Add(new AzureLogAnalyticsAuthOptionsConverter(replaceEnvVar));
        options.Converters.Add(new FileSinkConverter(replaceEnvVar));

        if (replaceEnvVar)
        {
            options.Converters.Add(new StringJsonConverterFactory(replacementFailureMode));
        }

        return options;
    }

    /// <summary>
    /// It adds or replaces a property in the connection string with `Application Name` property.
    /// If the connection string already contains the property, it appends the property `Application Name` to the connection string,
    /// else add the Application Name property with DataApiBuilder Application Name based on hosted/oss platform.
    /// </summary>
    /// <param name="connectionString">Connection string for connecting to database.</param>
    /// <returns>Updated connection string with `Application Name` property.</returns>
    internal static string GetConnectionStringWithApplicationName(string connectionString)
    {
        // If the connection string is null, empty, or whitespace, return it as is.
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        string applicationName = ProductInfo.GetDataApiBuilderUserAgent();

        // Create a StringBuilder from the connection string.
        SqlConnectionStringBuilder connectionStringBuilder;
        try
        {
            connectionStringBuilder = new SqlConnectionStringBuilder(connectionString);
        }
        catch (Exception ex)
        {
            throw new DataApiBuilderException(
                message: DataApiBuilderException.CONNECTION_STRING_ERROR_MESSAGE,
                statusCode: HttpStatusCode.ServiceUnavailable,
                subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization,
                innerException: ex);
        }

        string defaultApplicationName = new SqlConnectionStringBuilder().ApplicationName;

        // If the connection string does not contain the `Application Name` property, add it.
        // or if the connection string contains the `Application Name` property with default SqlClient library value, replace it with
        // the DataApiBuilder Application Name.
        if (string.IsNullOrWhiteSpace(connectionStringBuilder.ApplicationName)
            || connectionStringBuilder.ApplicationName.Equals(defaultApplicationName, StringComparison.OrdinalIgnoreCase))
        {
            connectionStringBuilder.ApplicationName = applicationName;
        }
        else
        {
            // If the connection string contains the `Application Name` property with a value, update the value by adding the DataApiBuilder Application Name.
            connectionStringBuilder.ApplicationName += $",{applicationName}";
        }

        // Return the updated connection string.
        return connectionStringBuilder.ConnectionString;
    }

    /// <summary>
    /// It adds or replaces a property in the connection string with `Application Name` property.
    /// If the connection string already contains the property, it appends the property `Application Name` to the connection string,
    /// else add the Application Name property with DataApiBuilder Application Name based on hosted/oss platform.
    /// </summary>
    /// <param name="connectionString">Connection string for connecting to database.</param>
    /// <returns>Updated connection string with `Application Name` property.</returns>
    internal static string GetPgSqlConnectionStringWithApplicationName(string connectionString)
    {
        // If the connection string is null, empty, or whitespace, return it as is.
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        string applicationName = ProductInfo.GetDataApiBuilderUserAgent();

        // Create a StringBuilder from the connection string.
        NpgsqlConnectionStringBuilder connectionStringBuilder;
        try
        {
            connectionStringBuilder = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (Exception ex)
        {
            throw new DataApiBuilderException(
                message: DataApiBuilderException.CONNECTION_STRING_ERROR_MESSAGE,
                statusCode: HttpStatusCode.ServiceUnavailable,
                subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization,
                innerException: ex);
        }

        // If the connection string does not contain the `Application Name` property, add it.
        // or if the connection string contains the `Application Name` property, replace it with the DataApiBuilder Application Name.
        if (string.IsNullOrEmpty(connectionStringBuilder.ApplicationName))
        {
            connectionStringBuilder.ApplicationName = applicationName;
        }
        else
        {
            // If the connection string contains the `ApplicationName` property with a value, update the value by adding the DataApiBuilder Application Name.
            connectionStringBuilder.ApplicationName += $",{applicationName}";
        }

        // Return the updated connection string.
        return connectionStringBuilder.ConnectionString;
    }

    public bool DoesConfigNeedValidation()
    {
        if (IsNewConfigDetected && !IsNewConfigValidated)
        {
            IsNewConfigDetected = false;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Once the validation of the new config file is confirmed to have passed,
    /// this function will save the newly resolved RuntimeConfig as the new last known good,
    /// in order to have config file DAB can go into in case hot reload fails.
    /// </summary>
    public void SetLkgConfig()
    {
        IsNewConfigValidated = false;
        LastValidRuntimeConfig = RuntimeConfig;
    }

    /// <summary>
    /// Changes the state of the config file into the last known good iteration,
    /// in order to allow users to still be able to make changes in DAB even if
    /// a hot reload fails.
    /// </summary>
    public void RestoreLkgConfig()
    {
        RuntimeConfig = LastValidRuntimeConfig;
    }

    /// <summary>
    /// Uses the Last Valid Runtime Config and inserts the log-level property to the Runtime Config that will be used
    /// during the hot-reload if DAB is in Production Mode, this means that only changes to log-level will be registered.
    /// This is done in order to ensure that no unwanted changes are honored during hot-reload in Production Mode.
    /// </summary>
    public void InsertWantedChangesInProductionMode()
    {
        if (!RuntimeConfig!.IsDevelopmentMode())
        {
            // Creates copy of last valid runtime config and only adds the new logger level changes
            RuntimeConfig runtimeConfigCopy = LastValidRuntimeConfig! with
            {
                Runtime = LastValidRuntimeConfig.Runtime! with
                {
                    Telemetry = LastValidRuntimeConfig.Runtime!.Telemetry! with
                    {
                        LoggerLevel = RuntimeConfig.Runtime!.Telemetry!.LoggerLevel
                    }
                }
            };

            RuntimeConfig = runtimeConfigCopy;
        }
    }
}
