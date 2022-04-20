using System.Collections.Generic;
using System.Linq;
using System.Net;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.GraphQLBuilder.Mutations;
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
                Predicate predicate;
                if (param.Value == null && !tableDefinition.Columns[param.Key].IsNullable)
                {
                    throw new DataGatewayException(
                        $"Cannot set argument {param.Key} to null.",
                        HttpStatusCode.BadRequest,
                        DataGatewayException.SubStatusCodes.BadRequest);
                }
                else if (param.Value == null)
                {
                    predicate = new(
                        new PredicateOperand(new Column(tableAlias: null, param.Key)),
                        PredicateOperation.Equal,
                        new PredicateOperand($"@{MakeParamWithValue(null)}")
                    );
                }
                else
                {
                    predicate = new(
                        new PredicateOperand(new Column(null, param.Key)),
                        PredicateOperation.Equal,
                        new PredicateOperand($"@{MakeParamWithValue(GetParamAsColumnSystemType(param.Value.ToString()!, param.Key))}")
                    );
                }

                // primary keys used as predicates
                if (primaryKeys.Contains(param.Key))
                {
                    Predicates.Add(new(
                        new PredicateOperand(new Column(null, param.Key)),
                        PredicateOperation.Equal,
                        new PredicateOperand($"@{MakeParamWithValue(param.Value)}")
                    ));
                }
                // Unpack the input argument type as columns to update
                else if (param.Key == UpdateMutationBuilder.INPUT_ARGUMENT_NAME)
                {
                    IDictionary<string, object?> updateFields = ArgumentToDictionary(mutationParams, UpdateMutationBuilder.INPUT_ARGUMENT_NAME);

                    foreach (KeyValuePair<string, object?> field in updateFields)
                    {
                        if (columns.Contains(field.Key))
                        {
                            UpdateOperations.Add(new(
                                new PredicateOperand(new Column(null, field.Key)),
                                PredicateOperation.Equal,
                                new PredicateOperand($"@{MakeParamWithValue(field.Value)}")
                            ));
                        }
                    }

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
