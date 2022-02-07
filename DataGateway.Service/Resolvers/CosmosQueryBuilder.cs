using System.Linq;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Resolvers;

namespace Azure.DataGateway.Service
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
            string query = $"SELECT {WrappedColumns(structure)}"
                + $" FROM {_containerAlias}";
            string predicateString = Build(structure.Predicates);
            if (!string.IsNullOrEmpty(predicateString))
            {
                query = query + $" WHERE {predicateString}";
            }

            return query;
        }

        protected override string Build(Column column)
        {
            return _containerAlias + "." + column.ColumnName;
        }

        protected override string Build(KeysetPaginationPredicate predicate)
        {
            return "";
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
