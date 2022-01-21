using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using Azure.DataGateway.Service.Exceptions;
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
            try
            {
                FieldValuePairsInBody = JsonSerializer.Deserialize<Dictionary<string, object>>(insertPayloadRoot.ToString());
            }
            catch(ArgumentNullException)
            {
                FieldValuePairsInBody = new();
            }
            catch(JsonException ex)
            {
                throw new DatagatewayException(
                    message: ex.Message,
                    statusCode: (int)HttpStatusCode.BadRequest,
                    subStatusCode: DatagatewayException.SubStatusCodes.BadRequest);
            }

            // We don't support InsertMany as yet.
            IsMany = false;
        }
    }
}
