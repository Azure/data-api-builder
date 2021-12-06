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
            //validate primary key matches
            //DatabaseSchema is a Dictionary of TableDefinition entries
            ////which contain a PrimaryKey list.
            ///1) from the FindRequestContext, get the table/entityName that is being queried
            ///2) validate that the entityName exists in the Database Schema
            ///3) ensure the PrimaryKey from FRC, which is defined in FRC.Conditions list,
            ///         exists in the DatabaseSchema>TableDefinition PrimaryKey list
            ///4) upon primaryKey match failure
            ///         -Keys that aren't Primary Keys
            ///         -Not defining the whole composite key
            ///         -Not defining any primary key values.
            ///         , return false
            ///Edge Cases: context of validation : findOne -> all parts of composite key needed Vs. findMany -> not all composite PrimaryKey are needed
            if ( context == null)
            {
                throw new ArgumentNullException(paramName: context.GetType().ToString(), message: "Context can't be null.");
            }

            if ( configurationProvider == null )
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
                        if(!tableDefinition.PrimaryKey.Contains(predicate.Field))
                        {
                            validPrimaryKeyAlignment = false;
                            break;
                        }
                    }

                    if(validPrimaryKeyAlignment)
                    {
                        validFindRequest = true;
                    }
                }
            }

            return validFindRequest;
        }
    }
}
