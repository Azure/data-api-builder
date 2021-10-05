using System;
using System.Linq;
using System.Data.Common;
using Npgsql;

namespace Cosmos.GraphQL.Service.Resolvers
{
    /// <summary>
    /// Modifies a query that returns regular rows to return JSON for Postgres
    /// </summary>
    public class PostgresQueryBuilder : IQueryBuilder
    {
        private static DbCommandBuilder Builder = new NpgsqlCommandBuilder();
        public string QuoteIdentifier(string ident)
        {
            return Builder.QuoteIdentifier(ident);
        }

        public string WrapSubqueryColumn(string column, SqlQueryStructure subquery)
        {
            return column;
        }

        public string Build(SqlQueryStructure structure)
        {
            var selectedColumns = String.Join(", ", structure.Columns.Select(x => $"{x.Value} AS {QuoteIdentifier(x.Key)}"));
            string fromPart = structure.Table(structure.TableName, structure.TableAlias);
            fromPart += String.Join("", structure.JoinQueries.Select(x => $" LEFT OUTER JOIN LATERAL ({Build(x.Value)}) AS {QuoteIdentifier(x.Key)} ON TRUE"));
            string query = $"SELECT {selectedColumns} FROM {fromPart}";
            if (structure.Conditions.Count() > 0)
            {
                query += $" WHERE {String.Join(" AND ", structure.Conditions)}";
            }
            var subqName = QuoteIdentifier($"subq{structure.Counter.Next()}");
            string start;
            if (structure.IsList())
            {
                start = $"SELECT COALESCE(jsonb_agg(to_jsonb({subqName})), '[]') AS {structure.DataIdent} FROM (";
            }
            else
            {
                start = $"SELECT to_jsonb({subqName}) AS {structure.DataIdent} FROM (";
            }
            var end = $") AS {subqName}";
            query = $"{start} {query} {end}";
            return query;
        }
    }
}
