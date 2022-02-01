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
        : base(metadataStore)
        {
            TableName = tableName;
            foreach (KeyValuePair<string, object> param in mutationParams)
            {
                Predicates.Add(new Predicate(
                    new PredicateOperand(new Column(TableName, param.Key)),
                    PredicateOperation.Equal,
                    new PredicateOperand($"@{MakeParamWithValue(GetParamAsColumnSystemType(param.Value.ToString(), param.Key))}")
                ));
            }
        }
    }
}
