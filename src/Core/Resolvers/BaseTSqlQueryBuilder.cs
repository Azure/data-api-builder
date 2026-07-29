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

    }
}
