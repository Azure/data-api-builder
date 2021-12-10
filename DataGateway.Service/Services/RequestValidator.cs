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
        /// <exception cref="PrimaryKeyValidationException"></exception>
        public static void ValidateFindRequest(FindRequestContext context, IMetadataStoreProvider configurationProvider)
        {
            TableDefinition tableDefinition = configurationProvider.GetTableDefinition(context.EntityName);
            if (tableDefinition == null)
            {
                throw new PrimaryKeyValidationException(message: "TableDefinition for Entity:" + context.EntityName + " does not exist.");
            }

            int primaryKeysInSchema = tableDefinition.PrimaryKey.Count;
            int primaryKeysInRequest = context.Predicates.Count;

            if (primaryKeysInRequest == 0)
            {
                throw new PrimaryKeyValidationException(message: "Primary Key must be provided in request");
            }

            if (primaryKeysInRequest != primaryKeysInSchema)
            {
                throw new PrimaryKeyValidationException(message: "Primary key column(s) provided do not match DB schema.");
            }

            //Each Predicate (Column) that is checked against the DB schema
            //is added to a list of validated columns. If a column has already
            //been checked and comes up again, the request contains duplicates.
            List<string> validatedColumns = new();
            foreach (RestPredicate predicate in context.Predicates)
            {
                if (validatedColumns.Contains(predicate.Field))
                {
                    throw new PrimaryKeyValidationException(message: "Primary Key field: " + predicate.Field + " appears more than once in the request.");

                }

                if (!tableDefinition.PrimaryKey.Contains(predicate.Field))
                {
                    throw new PrimaryKeyValidationException(message: "Primary Key field: " + predicate.Field + " is invalid.");
                }
                else
                {
                    validatedColumns.Add(predicate.Field);
                }
            }
        }
    }
}
