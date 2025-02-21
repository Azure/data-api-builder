// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.Models
{
    /// <summary>
    /// Represents the metadata for a GraphQL groupBy operation.
    /// </summary>
    public class GroupByMetadata
    {
        /// <summary>
        /// The fields to group by.
        /// </summary>
        public Dictionary<string, Column> Fields { get; set; }

        /// <summary>
        /// The aggregation operations requested.
        /// </summary>
        public List<AggregationOperation> Aggregations { get; set; }

        /// <summary>
        /// Whether fields were requested in the groupBy result.
        /// </summary>
        public bool RequestedFields { get; set; }

        /// <summary>
        /// Whether aggregations were requested in the groupBy result.
        /// </summary>
        public bool RequestedAggregations { get; set; }

        /// <summary>
        /// Initializes a new instance of GroupByMetadata.
        /// </summary>
        public GroupByMetadata()
        {
            Fields = new Dictionary<string, Column>();
            Aggregations = new List<AggregationOperation>();
            RequestedFields = false;
            RequestedAggregations = false;
        }
    }

    /// <summary>
    /// Represents a single aggregation operation in a groupBy query.
    /// </summary>
    public class AggregationOperation
    {
        /// <summary>
        /// The column to aggregate on.
        /// </summary>
        public AggregationColumn Column { get; set; }

        /// <summary>
        /// The predicates for the having clause.
        /// </summary>
        public List<Predicate>? HavingPredicates { get; set; }

        /// <summary>
        /// Initializes a new instance of AggregationOperation.
        /// </summary>
        public AggregationOperation(AggregationColumn column, List<Predicate>? having = null)
        {
            Column = column;
            HavingPredicates = having;
        }
    }
}
