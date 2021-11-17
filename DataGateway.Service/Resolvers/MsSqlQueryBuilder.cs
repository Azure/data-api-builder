using Microsoft.Data.SqlClient;
using System.Data.Common;
using System.Linq;
using System.Text;

namespace Azure.DataGateway.Service.Resolvers
{
    /// <summary>
    /// Modifies a query that returns regular rows to return JSON for MSSQL.
    /// </summary>
    public class MsSqlQueryBuilder : IQueryBuilder
    {
        private const string FOR_JSON_SUFFIX = " FOR JSON PATH, INCLUDE_NULL_VALUES";
        private const string WITHOUT_ARRAY_WRAPPER_SUFFIX = "WITHOUT_ARRAY_WRAPPER";

        private static DbCommandBuilder _builder = new SqlCommandBuilder();

        public string QuoteIdentifier(string ident)
        {
            return _builder.QuoteIdentifier(ident);
        }

        public string Build(string inputQuery, bool isList)
        {
            string queryText = inputQuery + FOR_JSON_SUFFIX;
            if (!isList)
            {
                queryText += "," + WITHOUT_ARRAY_WRAPPER_SUFFIX;
            }

            return queryText;
        }

        /// <summary>
        /// Build the MsSql query for the given QueryStructure object which
        /// holds the major components of a query.
        /// </summary>
        /// <param name="structure">The query structure holding the properties.</param>
        /// <returns>The formed query text.</returns>
        public string Build(QueryStructure structure)
        {
            string selectedColumns = string.Join(", ", structure.Fields.Select(x => $"{x}"));
            string fromPart = structure.EntityName;

            var query = new StringBuilder($"SELECT {selectedColumns} FROM {fromPart}");

            if (structure.Conditions.Count() > 0)
            {
                query.Append($" WHERE {string.Join(" AND ", structure.Conditions)}");
            }

            query.Append(FOR_JSON_SUFFIX);
            return query.ToString();
        }
    }
}
