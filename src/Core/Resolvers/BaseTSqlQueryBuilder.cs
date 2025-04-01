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

        /// <summary>
        /// Build the Group By Clause needed to append to the main query
        /// </summary>
        /// <param name="structure">Sql query structure to build query on</param>
        /// <returns>SQL query with group-by clause</returns>
        protected virtual string BuildGroupBy(SqlQueryStructure structure)
        {
            // Add GROUP BY clause if there are any group by columns
            if (structure.GroupByMetadata.Fields.Any())
            {
                return $" GROUP BY {string.Join(", ", structure.GroupByMetadata.Fields.Values.Select(c => Build(c)))}";
            }

            return string.Empty;
        }

        /// <summary>
        /// Build the Having clause needed to append to the main query
        /// </summary>
        /// <param name="structure">Sql query structure to build query on</param>
        /// <returns>SQL query with having clause</returns>
        protected virtual string BuildHaving(SqlQueryStructure structure)
        {
            if (structure.GroupByMetadata.Aggregations.Count > 0)
            {
                List<Predicate>? havingPredicates = structure.GroupByMetadata.Aggregations
                      .SelectMany(aggregation => aggregation.HavingPredicates ?? new List<Predicate>())
                      .ToList();

                if (havingPredicates.Any())
                {
                    return $" HAVING {Build(havingPredicates)}";
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Build the Order By clause needed to append to the main query
        /// </summary>
        /// <param name="structure">Sql query structure to build query on</param>
        /// <returns>SQL query with order-by clause</returns>
        protected virtual string BuildOrderBy(SqlQueryStructure structure)
        {
            if (structure.OrderByColumns.Any())
            {
                return $" ORDER BY {Build(structure.OrderByColumns)}";
            }

            return string.Empty;
        }

        /// <summary>
        /// Build the aggregation columns needed to append to the main query
        /// </summary>
        /// <param name="structure">Sql query structure to build query on</param>
        /// <returns>SQL query with aggregation columns</returns>
        protected virtual string BuildAggregationColumns(SqlQueryStructure structure)
        {
            string aggregations = string.Empty;
            if (structure.GroupByMetadata.Aggregations.Count > 0)
            {
                if (structure.Columns.Any())
                {
                    aggregations = $",{BuildAggregationColumns(structure.GroupByMetadata)}";
                }
                else
                {
                    aggregations = $"{BuildAggregationColumns(structure.GroupByMetadata)}";
                }
            }

            return aggregations;
        }

        /// <summary>
        /// Build the aggregation columns needed to append to the main query
        /// </summary>
        /// <param name="metadata">GroupByMetadata</param>
        /// <returns>SQL query with aggregation columns</returns>
        protected virtual string BuildAggregationColumns(GroupByMetadata metadata)
        {
            return string.Join(", ", metadata.Aggregations.Select(aggregation => Build(aggregation.Column, useAlias: true)));
        }
    }
}
