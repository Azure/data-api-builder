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
                + $" WHERE {structure.PredicatesSql(typecast: true)}"
                + $" ORDER BY {structure.OrderBySql()}"
                + $" LIMIT {structure.Limit()}";

            string subqueryName = QuoteIdentifier($"subq{structure.Counter.Next()}");

            string start;
            if (structure.IsListQuery)
            {
                start = $"SELECT COALESCE(jsonb_agg(to_jsonb({subqueryName})), '[]') AS {DataIdent} FROM (";
            }
            else
            {
                start = $"SELECT to_jsonb({subqueryName}) AS {DataIdent} FROM (";
            }

            string end = $") AS {subqueryName}";
            query = $"{start} {query} {end}";
            return query;
        }
    }
}
