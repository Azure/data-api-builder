using System.Collections.Generic;
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

            string query = $" FROM {fromSql}"
                + $" WHERE {structure.PredicatesSql()}"
                + $" ORDER BY {structure.OrderBySql()}";

            if (structure.IsPaginatedQuery)
            {
                List<string> requestedResults = new();
                List<string> requiredWrapperColumns = new();

                if (structure.IsRequestedPaginationResult("nodes"))
                {
                    requestedResults.Add($"{QuoteIdentifier("nodes")} = JSON_QUERY('[' + COALESCE(STRING_AGG([jsonelems], ', '), '') + ']')");

                    requiredWrapperColumns.Add($"[jsonelems] = (SELECT {string.Join(", ", structure.Columns.Keys.Select(columnName => QuoteIdentifier(columnName)))}" +
                                                $" {FOR_JSON_SUFFIX}, {WITHOUT_ARRAY_WRAPPER_SUFFIX})");
                }

                if (structure.IsRequestedPaginationResult("endCursor"))
                {
                    string primaryKeyField = QuoteIdentifier(structure.PrimaryKey()[0]);
                    requestedResults.Add($"{QuoteIdentifier("endCursor")} = CASE WHEN max({primaryKeyField}) IS NOT NULL THEN"
                                            + $" (SELECT CAST({structure.PaginationCursorJson()} AS VARBINARY(MAX)) FOR XML PATH(''), BINARY BASE64 )"
                                            + $" ELSE NULL END");

                    requiredWrapperColumns.Add(string.Join(", ", structure.PrimaryKey().Select(key => QuoteIdentifier(key))));
                }

                long baseQueryTop;
                string wrapperSelect = "SELECT";
                if (structure.IsRequestedPaginationResult("hasNextPage"))
                {
                    requestedResults.Add($"{QuoteIdentifier("hasNextPage")} = CAST(CASE WHEN max(___rowcount___) > {structure.Limit} THEN 1 ELSE 0 END AS BIT)");

                    requiredWrapperColumns.Add("COUNT(*) OVER() AS ___rowcount___");

                    baseQueryTop = structure.Limit + 1;
                    wrapperSelect += $" TOP {structure.Limit}";
                }
                else
                {
                    baseQueryTop = structure.Limit;
                }

                query = $"SELECT TOP {baseQueryTop} {structure.PaginationColumnsSql()}" + query;

                if (structure.IsRequestedPaginationResult("nodes") || structure.IsRequestedPaginationResult("hasNextPage"))
                {
                    // adds the wrapper and paginated query
                    query = $"SELECT {string.Join(", ", requestedResults)} FROM ({wrapperSelect} {string.Join(", ", requiredWrapperColumns)} FROM (" + query + ") AS wrapper) AS paginatedquery";
                }
                else
                {
                    // no need for wrapper
                    query = $"SELECT {string.Join(", ", requestedResults)} FROM (" + query + ") AS paginatedquery";
                }

                query += FOR_JSON_SUFFIX + ", " + WITHOUT_ARRAY_WRAPPER_SUFFIX;
            }
            else
            {
                query = $"SELECT TOP {structure.Limit} {structure.ColumnsSql()}" + query;

                query += FOR_JSON_SUFFIX;
                if (!structure.IsListQuery)
                {
                    query += "," + WITHOUT_ARRAY_WRAPPER_SUFFIX;
                }
            }

            return query;
        }
    }
}
