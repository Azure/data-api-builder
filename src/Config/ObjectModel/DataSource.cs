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
public record DataSource(
    DatabaseType DatabaseType,
    string ConnectionString,
    Dictionary<string, object?>? Options = null,
    DatasourceHealthCheckConfig? Health = null)
{
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
    /// Configuration for user-delegated authentication (OBO) against the
    /// configured database.
    /// </summary>
    [JsonPropertyName("user-delegated-auth")]
    public UserDelegatedAuthOptions? UserDelegatedAuth { get; init; }

    /// <summary>
    /// Indicates whether user-delegated authentication is enabled for this data source.
    /// </summary>
    [JsonIgnore]
    public bool IsUserDelegatedAuthEnabled =>
        UserDelegatedAuth is not null && UserDelegatedAuth.Enabled;

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

/// <summary>
/// Options for user-delegated authentication (OBO) for a data source.
/// 
/// When OBO is NOT enabled (default): DAB connects to the database using a single application principal,
/// either via Managed Identity or credentials supplied in the connection string. All requests execute
/// under the same database identity regardless of which user made the API call.
/// 
/// When OBO IS enabled: DAB exchanges the calling user's JWT for a database access token using the
/// On-Behalf-Of flow. This allows DAB to connect to the database as the actual user, enabling
/// Row-Level Security (RLS) filtering based on user identity.
/// 
/// OBO requires an Azure AD App Registration (separate from the DAB service's Managed Identity).
/// The operator deploying DAB must set the following environment variables for the OBO App Registration,
/// which DAB reads at startup via Environment.GetEnvironmentVariable():
/// - DAB_OBO_CLIENT_ID: The Application (client) ID of the OBO App Registration
/// - DAB_OBO_TENANT_ID: The Directory (tenant) ID where the OBO App Registration is registered
/// - DAB_OBO_CLIENT_SECRET: The client secret of the OBO App Registration (not a user secret)
/// 
/// These credentials belong to the OBO App Registration, which acts as a confidential client to exchange
/// the incoming user JWT for a database access token. The user provides only their JWT; DAB uses the
/// App Registration credentials to perform the OBO token exchange on their behalf.
/// 
/// These can be set in the hosting environment (e.g., Azure Container Apps secrets, Kubernetes secrets,
/// Docker environment variables, or local shell environment).
/// 
/// Note: DAB-specific prefixes (DAB_OBO_*) are used instead of AZURE_* to avoid conflict with
/// DefaultAzureCredential, which interprets AZURE_CLIENT_ID as a User-Assigned Managed Identity ID.
/// At startup (when no user context is available), DAB falls back to Managed Identity for metadata operations.
/// </summary>
/// <param name="Enabled">Whether user-delegated authentication is enabled.</param>
/// <param name="Provider">The authentication provider (currently only EntraId is supported).</param>
/// <param name="DatabaseAudience">Audience used when acquiring database tokens on behalf of the user.</param>
public record UserDelegatedAuthOptions(
    [property: JsonPropertyName("enabled")] bool Enabled = false,
    [property: JsonPropertyName("provider")] string? Provider = null,
    [property: JsonPropertyName("database-audience")] string? DatabaseAudience = null)
{
    /// <summary>
    /// Default duration, in minutes, to cache tokens for a given delegated identity.
    /// With a 5-minute early refresh buffer, tokens are refreshed at the 40-minute mark.
    /// </summary>
    public const int DEFAULT_TOKEN_CACHE_DURATION_MINUTES = 45;

    /// <summary>
    /// Environment variable name for OBO App Registration client ID.
    /// Uses DAB-specific prefix to avoid conflict with AZURE_CLIENT_ID which is
    /// interpreted by DefaultAzureCredential/ManagedIdentityCredential as a
    /// User-Assigned Managed Identity ID.
    /// </summary>
    public const string DAB_OBO_CLIENT_ID_ENV_VAR = "DAB_OBO_CLIENT_ID";

    /// <summary>
    /// Environment variable name for OBO App Registration client secret.
    /// Used for On-Behalf-Of token exchange.
    /// </summary>
    public const string DAB_OBO_CLIENT_SECRET_ENV_VAR = "DAB_OBO_CLIENT_SECRET";

    /// <summary>
    /// Environment variable name for OBO tenant ID.
    /// Uses DAB-specific prefix for consistency with OBO client ID.
    /// </summary>
    public const string DAB_OBO_TENANT_ID_ENV_VAR = "DAB_OBO_TENANT_ID";
}
