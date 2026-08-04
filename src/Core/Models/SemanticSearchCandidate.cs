// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.Models;

/// <summary>
/// Represents one semantic candidate with SQL column values extracted from
/// the semantic document and primary key values used for dedupe/output mapping.
/// </summary>
public record SemanticSearchCandidate(
    IReadOnlyDictionary<string, object?> PrimaryKeyValues,
    IReadOnlyDictionary<string, object?> ColumnValues,
    double Similarity);
