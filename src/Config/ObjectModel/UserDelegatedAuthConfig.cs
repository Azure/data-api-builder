// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Configuration for user-delegated authentication (OBO - On-Behalf-Of).
/// Enables per-user Entra ID access token authentication to Azure SQL.
/// </summary>
/// <param name="Enabled">Whether user-delegated authentication is enabled.</param>
/// <param name="DatabaseAudience">The Azure SQL resource identifier for token acquisition.</param>
/// <param name="DisableConnectionPooling">Explicitly control connection pooling behavior. Default: true (disabled) for safety. Connection pooling is disabled by default in OBO scenarios to prevent token reuse across different user contexts.</param>
/// <param name="TokenCacheDurationMinutes">In-memory cache duration for OBO tokens per user. Default: 50 minutes.</param>
public record UserDelegatedAuthConfig(
    bool Enabled = false,
    string? DatabaseAudience = null,
    bool? DisableConnectionPooling = null,
    int? TokenCacheDurationMinutes = null)
{
    /// <summary>
    /// Default value for token cache duration in minutes.
    /// Must be less than typical token lifetime (60 min).
    /// </summary>
    public const int DEFAULT_TOKEN_CACHE_DURATION_MINUTES = 50;

    /// <summary>
    /// Default value for connection pooling (disabled for safety in MVP).
    /// </summary>
    public const bool DEFAULT_DISABLE_CONNECTION_POOLING = true;

    /// <summary>
    /// Minimum allowed token cache duration in minutes.
    /// </summary>
    public const int MIN_TOKEN_CACHE_DURATION_MINUTES = 1;

    /// <summary>
    /// Maximum allowed token cache duration in minutes.
    /// Must be less than typical token lifetime (60 min).
    /// </summary>
    public const int MAX_TOKEN_CACHE_DURATION_MINUTES = 59;

    /// <summary>
    /// Gets the effective token cache duration value.
    /// Returns the configured value or the default if not specified.
    /// </summary>
    [JsonIgnore]
    public int EffectiveTokenCacheDurationMinutes =>
        TokenCacheDurationMinutes ?? DEFAULT_TOKEN_CACHE_DURATION_MINUTES;

    /// <summary>
    /// Gets the effective connection pooling setting.
    /// Returns the configured value or the default if not specified.
    /// </summary>
    [JsonIgnore]
    public bool EffectiveDisableConnectionPooling =>
        DisableConnectionPooling ?? DEFAULT_DISABLE_CONNECTION_POOLING;
}
