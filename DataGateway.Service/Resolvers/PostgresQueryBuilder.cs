using System.Data.Common;
using System.Linq;
using System.Text;
using Npgsql;

namespace Azure.DataGateway.Service.Resolvers
{
    /// <summary>
    /// Modifies a query that returns regular rows to return JSON for Postgres
    /// </summary>
    public class PostgresQueryBuilder : IQueryBuilder
    {
        private static DbCommandBuilder _builder = new NpgsqlCommandBuilder();
        private const string ALL_FIELDS = "*";

        public string QuoteIdentifier(string ident)
        {
            return _builder.QuoteIdentifier(ident);
        }

        public string Build(string inputQuery, bool isList)
        {
            if (!isList)
            {
                return $"SELECT row_to_json(q) FROM ({inputQuery}) q";
            }

            return $"SELECT jsonb_agg(row_to_json(q)) FROM ({inputQuery}) q";
        }

        /// <summary>
        /// Build the PgSql query for the given FindQueryStructure object which
        /// holds the major components of a query.
        /// </summary>
        /// <param name="structure">The query structure holding the properties.</param>
        /// <returns>The formed query text.</returns>
        public string Build(FindQueryStructure structure)
        {
            string selectedColumns = ALL_FIELDS;
            if (structure.Fields.Count > 0)
            {
                selectedColumns = string.Join(", ", structure.Fields.Select(x => $"{QuoteIdentifier(x)}"));
            }

            string fromPart = structure.EntityName;

            var query = new StringBuilder($"SELECT {selectedColumns} FROM {fromPart}");
            if (structure.Conditions.Count() > 0)
            {
                query.Append($" WHERE {string.Join(" AND ", structure.Conditions)}");
            }

            // Call the basic build to add the correct FOR JSON suffixes.
            return Build(query.ToString(), structure.IsListQuery);
        }

    }
}
