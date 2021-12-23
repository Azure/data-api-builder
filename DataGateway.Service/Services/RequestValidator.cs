using System.Collections.Generic;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Resolvers;
using Azure.DataGateway.Services;

namespace Azure.DataGateway.Service.Services
{
    /// <summary>
    /// Class which validates input supplied by a REST request.
    /// </summary>
    public class RequestValidator
    {
        /// <summary>
        /// Validates a FindOne request by ensuring each supplied primary key column
        /// exactly matches the DB schema.
        /// </summary>
        /// <param name="context">Request context containing request primary key columns and values</param>
        /// <param name="configurationProvider">Configuration provider that enables referencing DB schema in config.</param>
        /// <exception cref="DatagatewayException"></exception>
        public static void ValidateFindRequest(FindRequestContext context, IMetadataStoreProvider configurationProvider)
        {
            TableDefinition tableDefinition = configurationProvider.GetTableDefinition(context.EntityName);
            if (tableDefinition == null)
            {
                throw new DatagatewayException(message: "TableDefinition for Entity:" + context.EntityName + " does not exist.", statusCode: 400, DatagatewayException.SubStatusCodes.BadRequest);
            }

            foreach (string field in context.Fields)
            {
                if (!tableDefinition.Columns.ContainsKey(field))
                {
                    throw new DatagatewayException(message: "Invalid Column name" + field, statusCode: 400, DatagatewayException.SubStatusCodes.BadRequest);
                }
            }

            int primaryKeysInSchema = tableDefinition.PrimaryKey.Count;
            int primaryKeysInRequest = context.Predicates.Count;

            if (primaryKeysInRequest == 0)
            {
                // FindMany request, further primary key validation not required
                return;
            }

            if (primaryKeysInRequest != primaryKeysInSchema)
            {
                throw new DatagatewayException(message: "Primary key column(s) provided do not match DB schema.", statusCode: 400, DatagatewayException.SubStatusCodes.BadRequest);
            }

            //Each Predicate (Column) that is checked against the DB schema
            //is added to a list of validated columns. If a column has already
            //been checked and comes up again, the request contains duplicates.
            HashSet<string> validatedColumns = new();
            foreach (RestPredicate predicate in context.Predicates)
            {
                if (validatedColumns.Contains(predicate.Field))
                {
                    throw new DatagatewayException(message: "The request is invalid.", statusCode: 400, DatagatewayException.SubStatusCodes.BadRequest);

                }

                if (!tableDefinition.PrimaryKey.Contains(predicate.Field))
                {
                    throw new DatagatewayException(message: "The request is invalid.", statusCode: 400, DatagatewayException.SubStatusCodes.BadRequest);
                }
                else
                {
                    validatedColumns.Add(predicate.Field);
                }
            }
        }
    }
}
