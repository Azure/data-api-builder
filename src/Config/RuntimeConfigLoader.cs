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
using Azure.DataApiBuilder.Config.Telemetry;
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

    protected static LogBuffer _logBuffer = new();

    /// <summary>
    /// Logger used to drain buffered logs. <c>null</c> on a base loader with no logging; loaders that
    /// own a logger (e.g. <see cref="FileSystemRuntimeConfigLoader"/>) override this so
    /// <see cref="FlushLogBuffer"/> can emit to it.
    /// </summary>
    protected virtual ILogger? Logger => null;

    /// <summary>
    /// Flushes any logs buffered during config parsing / telemetry embedding (notably the telemetry
    /// Application Name Debug log) to <see cref="Logger"/>. Safe no-op when no logger is available (the
    /// buffered logs remain until a later flush), so it cannot lose logs or regress flush behavior.
    /// </summary>
    public void FlushLogBuffer()
    {
        if (Logger is not null)
        {
            _logBuffer.FlushToLogger(Logger);
        }
    }

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
    /// Extracts AzureKeyVaultOptions from JSON string with configurable variable replacement.
    /// </summary>
    /// <param name="json">JSON that represents the config file.</param>
    /// <param name="enableEnvReplacement">Whether to enable environment variable replacement during extraction.</param>
    /// <param name="replacementFailureMode">Failure mode for environment variable replacement if enabled.</param>
    /// <returns>AzureKeyVaultOptions if present, null otherwise.</returns>
    private static AzureKeyVaultOptions? ExtractAzureKeyVaultOptions(
        string json,
        bool enableEnvReplacement,
        EnvironmentVariableReplacementFailureMode replacementFailureMode = EnvironmentVariableReplacementFailureMode.Throw)
    {
        JsonSerializerOptions options = new()
        {
            PropertyNameCaseInsensitive = false,
            PropertyNamingPolicy = new HyphenatedNamingPolicy(),
            ReadCommentHandling = JsonCommentHandling.Skip
        };
        DeserializationVariableReplacementSettings envOnlySettings = new(
            azureKeyVaultOptions: null,
            doReplaceEnvVar: enableEnvReplacement,
            doReplaceAkvVar: false,
            envFailureMode: replacementFailureMode);
        options.Converters.Add(new StringJsonConverterFactory(envOnlySettings));
        options.Converters.Add(new EnumMemberJsonEnumConverterFactory());
        options.Converters.Add(new AzureKeyVaultOptionsConverterFactory(replacementSettings: envOnlySettings));
        options.Converters.Add(new AKVRetryPolicyOptionsConverterFactory(replacementSettings: envOnlySettings));

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("azure-key-vault", out JsonElement akvElement))
            {
                return JsonSerializer.Deserialize<AzureKeyVaultOptions>(akvElement.GetRawText(), options);
            }
        }
        catch
        {
            // If we can't extract AKV options, return null and proceed without AKV variable replacement
            return null;
        }

        return null;
    }

    /// <summary>
    /// Parses a JSON string into a <c>RuntimeConfig</c> object for single database scenario.
    /// </summary>
    /// <param name="json">JSON that represents the config file.</param>
    /// <param name="config">The parsed config, or null if it parsed unsuccessfully.</param>
    /// <param name="parseError">A clean error message when parsing fails, or null on success.</param>
    /// <param name="replacementSettings">Settings for variable replacement during deserialization. If null, no variable replacement will be performed.</param>
    /// <param name="connectionString">connectionString to add to config if specified</param>
    /// <returns>True if the config was parsed, otherwise false.</returns>
    public static bool TryParseConfig(string json,
        [NotNullWhen(true)] out RuntimeConfig? config,
        DeserializationVariableReplacementSettings? replacementSettings = null,
        string? connectionString = null)
    {
        return TryParseConfig(json, out config, out _, replacementSettings, connectionString);
    }

    /// <summary>
    /// Parses a JSON string into a <c>RuntimeConfig</c> object for single database scenario.
    /// </summary>
    /// <param name="json">JSON that represents the config file.</param>
    /// <param name="config">The parsed config, or null if it parsed unsuccessfully.</param>
    /// <param name="parseError">A clean error message when parsing fails, or null on success.</param>
    /// <param name="replacementSettings">Settings for variable replacement during deserialization. If null, no variable replacement will be performed.</param>
    /// <param name="connectionString">connectionString to add to config if specified</param>
    /// <returns>True if the config was parsed, otherwise false.</returns>
    public static bool TryParseConfig(string json,
        [NotNullWhen(true)] out RuntimeConfig? config,
        out string? parseError,
        DeserializationVariableReplacementSettings? replacementSettings = null,
        string? connectionString = null)
    {
        parseError = null;
        // First pass: extract AzureKeyVault options if AKV replacement is requested
        if (replacementSettings?.DoReplaceAkvVar is true)
        {
            AzureKeyVaultOptions? azureKeyVaultOptions = ExtractAzureKeyVaultOptions(
                json: json,
                enableEnvReplacement: replacementSettings.DoReplaceEnvVar,
                replacementFailureMode: replacementSettings.EnvFailureMode);

            // Update replacement settings with the extracted AKV options
            if (azureKeyVaultOptions is not null)
            {
                replacementSettings = new DeserializationVariableReplacementSettings(
                    azureKeyVaultOptions: azureKeyVaultOptions,
                    doReplaceEnvVar: replacementSettings.DoReplaceEnvVar,
                    doReplaceAkvVar: replacementSettings.DoReplaceAkvVar,
                    envFailureMode: replacementSettings.EnvFailureMode)
                {
                    // Preserve the child-config skip flag across this AKV-driven rebuild so nested
                    // configs still defer Application Name injection to the top-level load.
                    SkipApplicationNameInjection = replacementSettings.SkipApplicationNameInjection
                };
            }
        }

        JsonSerializerOptions options = GetSerializationOptions(replacementSettings);

        try
        {
            config = JsonSerializer.Deserialize<RuntimeConfig>(json, options);

            if (config is null)
            {
                return false;
            }

            // Embed the DAB Application Name (with anonymous usage telemetry) into the connection
            // string of every MSSQL / DWSQL / PostgreSQL data source.
            //
            // We iterate the fully-merged data-source map and pass the merged `config`, so that in a
            // multi-database setup each data source reflects the GLOBAL runtime settings and the
            // COMPLETE (merged) entity set rather than its own partial child config. Child configs skip
            // this step during their own parse (SkipApplicationNameInjection); the top-level load runs
            // it once here, after the merge, so every connection pool carries a self-contained snapshot
            // of the deployment.
            //
            // The explicit connection-string override (the `connectionString` parameter), when present,
            // applies only to the default data source.
            // The explicit connection-string override is applied to the default data source regardless of
            // env-var replacement, while telemetry embedding is gated on DoReplaceEnvVar (and skipped for
            // nested child configs, which defer injection to the top-level load).
            bool embedTelemetry = replacementSettings?.DoReplaceEnvVar == true && replacementSettings?.SkipApplicationNameInjection != true;
            bool hasConnectionStringOverride = !string.IsNullOrEmpty(connectionString);

            if (embedTelemetry || hasConnectionStringOverride)
            {
                foreach ((string dataSourceName, DataSource dataSource) in config.GetDataSourceNamesToDataSourcesIterator().ToList())
                {
                    bool isDefaultDataSource = string.Equals(dataSourceName, config.DefaultDataSourceName, StringComparison.OrdinalIgnoreCase);

                    // The override applies only to the default data source; others keep their own value.
                    bool applyOverrideHere = isDefaultDataSource && hasConnectionStringOverride;

                    // Nothing to do for a non-default data source when we're not embedding telemetry.
                    if (!embedTelemetry && !applyOverrideHere)
                    {
                        continue;
                    }

                    string baseConnectionString = applyOverrideHere ? connectionString! : dataSource.ConnectionString;

                    string updatedConnectionString = embedTelemetry
                        ? GetConnectionStringWithApplicationName(baseConnectionString, config, dataSource)
                        : baseConnectionString;

                    DataSource updatedDataSource = dataSource with { ConnectionString = updatedConnectionString };
                    config.UpdateDataSourceNameToDataSource(dataSourceName, updatedDataSource);

                    if (isDefaultDataSource)
                    {
                        config = config with { DataSource = updatedDataSource };
                    }
                }
            }
        }
        catch (Exception ex) when (
            ex is JsonException ||
            ex is DataApiBuilderException)
        {
            parseError = ex is DataApiBuilderException
                ? ex.Message
                : $"Deserialization of the configuration file failed. {ex.Message}";

            config = null;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Get Serializer options for the config file.
    /// </summary>
    /// <param name="replacementSettings">Settings for variable replacement during deserialization.
    /// If null, no variable replacement will be performed.</param>
    public static JsonSerializerOptions GetSerializationOptions(
        DeserializationVariableReplacementSettings? replacementSettings = null)
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
        options.Converters.Add(new RuntimeHealthOptionsConvertorFactory(replacementSettings));
        options.Converters.Add(new DataSourceHealthOptionsConvertorFactory(replacementSettings));
        options.Converters.Add(new EntityHealthOptionsConvertorFactory());
        options.Converters.Add(new RestRuntimeOptionsConverterFactory());
        options.Converters.Add(new GraphQLRuntimeOptionsConverterFactory(replacementSettings));
        options.Converters.Add(new McpRuntimeOptionsConverterFactory(replacementSettings));
        options.Converters.Add(new DmlToolsConfigConverter());
        options.Converters.Add(new EntitySourceConverterFactory(replacementSettings));
        options.Converters.Add(new EntityGraphQLOptionsConverterFactory(replacementSettings));
        options.Converters.Add(new EntityRestOptionsConverterFactory(replacementSettings));
        options.Converters.Add(new EntityActionConverterFactory());
        options.Converters.Add(new DataSourceFilesConverter());
        options.Converters.Add(new EntityCacheOptionsConverterFactory(replacementSettings));
        options.Converters.Add(new AutoentityConverter(replacementSettings));
        options.Converters.Add(new AutoentityPatternsConverter(replacementSettings));
        options.Converters.Add(new AutoentityTemplateConverter(replacementSettings));
        options.Converters.Add(new EntityMcpOptionsConverterFactory());
        options.Converters.Add(new RuntimeCacheOptionsConverterFactory());
        options.Converters.Add(new RuntimeCacheLevel2OptionsConverterFactory());
        options.Converters.Add(new CompressionOptionsConverterFactory());
        options.Converters.Add(new MultipleCreateOptionsConverter());
        options.Converters.Add(new MultipleMutationOptionsConverter(options));
        options.Converters.Add(new DataSourceConverterFactory(replacementSettings));
        options.Converters.Add(new HostOptionsConvertorFactory());
        options.Converters.Add(new AKVRetryPolicyOptionsConverterFactory(replacementSettings));
        options.Converters.Add(new AzureLogAnalyticsOptionsConverterFactory(replacementSettings));
        options.Converters.Add(new AzureLogAnalyticsAuthOptionsConverter(replacementSettings));
        options.Converters.Add(new BoolJsonConverter());
        options.Converters.Add(new FileSinkConverter(replacementSettings));

        // Add AzureKeyVaultOptionsConverterFactory to ensure AKV config is deserialized properly
        options.Converters.Add(new AzureKeyVaultOptionsConverterFactory(replacementSettings));

        // Add EmbeddingsOptionsConverterFactory to handle embeddings configuration
        options.Converters.Add(new EmbeddingsOptionsConverterFactory(replacementSettings));
        options.Converters.Add(new EmbeddingsCacheOptionsConverterFactory());

        // Only add the extensible string converter if we have replacement settings
        if (replacementSettings is not null)
        {
            options.Converters.Add(new StringJsonConverterFactory(replacementSettings));
        }

        return options;
    }

    /// <summary>
    /// Embeds the DAB <c>Application Name</c> (with anonymous usage telemetry) into the connection
    /// string for the given data source, dispatching to the engine-specific implementation. Engines
    /// that do not support telemetry (e.g. MySQL) return the connection string unchanged.
    /// </summary>
    /// <param name="connectionString">Connection string for connecting to the database.</param>
    /// <param name="config">The fully-resolved runtime config used to compute the telemetry payload.</param>
    /// <param name="dataSource">The data source whose connection is being opened (selects the engine and per-pool fields).</param>
    /// <returns>The connection string with the telemetry-bearing <c>Application Name</c> embedded.</returns>
    public static string GetConnectionStringWithApplicationName(string connectionString, RuntimeConfig config, DataSource dataSource)
    {
        return dataSource.DatabaseType switch
        {
            DatabaseType.MSSQL or DatabaseType.DWSQL => GetMsSqlConnectionStringWithApplicationName(connectionString, config, dataSource),
            DatabaseType.PostgreSQL => GetPgSqlConnectionStringWithApplicationName(connectionString, config, dataSource),
            _ => connectionString,
        };
    }

    /// <summary>
    /// It adds or replaces a property in the connection string with `Application Name` property.
    /// If the connection string already contains the property, it appends the property `Application Name` to the connection string,
    /// else add the Application Name property with DataApiBuilder Application Name based on hosted/oss platform.
    /// </summary>
    /// <param name="connectionString">Connection string for connecting to database.</param>
    /// <param name="config">When provided, anonymous DAB telemetry is embedded into the `Application Name`
    /// (honoring the `DAB_TELEMETRY_APPNAME_OPT_OUT` opt-out). When null, only the plain user agent is used.</param>
    /// <param name="liveDataSource">The data source whose connection is being opened, used to encode per-pool
    /// fields (Source, OBO). Ignored when <paramref name="config"/> is null.</param>
    /// <returns>Updated connection string with `Application Name` property.</returns>
    internal static string GetMsSqlConnectionStringWithApplicationName(string connectionString, RuntimeConfig? config = null, DataSource? liveDataSource = null)
    {
        // If the connection string is null, empty, or whitespace, return it as is.
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

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

        // Idempotency guard: if DAB telemetry was already embedded into the Application Name (e.g. by
        // the loader's post-processing), do not append it again — that would duplicate the payload.
        if (connectionStringBuilder.ApplicationName?.Contains(ProductInfo.DAB_USER_AGENT_MARKER, StringComparison.Ordinal) == true)
        {
            return connectionString;
        }

        // When the full runtime config is available, embed anonymous DAB telemetry into the
        // Application Name (honoring the opt-out switch). Otherwise fall back to the plain user agent.
        string applicationName = config is null
            ? ProductInfo.GetDataApiBuilderUserAgent()
            : ApplicationNameTelemetry.BuildApplicationNameSegment(config, liveDataSource);

        if (config is not null)
        {
            // Emit the telemetry-bearing Application Name (never the full connection string, which can
            // contain secrets) at Debug, once per pool, as required by the telemetry design.
            _logBuffer.BufferLog(LogLevel.Debug, $"DAB telemetry Application Name computed for '{liveDataSource?.DatabaseType}' data source: {applicationName}");
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
    /// <param name="config">When provided, anonymous DAB usage telemetry is embedded in the Application Name (honoring the opt-out switch); otherwise the plain user agent is used.</param>
    /// <param name="liveDataSource">The data source whose connection is being opened, used to encode per-pool
    /// fields (Source, OBO). Ignored when <paramref name="config"/> is null.</param>
    /// <returns>Updated connection string with `Application Name` property.</returns>
    internal static string GetPgSqlConnectionStringWithApplicationName(string connectionString, RuntimeConfig? config = null, DataSource? liveDataSource = null)
    {
        // If the connection string is null, empty, or whitespace, return it as is.
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

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

        // Idempotency guard: if DAB telemetry was already embedded into the Application Name (e.g. by
        // the loader's post-processing), do not append it again — that would duplicate the payload.
        if (connectionStringBuilder.ApplicationName?.Contains(ProductInfo.DAB_USER_AGENT_MARKER, StringComparison.Ordinal) == true)
        {
            return connectionString;
        }

        // When the full runtime config is available, embed anonymous DAB telemetry into the
        // Application Name (honoring the opt-out switch). Otherwise fall back to the plain user agent.
        string applicationName = config is null
            ? ProductInfo.GetDataApiBuilderUserAgent()
            : ApplicationNameTelemetry.BuildApplicationNameSegment(config, liveDataSource);

        if (config is not null)
        {
            // Emit the telemetry-bearing Application Name (never the full connection string, which can
            // contain secrets) at Debug, once per pool, as required by the telemetry design.
            _logBuffer.BufferLog(LogLevel.Debug, $"DAB telemetry Application Name computed for '{liveDataSource?.DatabaseType}' data source: {applicationName}");
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

    public void EditRuntimeConfig(RuntimeConfig newRuntimeConfig)
    {
        RuntimeConfig = newRuntimeConfig;
    }
}
