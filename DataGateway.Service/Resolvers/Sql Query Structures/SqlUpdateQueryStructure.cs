using System.Collections.Generic;
using System.Linq;
using System.Net;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Services;

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

        public SqlUpdateStructure(string tableName, SqlGraphQLFileMetadataProvider metadataStore, IDictionary<string, object?> mutationParams, bool isIncrementalUpdate)
        : base(metadataStore, tableName: tableName)
        {
            UpdateOperations = new();
            TableDefinition tableDefinition = GetTableDefinition();

            List<string> primaryKeys = tableDefinition.PrimaryKey;
            List<string> columns = tableDefinition.Columns.Keys.ToList();
            foreach (KeyValuePair<string, object?> param in mutationParams)
            {
                if (param.Value == null)
                {
                    continue;
                }

                Predicate predicate = new(
                    new PredicateOperand(new Column(null, param.Key)),
                    PredicateOperation.Equal,
                    new PredicateOperand($"@{MakeParamWithValue(GetParamAsColumnSystemType(param.Value.ToString()!, param.Key))}")
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

                columns.Remove(param.Key);
            }

            if (!isIncrementalUpdate)
            {
                AddNullifiedUnspecifiedFields(columns, UpdateOperations, tableDefinition);
            }

            if (UpdateOperations.Count == 0)
            {
                throw new DataGatewayException(
                    message: "Update mutation does not update any values",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataGatewayException.SubStatusCodes.BadRequest);
            }
        }
    }
}
