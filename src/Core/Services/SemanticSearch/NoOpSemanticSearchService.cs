// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Models;

namespace Azure.DataApiBuilder.Core.Services.SemanticSearch;

/// <summary>
/// Placeholder semantic search service used when no embedding/vector integration is configured.
/// </summary>
public sealed class NoOpSemanticSearchService : ISemanticSearchService
{
    public static readonly NoOpSemanticSearchService Instance = new();

    private NoOpSemanticSearchService()
    {
    }

    public Task<IReadOnlyList<SemanticSearchCandidate>> GetCandidatesAsync(
        string entityName,
        EntitySemanticSearchOptions options,
        IReadOnlyList<string> primaryKeyColumns,
        string semanticSearchValue,
        double similarityThreshold,
        int top)
    {
        return Task.FromResult<IReadOnlyList<SemanticSearchCandidate>>([]);
    }
}
