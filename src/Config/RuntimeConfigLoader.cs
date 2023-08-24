// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
    /// <returns>True if the config was loaded, otherwise false.</returns>
    public abstract bool TryLoadKnownConfig([NotNullWhen(true)] out RuntimeConfig? config);

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
    public static bool TryParseConfig(string json, [NotNullWhen(true)] out RuntimeConfig? config, ILogger? logger = null, string? connectionString = null, string? dataSourceName = null, Dictionary<string, string>? datasourceNameToConnectionString = null)
    {
        JsonSerializerOptions options = GetSerializationOptions();

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
            dataSourceName = dataSourceName ?? config.DefaultDataSourceName;

            if (!string.IsNullOrEmpty(connectionString))
            {
                // update connection string if provided.
                updatedConnectionString = connectionString;
            }

            if (datasourceNameToConnectionString is null)
            {
                // single db scenario. 
                datasourceNameToConnectionString = new Dictionary<string, string>
                {
                    // add default db values to this
                    { dataSourceName, updatedConnectionString }
                };
            }

            // iterate over dictionary and update runtime config with connection strings.
            foreach ((string dataSourceKey, string connectionValue) in datasourceNameToConnectionString)
            {
                config.DataSourceNameToDataSource.TryGetValue(dataSourceKey, out DataSource? ds);
                if (ds is not null)
                {
                    string updatedConnection = connectionValue;

                    // Add Application Name for telemetry for MsSQL
                    if (ds.DatabaseType is DatabaseType.MSSQL)
                    {
                        updatedConnection = GetConnectionStringWithApplicationName(connectionValue);
                    }

                    ds = ds with { ConnectionString = updatedConnection };
                    config.DataSourceNameToDataSource[dataSourceKey] = ds;
                }
                else
                {
                    throw new DataApiBuilderException($"{nameof(dataSourceKey)} could not be found within the config", HttpStatusCode.InternalServerError, DataApiBuilderException.SubStatusCodes.DataSourceNotFound);
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
    public static JsonSerializerOptions GetSerializationOptions()
    {
        List<String> propertiesToExcludeForSerialization = new()
        {
            "DefaultDataSourceName",
            "DataSourceNameToDataSource",
            "EntityNameToDataSourceName"
        };

        JsonSerializerOptions options = new()
        {
            PropertyNameCaseInsensitive = false,
            PropertyNamingPolicy = new HyphenatedNamingPolicy(),
            ReadCommentHandling = JsonCommentHandling.Skip,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            IncludeFields = true,
        };
        options.Converters.Add(new EnumMemberJsonEnumConverterFactory());
        options.Converters.Add(new RestRuntimeOptionsConverterFactory());
        options.Converters.Add(new GraphQLRuntimeOptionsConverterFactory());
        options.Converters.Add(new EntitySourceConverterFactory());
        options.Converters.Add(new EntityActionConverterFactory());
        options.Converters.Add(new StringJsonConverterFactory());
        options.Converters.Add(new RuntimeConfigConditionalConverter(propertiesToExcludeForSerialization));
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

    /// <summary>
    /// Returns the hyphenated name for the given name.
    /// </summary>
    /// <param name="name">name.</param>
    /// <returns>hyphenatedname.</returns>
    public static string GenerateHyphenatedName(string name)
    {
        if (string.Equals(name, "graphql", StringComparison.OrdinalIgnoreCase))
        {
            return name.ToLower();
        }

        return string.Join("-", Regex.Split(name, @"(?<!^)(?=[A-Z])", RegexOptions.Compiled)).ToLower();
    }
}
