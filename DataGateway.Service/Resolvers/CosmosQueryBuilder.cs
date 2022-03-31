using System.Linq;
using System.Text;
using Azure.DataGateway.Service.Models;

namespace Azure.DataGateway.Service.Resolvers
{
    public class CosmosQueryBuilder : BaseSqlQueryBuilder
    {
        private readonly string _containerAlias = "c";

        /// <summary>
        /// Builds a cosmos sql query string
        /// </summary>
        /// <param name="structure"></param>
        /// <returns></returns>
        public string Build(CosmosQueryStructure structure)
        {
            StringBuilder queryStringBuilder = new();
            queryStringBuilder.Append($"SELECT {WrappedColumns(structure)}"
                + $" FROM {_containerAlias}");
            string predicateString = Build(structure.Predicates);
            if (!string.IsNullOrEmpty(predicateString))
            {
                queryStringBuilder.Append($" WHERE {predicateString}");
            }

            return queryStringBuilder.ToString();
        }

        // bool to match signature for override
        protected override string Build(Column column, bool printDirection = true)
        {
            return _containerAlias + "." + column.ColumnName;
        }

        protected override string Build(KeysetPaginationPredicate? predicate)
        {
            // Cosmos doesnt do keyset pagination
            return string.Empty;
        }

        protected override string QuoteIdentifier(string ident)
        {
            throw new System.NotImplementedException();
        }

        /// <summary>
        /// Build columns and wrap columns
        /// </summary>
        private string WrappedColumns(CosmosQueryStructure structure)
        {
            return string.Join(", ",
                structure.Columns.Select(
                    c => _containerAlias + "." + c.Label
            ));
        }

    }
}
