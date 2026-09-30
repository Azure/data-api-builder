// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Service.Models;

/// <summary>
/// Response model for a single document in a batch embedding response.
/// </summary>
public record EmbedDocumentResponse
{
    /// <summary>
    /// The unique key/identifier for this document (matches request key).
    /// </summary>
    [JsonPropertyName("key")]
    public string Key { get; init; }

    /// <summary>
    /// The embedding vectors for this document.
    /// If chunking is disabled or text fits in one chunk, this will contain one vector.
    /// If chunking is enabled and text is split, this will contain multiple vectors (one per chunk).
    /// </summary>
    [JsonPropertyName("data")]
    public float[][] Data { get; init; }

    public EmbedDocumentResponse(string key, float[][] data)
    {
        Key = key;
        Data = data;
    }
}
