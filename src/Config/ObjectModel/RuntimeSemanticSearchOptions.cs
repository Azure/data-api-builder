// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Runtime semantic search configuration.
/// </summary>
public record RuntimeSemanticSearchOptions
{
    /// <summary>
    /// Endpoint used to generate embeddings from semantic-search input text.
    /// </summary>
    [JsonPropertyName("embedding-endpoint")]
    public string? EmbeddingEndpoint { get; init; } = null;

    /// <summary>
    /// Optional API key used as a bearer token for embedding endpoint calls.
    /// </summary>
    [JsonPropertyName("embedding-api-key")]
    public string? EmbeddingApiKey { get; init; } = null;

    [JsonConstructor]
    public RuntimeSemanticSearchOptions(string? EmbeddingEndpoint = null, string? EmbeddingApiKey = null)
    {
        this.EmbeddingEndpoint = EmbeddingEndpoint;
        this.EmbeddingApiKey = EmbeddingApiKey;
    }
}