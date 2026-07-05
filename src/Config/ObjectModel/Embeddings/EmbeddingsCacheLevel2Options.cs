// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel.Embeddings;

/// <summary>
/// Embeddings-specific level2 (distributed) cache configuration for Azure Managed Redis.
/// Properties are nullable to support DAB CLI merge config expected behavior.
/// </summary>
public record EmbeddingsCacheLevel2Options
{
    /// <summary>
    /// Whether the L2 distributed Azure Managed Redis cache should be used for embeddings.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; } = false;

    /// <summary>
    /// The connection string for Azure Managed Redis.
    /// Example: "contoso.redis.cache.windows.net:6380,password=...,ssl=True,abortConnect=False"
    /// Authentication is auto-detected from the connection string:
    /// - If a password is provided (e.g. "password=..." or an access key), connection-string based authentication is used.
    /// - If no password is provided and the endpoint is not localhost/loopback, Microsoft Entra ID (token-based) authentication
    ///   is used via DefaultAzureCredential (Managed Identity when available, with standard credential fallbacks).
    /// </summary>
    [JsonPropertyName("connection-string")]
    public string? ConnectionString { get; init; } = null;

    [JsonConstructor]
    public EmbeddingsCacheLevel2Options(
        bool? Enabled = false,
        string? ConnectionString = null)
    {
        this.Enabled = Enabled;
        this.ConnectionString = ConnectionString;
    }
}
