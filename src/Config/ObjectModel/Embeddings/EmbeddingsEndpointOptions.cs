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
    /// Default roles for the embedding endpoint in production mode.
    /// </summary>
    public static readonly string[] DEFAULT_ROLES = new[] { "authenticated" };

    /// <summary>
    /// Default roles for the embedding endpoint in development mode.
    /// </summary>
    public static readonly string[] DEFAULT_ROLES_DEVELOPMENT = new[] { "anonymous" };

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
    /// The roles allowed to access the embedding endpoint.
    /// When null in development mode, GetEffectiveRoles returns ["anonymous"].
    /// When null in production mode, GetEffectiveRoles returns ["authenticated"].
    /// </summary>
    [JsonPropertyName("roles")]
    public string[]? Roles { get; init; }

    /// <summary>
    /// The URL path for the embedding endpoint.
    /// Defaults to "/embed" if not specified.
    /// </summary>
    [JsonPropertyName("path")]
    public string? Path { get; init; }

    /// <summary>
    /// Gets the effective path for the embedding endpoint.
    /// Returns the configured path if specified, otherwise returns the default "/embed".
    /// </summary>
    [JsonIgnore]
    public string EffectivePath => Path ?? DEFAULT_PATH;

    /// <summary>
    /// Gets the effective roles based on configuration and environment.
    /// Returns configured roles if specified.
    /// In development mode without explicit roles, returns ["anonymous"] to allow easy testing.
    /// In production mode without explicit roles, returns ["authenticated"] for security.
    /// </summary>
    /// <param name="isDevelopmentMode">Whether the host is in development mode.</param>
    /// <returns>Array of allowed roles.</returns>
    public string[] GetEffectiveRoles(bool isDevelopmentMode)
    {
        if (Roles is not null && Roles.Length > 0)
        {
            return Roles;
        }

        // In development mode, allow anonymous access for easier testing
        // In production mode, require authentication by default
        return isDevelopmentMode ? DEFAULT_ROLES_DEVELOPMENT : DEFAULT_ROLES;
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
        string[]? roles = null,
        string? path = null)
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

        // Keep roles as-is (null if not provided) so validation can check it
        // GetEffectiveRoles() will provide the default when needed
        Roles = roles;
        Path = path;
    }
}
