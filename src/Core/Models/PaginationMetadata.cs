// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Core.Resolvers;

namespace Azure.DataApiBuilder.Core.Models;

/// <summary>
/// Holds pagination related information for the query and its subqueries
/// </summary>
public class PaginationMetadata : IMetadata
{
    public const bool DEFAULT_PAGINATION_FLAGS_VALUE = false;

    /// <summary>
    /// Shows if the type is a *Connection pagination result type
    /// <summary>
    public bool IsPaginated { get; set; } = DEFAULT_PAGINATION_FLAGS_VALUE;

    /// <summary>
    /// Shows if <c>items</c> is requested from the pagination result
    /// </summary>
    public bool RequestedItems { get; set; } = DEFAULT_PAGINATION_FLAGS_VALUE;

    /// <summary>
    /// Shows if <c>GroupBY</c> is requested from the pagination result
    /// </summary>
    public bool RequestedGroupBy { get; set; } = DEFAULT_PAGINATION_FLAGS_VALUE;

    /// <summary>
    /// Shows if <c>endCursor</c> is requested from the pagination result
    /// </summary>
    public bool RequestedEndCursor { get; set; } = DEFAULT_PAGINATION_FLAGS_VALUE;

    /// <summary>
    /// Shows if <c>hasNextPage</c> is requested from the pagination result
    /// </summary>
    public bool RequestedHasNextPage { get; set; } = DEFAULT_PAGINATION_FLAGS_VALUE;

    /// <summary>
    /// Keeps a reference to the SqlQueryStructure the pagination metadata is associated with
    /// </summary>
    public SqlQueryStructure? Structure { get; }

    /// <summary>
    /// Holds the pagination metadata for subqueries
    /// </summary>
    public Dictionary<string, PaginationMetadata> Subqueries { get; set; } = new();

    /// <summary>
    /// Holds the keyset pagination predicate for a SqlQueryStructure
    /// </summary>
    public KeysetPaginationPredicate? PaginationPredicate { get; set; }

    public PaginationMetadata(SqlQueryStructure? structure)
    {
        Structure = structure;
    }

    /// <summary>
    /// Create a pagination metadata which is not coupled with any SqlQueryStructure
    /// </summary>
    public static PaginationMetadata MakeEmptyPaginationMetadata()
    {
        return new PaginationMetadata(null);
    }
}
