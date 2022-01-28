using System.Collections.Generic;
using System.Linq;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Services;

namespace Azure.DataGateway.Service.Resolvers
{
    ///<summary>
    /// Wraps all the required data and logic to write a SQL UPDATE query
    ///</summary>
    public class SqlUpdateStructure : BaseSqlQueryStructure
    {
        /// <summary>
        /// Updates to be applied to selected row
        /// </summary>
        public List<Predicate> UpdateOperations { get; }

        /// <summary>
        /// The updated columns that the update will return
        /// </summary>
        public List<string> ReturnColumns { get; }

        public SqlUpdateStructure(string tableName, IMetadataStoreProvider metadataStore, IDictionary<string, object> mutationParams)
        : base(metadataStore)
        {
            TableName = tableName;
            UpdateOperations = new();
            TableDefinition tableDefinition = GetTableDefinition();

            ReturnColumns = tableDefinition.PrimaryKey;

            List<string> primaryKeys = tableDefinition.PrimaryKey;
            List<string> columns = tableDefinition.Columns.Keys.ToList();
            foreach (KeyValuePair<string, object> param in mutationParams)
            {
                if (param.Value == null)
                {
                    continue;
                }

                Predicate predicate = new(
                    new PredicateOperand(new Column(null, param.Key)),
                    PredicateOperation.Equal,
                    new PredicateOperand($"@{MakeParamWithValue(param.Value)}")
                );

                // primary keys used as predicates
                if (primaryKeys.Contains(param.Key))
                {
                    Predicates.Add(predicate);
                }
                // use columns to determine values to edit
                else if (columns.Contains(param.Key))
                {
                    UpdateOperations.Add(predicate);
                }
            }

            if (UpdateOperations.Count == 0)
            {
                throw new DatagatewayException("Update mutation does not update any values", 400, DatagatewayException.SubStatusCodes.BadRequest);
            }
        }
    }
}
