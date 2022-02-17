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
            : base(httpVerb, entityName)
        {
            FieldsToBeReturned = new();
            PrimaryKeyValuePairs = new();
            OperationType = operationType;

            string? payload = insertPayloadRoot.ToString();
            if (!string.IsNullOrEmpty(payload))
            {
                try
                {
                    Dictionary<string, object>? fieldValuePairs = JsonSerializer.Deserialize<Dictionary<string, object>>(payload);
                    if (fieldValuePairs != null)
                    {
                        FieldValuePairsInBody = fieldValuePairs;
                    }
                    else
                    {
                        throw new JsonException("Failed to deserialize the insert payload");
                    }
                }
                catch (JsonException)
                {
                    throw new DatagatewayException(
                        message: "The request body is not in a valid JSON format.",
                        statusCode: HttpStatusCode.BadRequest,
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
