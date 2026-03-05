// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.Resolvers
{
    /// <summary>
    /// Represents the structure of a DAX query for Semantic Models (Analysis Services).
    /// This is the intermediate representation used to generate DAX EVALUATE statements.
    /// </summary>
    public class DaxQueryStructure
    {
        /// <summary>
        /// The name of the target table in the semantic model.
        /// </summary>
        public string TableName { get; set; } = string.Empty;

        /// <summary>
        /// Columns to include in the query result. If empty, all columns are returned.
        /// Key: alias (GraphQL field name), Value: original column name from the semantic model.
        /// The alias is used in the SELECTCOLUMNS expression result, the original name is used
        /// for the DAX column reference (e.g., 'table'[Original Name]).
        /// </summary>
        public Dictionary<string, string> SelectedColumns { get; set; } = new();

        /// <summary>
        /// Measures to compute and include in the result (via ADDCOLUMNS).
        /// Key: alias for the measure in results, Value: DAX measure expression (e.g., [Total Sales]).
        /// </summary>
        public Dictionary<string, string> IncludedMeasures { get; set; } = new();

        /// <summary>
        /// Filter predicates to apply to the query (used with CALCULATETABLE/FILTER).
        /// Each entry is a DAX filter expression string.
        /// </summary>
        public List<string> FilterPredicates { get; set; } = new();

        /// <summary>
        /// Columns to order by. Key: column name, Value: true for ascending, false for descending.
        /// </summary>
        public List<(string Column, bool IsAscending)> OrderByColumns { get; set; } = new();

        /// <summary>
        /// Maximum number of rows to return (used with TOPN).
        /// Null means no limit.
        /// </summary>
        public int? TopCount { get; set; }

        /// <summary>
        /// Number of rows to skip for pagination.
        /// Used in combination with TopCount for cursor-based pagination.
        /// </summary>
        public int? SkipCount { get; set; }

        /// <summary>
        /// Parameters for the query, mapped by name.
        /// </summary>
        public Dictionary<string, object?> Parameters { get; set; } = new();
    }
}
