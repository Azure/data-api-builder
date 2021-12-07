using System;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Resolvers;
using Azure.DataGateway.Services;

namespace Azure.DataGateway.Service.Services
{
    public class RequestValidator
    {
        public static bool IsValidFindRequest(FindRequestContext context, IMetadataStoreProvider configurationProvider)
        {
            if (context == null)
            {
                throw new ArgumentNullException(paramName: context.GetType().ToString(), message: "Context can't be null.");
            }

            if (configurationProvider == null)
            {
                throw new ArgumentNullException(paramName: configurationProvider.GetType().ToString(), message: "configurationProvider can't be null.");
            }

            // Invalid until proven valid. 
            bool validFindRequest = false;
            TableDefinition tableDefinition = configurationProvider.GetTableDefinition(context.EntityName);
            if (tableDefinition != null)
            {
                int primaryKeysInSchema = tableDefinition.PrimaryKey.Count;
                int primaryKeysInRequest = context.Predicates.Count;

                //Mismatched count means invalid usage of composite PrimaryKey
                if (primaryKeysInRequest > 0 && primaryKeysInRequest == primaryKeysInSchema)
                {
                    bool validPrimaryKeyAlignment = true;
                    foreach (RestPredicate predicate in context.Predicates)
                    {
                        if (!tableDefinition.PrimaryKey.Contains(predicate.Field))
                        {
                            validPrimaryKeyAlignment = false;
                            break;
                        }
                    }

                    if (validPrimaryKeyAlignment)
                    {
                        validFindRequest = true;
                    }
                }
            }

            return validFindRequest;
        }
    }
}
