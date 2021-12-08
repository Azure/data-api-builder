using System.Data.Common;
using System.Linq;
using Microsoft.Data.SqlClient;

namespace Azure.DataGateway.Service.Resolvers
{
    /// <summary>
    /// Class for building MsSql queries.
    /// </summary>
    public class MsSqlQueryBuilder : IQueryBuilder
    {
        private const string FOR_JSON_SUFFIX = " FOR JSON PATH, INCLUDE_NULL_VALUES";
        private const string WITHOUT_ARRAY_WRAPPER_SUFFIX = "WITHOUT_ARRAY_WRAPPER";

        private static DbCommandBuilder _builder = new SqlCommandBuilder();

        public string DataIdent { get; } = "[data]";

        /// <summary>
        /// Enclose the given string within [] specific for MsSql.
        /// </summary>
        /// <param name="ident">The unquoted identifier to be enclosed.</param>
        /// <returns>The quoted identifier.</returns>
        public string QuoteIdentifier(string ident)
        {
            return _builder.QuoteIdentifier(ident);
        }

        public string WrapSubqueryColumn(string column, SqlQueryStructure subquery)
        {
            if (subquery.IsListQuery)
            {
                return $"JSON_QUERY (COALESCE({column}, '[]'))";
            }

            return $"JSON_QUERY ({column})";
        }

        public string Build(SqlQueryStructure structure)
        {
            string fromSql = structure.TableSql();
            fromSql += string.Join(
                    "",
                    structure.JoinQueries.Select(
                        x => $" OUTER APPLY ({Build(x.Value)}) AS {QuoteIdentifier(x.Key)}({DataIdent})"));
            string query = $"SELECT TOP {structure.Limit()} {structure.ColumnsSql()}"
                + $" FROM {fromSql}"
                + $" WHERE {structure.PredicatesSql()}"
                + $" ORDER BY {structure.OrderBySql()}";

            query += FOR_JSON_SUFFIX;
            if (!structure.IsListQuery)
            {
                query += "," + WITHOUT_ARRAY_WRAPPER_SUFFIX;
            }

            return query;
        }
    }
}
