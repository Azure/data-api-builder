using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using Npgsql;

namespace Azure.DataGateway.Service.Resolvers
{
    /// <summary>
    /// Modifies a query that returns regular rows to return JSON for Postgres
    /// </summary>
    public class PostgresQueryBuilder : IQueryBuilder
    {
        private static DbCommandBuilder _builder = new NpgsqlCommandBuilder();

        public string DataIdent { get; } = "\"data\"";

        public string QuoteIdentifier(string ident)
        {
            return _builder.QuoteIdentifier(ident);
        }

        public string WrapSubqueryColumn(string column, SqlQueryStructure subquery)
        {
            return column;
        }

        public string Build(SqlQueryStructure structure)
        {
            string fromSql = structure.TableSql();
            fromSql += string.Join("", structure.JoinQueries.Select(x => $" LEFT OUTER JOIN LATERAL ({Build(x.Value)}) AS {QuoteIdentifier(x.Key)} ON TRUE"));

            string query = $"SELECT {structure.ColumnsSql()}"
                + $" FROM {fromSql}"
                + $" WHERE {structure.PredicatesSql()}"
                + $" ORDER BY {structure.OrderBySql()}";

            string subqueryName = QuoteIdentifier($"subq{structure.Counter.Next()}");

            string start, end;
            if (structure.IsPaginatedQuery)
            {
                List<string> requestedResults = new();

                if (structure.IsRequestedPaginationResult("nodes"))
                {
                    requestedResults.Add($"COALESCE(jsonb_agg(json_build_object({MakeJsonBuildObjectParams(structure.Columns.Keys.ToList())})), '[]') AS {QuoteIdentifier("nodes")}");
                }

                if (structure.IsRequestedPaginationResult("endCursor"))
                {
                    string primaryKeyField = QuoteIdentifier(structure.PrimaryKey()[0]);
                    requestedResults.Add($"CASE WHEN max({primaryKeyField}) IS NOT NULL THEN"
                                         + $" ENCODE(CONVERT_TO( {structure.PaginationCursorJson()}, 'UTF-8'), 'BASE64')"
                                         + $" ELSE NULL END AS {QuoteIdentifier("endCursor")}");
                }

                if (structure.IsRequestedPaginationResult("hasNextPage"))
                {
                    requestedResults.Add($"COALESCE(max(___rowcount___), 0) > {structure.Limit} AS {QuoteIdentifier("hasNextPage")}");

                    start = $"SELECT *, COUNT(*) OVER() AS ___rowcount___ FROM (";
                    end = $") AS count_wrapper LIMIT {structure.Limit}";

                    query += $" LIMIT {structure.Limit + 1}";
                }
                else
                {
                    start = string.Empty;
                    end = string.Empty;

                    query += $" LIMIT {structure.Limit}";
                }

                start = $"SELECT to_jsonb(paginatedquery) AS {DataIdent} FROM ( SELECT {string.Join(", ", requestedResults)} FROM (" + start;
                end += $") AS {subqueryName} ) AS paginatedquery";
            }
            else
            {
                if (structure.IsListQuery)
                {
                    start = $"SELECT COALESCE(jsonb_agg(to_jsonb({subqueryName})), '[]') AS {DataIdent} FROM (";
                    ;
                }
                else
                {
                    start = $"SELECT to_jsonb({subqueryName}) AS {DataIdent} FROM (";
                }

                end = $") AS {subqueryName}";
                query += $" LIMIT {structure.Limit}";
            }

            query = $"{start} {query} {end}";
            return query;
        }

        /// <summary>
        /// Make params for the json_build_object method of postgres which builds a json object using
        /// json_build_object('fieldName1', fieldName1JsonObj, 'fieldName2', fieldName2JsonObj)
        /// </summary>
        string MakeJsonBuildObjectParams(List<string> columns)
        {
            return string.Join(", ", columns.Select(columnName => $"'{columnName}', {QuoteIdentifier(columnName)}"));
        }
    }
}
