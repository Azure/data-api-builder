// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel.Embeddings;

/// <summary>
/// Endpoint configuration for the embedding service.
/// </summary>
public record EmbeddingsEndpointOptions
{
    /// <summary>
    /// Default path for the embedding endpoint.
    /// </summary>
    public const string DEFAULT_PATH = "/embed";

    /// <summary>
    /// Anonymous role constant.
    /// </summary>
    public const string ANONYMOUS_ROLE = "anonymous";

    /// <summary>
    /// Whether the endpoint is enabled. Defaults to false.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    /// <summary>
    /// Flag indicating whether the user provided the enabled setting.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    public bool UserProvidedEnabled { get; init; }

    /// <summary>
    /// The endpoint path. Defaults to "/embed".
    /// </summary>
    [JsonPropertyName("path")]
    public string? Path { get; init; }

    /// <summary>
    /// Flag indicating whether the user provided a custom path.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    public bool UserProvidedPath { get; init; }

    /// <summary>
    /// The roles allowed to access the embedding endpoint.
    /// In development mode, defaults to ["anonymous"].
    /// In production mode, must be explicitly configured.
    /// </summary>
    [JsonPropertyName("roles")]
    public string[]? Roles { get; init; }

    /// <summary>
    /// Flag indicating whether the user provided roles.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    public bool UserProvidedRoles { get; init; }

    /// <summary>
    /// Gets the effective path, using default if not specified.
    /// </summary>
    [JsonIgnore]
    public string EffectivePath => Path ?? DEFAULT_PATH;

    /// <summary>
    /// Gets the effective roles based on host mode.
    /// In development mode, returns ["anonymous"] if no roles specified.
    /// In production mode, returns the configured roles or empty array.
    /// </summary>
    /// <param name="isDevelopmentMode">Whether the host is in development mode.</param>
    /// <returns>Array of allowed roles.</returns>
    public string[] GetEffectiveRoles(bool isDevelopmentMode)
    {
        if (Roles is not null && Roles.Length > 0)
        {
            return Roles;
        }

        // In development mode, default to anonymous access
        if (isDevelopmentMode)
        {
            return new[] { ANONYMOUS_ROLE };
        }

        // In production mode with no roles specified, return empty (no access)
        return Array.Empty<string>();
    }

    /// <summary>
    /// Checks if the given role is allowed to access the embedding endpoint.
    /// </summary>
    /// <param name="role">The role to check.</param>
    /// <param name="isDevelopmentMode">Whether the host is in development mode.</param>
    /// <returns>True if the role is allowed; otherwise, false.</returns>
    public bool IsRoleAllowed(string role, bool isDevelopmentMode)
    {
        string[] effectiveRoles = GetEffectiveRoles(isDevelopmentMode);
        return effectiveRoles.Contains(role, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Default constructor.
    /// </summary>
    public EmbeddingsEndpointOptions()
    {
        Enabled = false;
    }

    /// <summary>
    /// Constructor with optional parameters.
    /// </summary>
    [JsonConstructor]
    public EmbeddingsEndpointOptions(
        bool? enabled = null,
        string? path = null,
        string[]? roles = null)
    {
        if (enabled.HasValue)
        {
            Enabled = enabled.Value;
            UserProvidedEnabled = true;
        }
        else
        {
            Enabled = false;
        }

        if (path is not null)
        {
            Path = path;
            UserProvidedPath = true;
        }

        if (roles is not null)
        {
            Roles = roles;
            UserProvidedRoles = true;
        }
    }
}
