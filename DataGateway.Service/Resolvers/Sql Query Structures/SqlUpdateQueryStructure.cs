using System.Collections.Generic;
using System.Linq;
using System.Net;
using Azure.DataGateway.Config;
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

        public SqlUpdateStructure(
            string entityName,
            IGraphQLMetadataProvider metadataStoreProvider,
            ISqlMetadataProvider sqlMetadataProvider,
            IDictionary<string, object?> mutationParams,
            bool isIncrementalUpdate)
        : base(metadataStoreProvider, sqlMetadataProvider, entityName: entityName)
        {
            UpdateOperations = new();
            TableDefinition tableDefinition = GetUnderlyingTableDefinition();

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
                        new PredicateOperand(new Column(tableSchema: SchemaName, tableName: TableName, param.Key)),
                        PredicateOperation.Equal,
                        new PredicateOperand($"@{MakeParamWithValue(null)}")
                    );
                }
                else
                {
                    predicate = new(
                        new PredicateOperand(new Column(tableSchema: SchemaName, tableName: TableName, param.Key)),
                        PredicateOperation.Equal,
                        new PredicateOperand($"@{MakeParamWithValue(GetParamAsColumnSystemType(param.Value.ToString()!, param.Key))}")
                    );
                }

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
