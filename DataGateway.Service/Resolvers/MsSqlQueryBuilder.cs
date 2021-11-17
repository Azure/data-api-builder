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

        public string Build(string inputQuery, bool isList)
        {
            var queryText = new StringBuilder(inputQuery + FOR_JSON_SUFFIX);
            if (!isList)
            {
                queryText.Append("," + WITHOUT_ARRAY_WRAPPER_SUFFIX);
            }

            return queryText.ToString();
        }

        /// <summary>
        /// Build the MsSql query for the given FindQueryStructure object which
        /// holds the major components of a query.
        /// </summary>
        /// <param name="structure">The query structure holding the properties.</param>
        /// <returns>The formed query text.</returns>
        public string Build(FindQueryStructure structure)
        {
            // Add *
            string selectedColumns = string.Join(", ", structure.Fields.Select(x => $"{x}"));
            string fromPart = structure.EntityName;

            var query = new StringBuilder($"SELECT {selectedColumns} FROM {fromPart}");

            if (structure.Conditions.Count() > 0)
            {
                query.Append($" WHERE {string.Join(" AND ", structure.Conditions)}");
            }

            // Add Without array wrapper suffix
            return Build(query.ToString(), structure.IsList());
        }
    }
}
