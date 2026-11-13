using System.Text.RegularExpressions;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Models;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    /// <summary>
    /// Base query builder class for T-SQL engine
    /// Can be used by dwsql and mssql
    /// </summary>
    public abstract class BaseTSqlQueryBuilder : BaseSqlQueryBuilder
    {
        protected const string FOR_JSON_SUFFIX = " FOR JSON PATH, INCLUDE_NULL_VALUES";
        protected const string WITHOUT_ARRAY_WRAPPER_SUFFIX = "WITHOUT_ARRAY_WRAPPER";

        /// <summary>
        /// Builds the engine-specific JSON query used by aggregation.
        /// </summary>
        public abstract string Build(SqlQueryStructure structure);

        /// <summary>
        /// Orders grouped aggregates and applies T-SQL offset pagination before JSON serialization.
        /// </summary>
        public string BuildAggregation(SqlQueryStructure structure, bool descending, int? offset = null, long? limit = null)
        {
            string sql = Build(structure);
            if (structure.GroupByMetadata.Fields.Count == 0)
            {
                return sql;
            }

            string direction = descending ? "DESC" : "ASC";
            string suffix = " ORDER BY " + string.Join(", ", structure.GroupByMetadata.Aggregations.Select(a => $"{Build(a.Column).TrimEnd()} {direction}"));
            if (limit.HasValue)
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit.Value);
                ArgumentOutOfRangeException.ThrowIfNegative(offset ?? 0);
                string offsetParameter = BaseQueryStructure.GetEncodedParamName(structure.Counter.Next());
                structure.Parameters.Add(offsetParameter, new DbConnectionParam(offset ?? 0));
                string limitParameter = BaseQueryStructure.GetEncodedParamName(structure.Counter.Next());
                structure.Parameters.Add(limitParameter, new DbConnectionParam(limit.Value));
                suffix += $" OFFSET {offsetParameter} ROWS FETCH NEXT {limitParameter} ROWS ONLY";
                // TOP and OFFSET/FETCH cannot be used together in the same SELECT.
                sql = Regex.Replace(sql, @"SELECT TOP \d+", "SELECT");
            }

            int jsonPathIndex = sql.IndexOf(" FOR JSON PATH", StringComparison.OrdinalIgnoreCase);
            return jsonPathIndex > 0 ? sql.Insert(jsonPathIndex, suffix) : sql + suffix;
        }

        /// <summary>
        /// Build the Json Path query needed to append to the main query
        /// </summary>
        /// <param name="structure">Sql query structure to build query on</param>
        /// <returns>SQL query with JSON PATH format</returns>
        protected virtual string BuildJsonPath(SqlQueryStructure structure)
        {
            string query = string.Empty;
            query += FOR_JSON_SUFFIX;
            if (!structure.IsListQuery)
            {
                query += "," + WITHOUT_ARRAY_WRAPPER_SUFFIX;
            }

            return query;
        }

        /// <summary>
        /// Build the predicates query needed to append to the main query
        /// </summary>
        /// <param name="structure">Sql query structure to build query on</param>
        /// <returns>SQL query with predicates</returns>
        protected virtual string BuildPredicates(SqlQueryStructure structure)
        {
            return JoinPredicateStrings(
                          structure.GetDbPolicyForOperation(EntityActionOperation.Read),
                          structure.FilterPredicates,
                          Build(structure.Predicates),
                          Build(structure.PaginationMetadata.PaginationPredicate));
        }

    }
}
