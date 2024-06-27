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
using Microsoft.IdentityModel.Tokens;
using Npgsql;

[assembly: InternalsVisibleTo("Azure.DataApiBuilder.Service.Tests")]
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
    /// <param name="dataSourceName">The data source name to be used in the loaded config.</param>
    /// <returns>True if the config was loaded, otherwise false.</returns>
    public abstract bool TryLoadKnownConfig([NotNullWhen(true)] out RuntimeConfig? config, bool replaceEnvVar = false, string dataSourceName = "");

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
    /// <param name="dataSourceName"> datasource name for which to add connection string</param>
    /// <param name="datasourceNameToConnectionString"> dictionary of datasource name to connection string</param>
    public static bool TryParseConfig(string json,
        [NotNullWhen(true)] out RuntimeConfig? config,
        ILogger? logger = null,
        string? connectionString = null,
        bool replaceEnvVar = false,
        string dataSourceName = "",
        Dictionary<string, string>? datasourceNameToConnectionString = null,
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

            // set dataSourceName to default if not provided
            if (string.IsNullOrEmpty(dataSourceName))
            {
                dataSourceName = config.DefaultDataSourceName;
            }

            if (!string.IsNullOrEmpty(connectionString))
            {
                // update connection string if provided.
                updatedConnectionString = connectionString;
            }

            if (datasourceNameToConnectionString is null)
            {
                datasourceNameToConnectionString = new Dictionary<string, string>();
            }

            // add to dictionary if datasourceName is present (will either be the default or the one provided)
            datasourceNameToConnectionString.TryAdd(dataSourceName, updatedConnectionString);

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
                config.UpdateDataSourceNameToDataSource(dataSourceName, ds);

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
        options.Converters.Add(new RestRuntimeOptionsConverterFactory());
        options.Converters.Add(new GraphQLRuntimeOptionsConverterFactory(replaceEnvVar));
        options.Converters.Add(new EntitySourceConverterFactory(replaceEnvVar));
        options.Converters.Add(new EntityGraphQLOptionsConverterFactory(replaceEnvVar));
        options.Converters.Add(new EntityRestOptionsConverterFactory(replaceEnvVar));
        options.Converters.Add(new EntityActionConverterFactory());
        options.Converters.Add(new DataSourceFilesConverter());
        options.Converters.Add(new EntityCacheOptionsConverterFactory());
        options.Converters.Add(new MultipleCreateOptionsConverter());
        options.Converters.Add(new MultipleMutationOptionsConverter(options));
        options.Converters.Add(new DataSourceConverterFactory(replaceEnvVar));
        options.Converters.Add(new HostOptionsConvertorFactory());

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
        if (connectionStringBuilder.ApplicationName.IsNullOrEmpty())
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
}
