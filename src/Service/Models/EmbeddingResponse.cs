// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Service.Models;

/// <summary>
/// JSON response model for the embedding endpoint.
/// Provides a structured, extensible format instead of raw comma-separated text.
/// </summary>
public record EmbeddingResponse
{
    /// <summary>
    /// The embedding vector as an array of floating-point values.
    /// </summary>
    [JsonPropertyName("embedding")]
    public float[] Embedding { get; init; }

    /// <summary>
    /// The number of dimensions in the embedding vector.
    /// </summary>
    [JsonPropertyName("dimensions")]
    public int Dimensions { get; init; }

    public EmbeddingResponse(float[] embedding)
    {
        Embedding = embedding;
        Dimensions = embedding.Length;
    }
}
