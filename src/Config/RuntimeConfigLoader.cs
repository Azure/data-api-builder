// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Azure.DataApiBuilder.Config.Converters;
using Azure.DataApiBuilder.Config.NamingPolicies;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Product;
using Microsoft.Extensions.Logging;
using static Azure.DataApiBuilder.Config.ObjectModel.DataSource;

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

            string updatedConnectionString = config.DataSource.ConnectionString;

            if (!string.IsNullOrEmpty(connectionString))
            {
                updatedConnectionString = connectionString;
            }

            // Add Application Name for telemetry
            updatedConnectionString = GetConnectionStringWithApplicationName(config.DataSource.DatabaseType, updatedConnectionString);
            config = config with { DataSource = config.DataSource with { ConnectionString = updatedConnectionString } };
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

    /// <summary>
    /// It adds or replaces a property in the connection string with `Application Name` property.
    /// If the connection string already contains the property, it uses a regular expression to update the existing value
    /// by adding the DataApiBuilder Application Name (dab_oss/dab_hosted). If not, it appends the property `Application Name` to the connection string.
    /// This method only adds the `Application Name` property for MSSQL, as other DB's have different ways to specify the Application Name. 
    /// </summary>
    /// <param name="databaseType">Type of Database</param>
    /// <param name="connectionString">Connection string for connecting to database.</param>
    /// <returns></returns>Updated connection string with `Application Name` property<summary>
    public static string GetConnectionStringWithApplicationName(DatabaseType databaseType, string connectionString)
    {
        if (databaseType is not DatabaseType.MSSQL || string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        string applicationName = ProductInfo.GetDataApiBuilderUserAgent();

        if (connectionString.Contains(CONN_STRING_APP_NAME_PROPERTY, StringComparison.OrdinalIgnoreCase))
        {
            connectionString = Regex.Replace(connectionString, $@"(?i)({CONN_STRING_APP_NAME_PROPERTY}.*?)(;|$)", $"$1,{applicationName}$2");
        }
        else if (connectionString.Contains(CONN_STRING_APP_NAME_PROPERTY_SHORT, StringComparison.OrdinalIgnoreCase))
        {
            connectionString = Regex.Replace(connectionString, $@"(?i)({CONN_STRING_APP_NAME_PROPERTY_SHORT}.*?)(;|$)", $"$1,{applicationName}$2");
        }
        else
        {
            if (!connectionString.EndsWith(";"))
            {
                connectionString += ";";
            }

            connectionString += $"{CONN_STRING_APP_NAME_PROPERTY}=" + applicationName + ";";
        }

        return connectionString;
    }
}
