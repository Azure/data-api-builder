using System.Collections.Generic;
using System.Net;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Services;

namespace Azure.DataGateway.Service.Resolvers
{
    ///<summary>
    /// Wraps all the required data and logic to write a SQL DELETE query
    ///</summary>
    public class SqlDeleteStructure : BaseSqlQueryStructure
    {
        public SqlDeleteStructure(
            string entityName,
            ISqlMetadataProvider sqlMetadataProvider,
            IDictionary<string, object?> mutationParams)
        : base(sqlMetadataProvider, entityName: entityName)
        {
            TableDefinition tableDefinition = GetUnderlyingTableDefinition();

            List<string> primaryKeys = tableDefinition.PrimaryKey;
            foreach (KeyValuePair<string, object?> param in mutationParams)
            {
                SqlMetadataProvider.TryGetBackingColumn(EntityName, param.Key, out string? backingColumn);
                if (param.Value is null)
                {
                    // Should never happen since delete mutations expect non nullable pk params
                    throw new DataGatewayException(
                        message: $"Unexpected {param.Key} null argument.",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataGatewayException.SubStatusCodes.BadRequest);
                }

                // primary keys used as predicates
                if (primaryKeys.Contains(backingColumn!))
                {
                    Predicates.Add(new Predicate(
                        new PredicateOperand(new Column(DatabaseObject.SchemaName, DatabaseObject.Name, backingColumn!)),
                        PredicateOperation.Equal,
                        new PredicateOperand($"@{MakeParamWithValue(GetParamAsColumnSystemType(param.Value.ToString()!, backingColumn!))}")
                    ));
                }
            }
        }
    }
}
