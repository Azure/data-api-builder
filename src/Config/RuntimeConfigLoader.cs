// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.CompilerServices;
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

            string updatedConnectionString = config.DataSource.ConnectionString;

            if (!string.IsNullOrEmpty(connectionString))
            {
                updatedConnectionString = connectionString;
            }

            // Add Application Name for telemetry for MsSQL
            // Do this only when environment variables have been replaced since it parsing
            // the connection string will result in an exception.
            if (config.DataSource.DatabaseType is DatabaseType.MSSQL && replaceEnvVar)
            {
                updatedConnectionString = GetConnectionStringWithApplicationName(updatedConnectionString);
            }

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
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new EnumMemberJsonEnumConverterFactory());
        options.Converters.Add(new RestRuntimeOptionsConverterFactory());
        options.Converters.Add(new GraphQLRuntimeOptionsConverterFactory());
        options.Converters.Add(new EntitySourceConverterFactory(replaceEnvVar));
        options.Converters.Add(new EntityGraphQLOptionsConverterFactory(replaceEnvVar));
        options.Converters.Add(new EntityRestOptionsConverterFactory(replaceEnvVar));
        options.Converters.Add(new EntityActionConverterFactory());
        options.Converters.Add(new StringJsonConverterFactory(replaceEnvVar));
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
