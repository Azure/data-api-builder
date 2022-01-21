using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace Azure.DataGateway.Service.Models
{
    public class InsertRequestContext : RequestContext
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        public InsertRequestContext(
            string entityName,
            JsonElement insertPayloadRoot,
            OperationAuthorizationRequirement httpVerb,
            Operation operationType)
        {
            EntityName = entityName;
            FieldsToBeReturned = new();
            PrimaryKeyValuePairs = new();
            HttpVerb = httpVerb;
            OperationType = operationType;
            if (!string.IsNullOrEmpty(insertPayloadRoot.ToString()))
            {
                try
                {
                    FieldValuePairsInBody = JsonSerializer.Deserialize<Dictionary<string, object>>(insertPayloadRoot.ToString());
                }
                catch(ArgumentNullException)
                {
                    FieldValuePairsInBody = new();
                }
                catch(JsonException)
                {
                    throw new DatagatewayException(
                        message: 
                        )
                }
                catch(NotSupportedException)
                {

                }
            }
            else
            {
            }

            // We don't support InsertMany as yet.
            IsMany = false;
        }
    }
}
