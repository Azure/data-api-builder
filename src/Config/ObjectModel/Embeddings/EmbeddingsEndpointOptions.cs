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
    /// Default roles for the embedding endpoint.
    /// </summary>
    public static readonly string[] DEFAULT_ROLES = new[] { "authenticated" };

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
    /// When null, GetEffectiveRoles returns ["authenticated"] by default.
    /// In production mode, must be explicitly configured (cannot be null).
    /// </summary>
    [JsonPropertyName("roles")]
    public string[]? Roles { get; init; }

    /// <summary>
    /// Gets the effective roles.
    /// Returns configured roles if specified, otherwise defaults to ["authenticated"].
    /// </summary>
    /// <param name="isDevelopmentMode">Whether the host is in development mode (kept for API compatibility).</param>
    /// <returns>Array of allowed roles.</returns>
    public string[] GetEffectiveRoles(bool isDevelopmentMode)
    {
        if (Roles is not null && Roles.Length > 0)
        {
            return Roles;
        }

        return DEFAULT_ROLES;
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

        // Keep roles as-is (null if not provided) so validation can check it
        // GetEffectiveRoles() will provide the default when needed
        Roles = roles;
    }
}
