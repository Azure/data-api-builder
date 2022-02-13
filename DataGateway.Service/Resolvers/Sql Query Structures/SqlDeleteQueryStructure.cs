using System.Collections.Generic;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Services;

namespace Azure.DataGateway.Service.Resolvers
{
    ///<summary>
    /// Wraps all the required data and logic to write a SQL DELETE query
    ///</summary>
    public class SqlDeleteStructure : BaseSqlQueryStructure
    {
        public SqlDeleteStructure(string tableName, IMetadataStoreProvider metadataStore, IDictionary<string, object> mutationParams)
        : base(metadataStore, tableName)
        {
            TableDefinition tableDefinition = GetTableDefinition();

            List<string> primaryKeys = tableDefinition.PrimaryKey;
            foreach (KeyValuePair<string, object> param in mutationParams)
            {
                if (param.Value == null)
                {
                    continue;
                }

                // primary keys used as predicates
                if (primaryKeys.Contains(param.Key))
                {
                    Predicates.Add(new Predicate(
                        new PredicateOperand(new Column(TableName, param.Key)),
                        PredicateOperation.Equal,
                        new PredicateOperand($"@{MakeParamWithValue(GetParamAsColumnSystemType(param.Value.ToString()!, param.Key))}")
                    ));
                }
            }
        }
    }
}
