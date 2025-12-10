// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.HealthCheck;
using Azure.DataApiBuilder.Config.NamingPolicies;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Contains the information needed to connect to the backend database.
/// </summary>
/// <param name="DatabaseType">Type of database to use.</param>
/// <param name="ConnectionString">Connection string to access the database.</param>
/// <param name="Options">Custom options for the specific database. If there are no options, this could be null.</param>
/// <param name="Health">Health check configuration for the datasource.</param>
/// <param name="IncludeVectorFieldsByDefault">When false (default), vector-type columns are omitted from results. Only applicable to mssql.</param>
public record DataSource(
    DatabaseType DatabaseType,
    string ConnectionString,
    Dictionary<string, object?>? Options = null,
    DatasourceHealthCheckConfig? Health = null,
    bool IncludeVectorFieldsByDefault = false)
{
    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write include-vector-fields-by-default
    /// property and value to the runtime config file.
    /// When user doesn't provide the property/value, which signals DAB to use the default (false),
    /// the DAB CLI should not write the default value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    public bool UserProvidedIncludeVectorFieldsByDefault { get; init; } = false;

    [JsonIgnore]
    public bool IsDatasourceHealthEnabled =>
        Health is null || Health.Enabled;

    [JsonIgnore]
    public int DatasourceThresholdMs
    {
        get
        {
            if (Health == null || Health?.ThresholdMs == null)
            {
                return HealthCheckConstants.DEFAULT_THRESHOLD_RESPONSE_TIME_MS;
            }
            else
            {
                return Health.ThresholdMs;
            }
        }
    }

    /// <summary>
    /// Converts the <c>Options</c> dictionary into a typed options object.
    /// May return null if the dictionary is null.
    /// </summary>
    /// <typeparam name="TOptionType">The strongly typed object for Options.</typeparam>
    /// <returns>The strongly typed representation of Options.</returns>
    /// <exception cref="NotSupportedException">Thrown when the provided <c>TOptionType</c> is not supported for parsing.</exception>
    public TOptionType? GetTypedOptions<TOptionType>() where TOptionType : IDataSourceOptions
    {
        HyphenatedNamingPolicy namingPolicy = new();

        if (typeof(TOptionType).IsAssignableFrom(typeof(CosmosDbNoSQLDataSourceOptions)))
        {
            return Options is not null ?
                (TOptionType)(object)new CosmosDbNoSQLDataSourceOptions(
                Database: ReadStringOption(namingPolicy.ConvertName(nameof(CosmosDbNoSQLDataSourceOptions.Database))),
                Container: ReadStringOption(namingPolicy.ConvertName(nameof(CosmosDbNoSQLDataSourceOptions.Container))),
                Schema: ReadStringOption(namingPolicy.ConvertName(nameof(CosmosDbNoSQLDataSourceOptions.Schema))),
                // The "raw" schema will be provided via the controller to setup config, rather than parsed from the JSON file.
                GraphQLSchema: ReadStringOption(namingPolicy.ConvertName(nameof(CosmosDbNoSQLDataSourceOptions.GraphQLSchema))))
                : default;
        }

        if (typeof(TOptionType).IsAssignableFrom(typeof(MsSqlOptions)))
        {
            return (TOptionType)(object)new MsSqlOptions(
                SetSessionContext: ReadBoolOption(namingPolicy.ConvertName(nameof(MsSqlOptions.SetSessionContext))));
        }

        throw new NotSupportedException($"The type {typeof(TOptionType).FullName} is not a supported strongly typed options object");
    }

    private string? ReadStringOption(string option)
    {
        if (Options is not null && Options.TryGetValue(option, out object? value) && value is string stringValue)
        {
            return stringValue;
        }

        return null;
    }

    private bool ReadBoolOption(string option)
    {
        if (Options is not null && Options.TryGetValue(option, out object? value) && value is bool boolValue)
        {
            return boolValue;
        }

        return false;
    }

    [JsonIgnore]
    public string DatabaseTypeNotSupportedMessage => $"The provided database-type value: {DatabaseType} is currently not supported. Please check the configuration file.";
}

public interface IDataSourceOptions { }

/// <summary>
/// The CosmosDB NoSQL connection options.
/// </summary>
/// <param name="Database">Name of the default CosmosDB database.</param>
/// <param name="Container">Name of the default CosmosDB container.</param>
/// <param name="Schema">Path to the GraphQL schema file.</param>
/// <param name="GraphQLSchema">Raw contents of the GraphQL schema.</param>
public record CosmosDbNoSQLDataSourceOptions(string? Database, string? Container, string? Schema, string? GraphQLSchema) : IDataSourceOptions;

/// <summary>
/// Options for MsSql database.
/// </summary>
public record MsSqlOptions(bool SetSessionContext = true) : IDataSourceOptions;
