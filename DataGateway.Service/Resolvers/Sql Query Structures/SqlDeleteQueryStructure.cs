using System.Collections.Generic;
using Azure.DataGateway.Service.Models;

namespace Azure.DataGateway.Service.Resolvers
{
    ///<summary>
    /// Wraps all the required data and logic to write a SQL DELETE query
    ///</summary>
    public class SqlDeleteStructure : BaseSqlQueryStructure
    {
        private readonly TableDefinition _tableDefinition;
        public SqlDeleteStructure(string tableName, TableDefinition tableDefinition, IDictionary<string, object> mutationParams)
        : base()
        {
            TableName = tableName;
            _tableDefinition = tableDefinition;

            List<string> primaryKeys = _tableDefinition.PrimaryKey;
            foreach (KeyValuePair<string, object> param in mutationParams)
            {
                if (param.Value == null)
                {
                    continue;
                }

                // primary keys used as predicates
                if (primaryKeys.Contains(param.Key))
                {
                    // Predicates.Add($"{QuoteIdentifier(param.Key)} = @{MakeParamWithValue(param.Value)}");
                    Predicates.Add(new Predicate(
                        new PredicateOperand(new Column(TableName, param.Key)),
                        new PredicateOperand($"@{MakeParamWithValue(param.Value)}"),
                        PredicateOperation.Equals
                    ));
                }
            }
        }
    }
}
