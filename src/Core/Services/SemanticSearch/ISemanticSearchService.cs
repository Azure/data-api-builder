// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Models;

namespace Azure.DataApiBuilder.Core.Services.SemanticSearch;

public interface ISemanticSearchService
{
    Task<IReadOnlyList<SemanticSearchCandidate>> GetCandidatesAsync(
        string entityName,
        EntitySemanticSearchOptions options,
        IReadOnlyList<string> primaryKeyColumns,
        string semanticSearchValue,
        double similarityThreshold,
        int top);
}
