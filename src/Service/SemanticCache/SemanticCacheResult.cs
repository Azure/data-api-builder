// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace Azure.DataApiBuilder.Service.SemanticCache;

/// <summary>
/// Represents a semantic cache query result with similarity score.
/// </summary>
public class SemanticCacheResult
{
    /// <summary>
    /// The cached response JSON.
    /// </summary>
    public string ResponseJson { get; set; }

    /// <summary>
    /// The cosine similarity score (0.0 to 1.0).
    /// </summary>
    public double SimilarityScore { get; set; }

    /// <summary>
    /// The original query text that was cached.
    /// </summary>
    public string? OriginalQuery { get; set; }

    public SemanticCacheResult(string responseJson, double similarityScore, string? originalQuery = null)
    {
        ResponseJson = responseJson ?? throw new ArgumentNullException(nameof(responseJson));
        SimilarityScore = similarityScore;
        OriginalQuery = originalQuery;
    }
}
