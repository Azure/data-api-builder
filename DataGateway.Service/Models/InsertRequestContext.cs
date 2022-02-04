using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using Azure.DataGateway.Service.Exceptions;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace Azure.DataGateway.Service.Models
{
    /// <summary>
    /// InsertRequestContext provides the major components of a REST query
    /// corresponding to the InsertOne or InsertMany operations.
    /// </summary>
    public class InsertRequestContext : RestRequestContext
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
            FieldValuePairsInUrl = new();
            HttpVerb = httpVerb;
            OperationType = operationType;
            if (!string.IsNullOrEmpty(insertPayloadRoot.ToString()))
            {
                try
                {
                    FieldValuePairsInBody = JsonSerializer.Deserialize<Dictionary<string, object>>(insertPayloadRoot.ToString());
                }
                catch (JsonException)
                {
                    throw new DatagatewayException(
                        message: "The request body is not in a valid JSON format.",
                        statusCode: (int)HttpStatusCode.BadRequest,
                        subStatusCode: DatagatewayException.SubStatusCodes.BadRequest);
                }
            }
            else
            {
                FieldValuePairsInBody = new();
            }

            // We don't support InsertMany as yet.
            IsMany = false;
        }
    }
}
