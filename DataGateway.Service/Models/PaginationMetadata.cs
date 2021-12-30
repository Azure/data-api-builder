using System.Collections.Generic;
using Azure.DataGateway.Service.Resolvers;

namespace Azure.DataGateway.Service.Models
{

    /// <summary>
    /// Holds pagination related information for the query and its subqueries
    /// </summary>
    public class PaginationMetadata
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
        /// Shows if <c>items</c> is requested from the pagination result
        /// </summary>
        public bool RequestedEndCursor { get; set; } = DEFAULT_PAGINATION_FLAGS_VALUE;

        /// <summary>
        /// Shows if <c>items</c> is requested from the pagination result
        /// </summary>
        public bool RequestedHasNextPage { get; set; } = DEFAULT_PAGINATION_FLAGS_VALUE;

        /// <summary>
        /// Keeps a reference to the SqlQueryStructure the pagination metadata is associated with
        /// </summary>
        public SqlQueryStructure Structure { get; }

        /// <summary>
        /// Holds the pagination metadata for subqueries
        /// </summary>
        public Dictionary<string, PaginationMetadata> Subqueries { get; set; } = new();

        public PaginationMetadata(SqlQueryStructure structure)
        {
            Structure = structure;
        }
    }
}
