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
            IGraphQLMetadataProvider metadataStoreProvider,
            ISqlMetadataProvider sqlMetadataProvider,
            IDictionary<string, object?> mutationParams)
        : base(metadataStoreProvider, sqlMetadataProvider, entityName: entityName)
        {
            DatabaseObject dbObject = sqlMetadataProvider.EntityToDatabaseObject[entityName];
            string schemaName = dbObject.Name;
            string tableName = dbObject.Name;
            TableDefinition tableDefinition = GetUnderlyingTableDefinition();

            List<string> primaryKeys = tableDefinition.PrimaryKey;
            foreach (KeyValuePair<string, object?> param in mutationParams)
            {
                if (param.Value == null)
                {
                    // Should never happen since delete mutations expect non nullable pk params
                    throw new DataGatewayException(
                        $"Unexpected {param.Key} null argument.",
                        HttpStatusCode.BadRequest,
                        DataGatewayException.SubStatusCodes.BadRequest);
                }

                // primary keys used as predicates
                if (primaryKeys.Contains(param.Key))
                {
                    Predicates.Add(new Predicate(
                        new PredicateOperand(new Column(schemaName, tableName, param.Key)),
                        PredicateOperation.Equal,
                        new PredicateOperand($"@{MakeParamWithValue(GetParamAsColumnSystemType(param.Value.ToString()!, param.Key))}")
                    ));
                }
            }
        }
    }
}
