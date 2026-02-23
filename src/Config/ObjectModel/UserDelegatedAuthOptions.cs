// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Configuration for user-delegated authentication (OBO - On-Behalf-Of).
/// Enables per-user Entra ID access token authentication to Azure SQL.
/// </summary>
/// <param name="Enabled">Whether user-delegated authentication is enabled.</param>
/// <param name="Provider">Identity provider for user-delegated authentication.</param>
/// <param name="DatabaseAudience">The Azure SQL resource identifier for token acquisition.</param>
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
    public const string AZURE_CLIENT_ID_ENV_VAR = "DAB_OBO_CLIENT_ID";

    /// <summary>
    /// Environment variable name for OBO App Registration client secret.
    /// Used for On-Behalf-Of token exchange.
    /// </summary>
    public const string AZURE_CLIENT_SECRET_ENV_VAR = "DAB_OBO_CLIENT_SECRET";

    /// <summary>
    /// Environment variable name for OBO tenant ID.
    /// Uses DAB-specific prefix for consistency with OBO client ID.
    /// </summary>
    public const string AZURE_TENANT_ID_ENV_VAR = "DAB_OBO_TENANT_ID";
}
