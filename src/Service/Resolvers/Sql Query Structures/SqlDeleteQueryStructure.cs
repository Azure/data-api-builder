using System;
using System.Collections.Generic;
using System.Net;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Models;
using Azure.DataApiBuilder.Service.Services;

namespace Azure.DataApiBuilder.Service.Resolvers
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

            try
            {
                List<string> primaryKeys = tableDefinition.PrimaryKey;
                foreach (KeyValuePair<string, object?> param in mutationParams)
                {
                    if (param.Value is null)
                    {
                        // Should never happen since delete mutations expect non nullable pk params
                        throw new DataApiBuilderException(
                            message: $"Unexpected {param.Key} null argument.",
                            statusCode: HttpStatusCode.BadRequest,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                    }

                    // primary keys used as predicates
                    SqlMetadataProvider.TryGetBackingColumn(EntityName, param.Key, out string? backingColumn);
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
            catch (ArgumentException ex)
            {
                // ArgumentException thrown from GetParamAsColumnSystemType()
                throw new DataApiBuilderException(
                    message: ex.Message,
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }
        }
    }
}
