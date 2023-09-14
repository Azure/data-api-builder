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
    /// <param name="dataSourceName"> datasource name for which to add connection string</param>
    /// <param name="datasourceNameToConnectionString"> dictionary of datasource name to connection string</param>
    public static bool TryParseConfig(string json,
        [NotNullWhen(true)] out RuntimeConfig? config,
        ILogger? logger = null,
        string? connectionString = null,
        bool replaceEnvVar = false,
        string dataSourceName = "",
        Dictionary<string, string>? datasourceNameToConnectionString = null)
    {
        JsonSerializerOptions options = GetSerializationOptions(replaceEnvVar);

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
                dataSourceName = config.GetDefaultDataSourceName();
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

                // Add Application Name for telemetry for MsSQL
                if (ds.DatabaseType is DatabaseType.MSSQL && replaceEnvVar)
                {
                    updatedConnection = GetConnectionStringWithApplicationName(connectionValue);
                }

                ds = ds with { ConnectionString = updatedConnection };
                config.UpdateDataSourceNameToDataSource(dataSourceName, ds);

                if (string.Equals(dataSourceKey, config.GetDefaultDataSourceName(), StringComparison.OrdinalIgnoreCase))
                {
                    config = config with { DataSource = ds };
                }

            }

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
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
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

        // Get the application name using ProductInfo.GetDataApiBuilderUserAgent().
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
}
