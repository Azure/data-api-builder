using System;
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
        // TODO: Remove this once REST uses the schema defined in the config.
        private const string ALL_FIELDS = "*";

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
            string selectedColumns = ALL_FIELDS;
            if (structure.Columns.Count > 0)
            {
                selectedColumns = string.Join(", ", structure.Columns.Select(x => $"{x.Value} AS {QuoteIdentifier(x.Key)}"));
            }

            Console.WriteLine($"selectedColumns: {selectedColumns}");
            string fromPart = structure.Table(structure.TableName, structure.TableAlias);
            fromPart += string.Join("", structure.JoinQueries.Select(x => $" LEFT OUTER JOIN LATERAL ({Build(x.Value)}) AS {QuoteIdentifier(x.Key)} ON TRUE"));
            string query = $"SELECT {selectedColumns} FROM {fromPart}";
            if (structure.Conditions.Count() > 0)
            {
                query += $" WHERE {string.Join(" AND ", structure.Conditions)}";
            }

            query += $" LIMIT {structure.Limit()}";

            string subqueryName = QuoteIdentifier($"subq{structure.Counter.Next()}");

            IQueryBuilder queryBuilder = this;
            string start;
            if (structure.IsListQuery)
            {
                start = $"SELECT COALESCE(jsonb_agg(to_jsonb({subqueryName})), '[]') AS {queryBuilder.DataIdent()} FROM (";
            }
            else
            {
                start = $"SELECT to_jsonb({subqueryName}) AS {queryBuilder.DataIdent()} FROM (";
            }

            string end = $") AS {subqueryName}";
            query = $"{start} {query} {end}";
            return query;
        }
    }
}
