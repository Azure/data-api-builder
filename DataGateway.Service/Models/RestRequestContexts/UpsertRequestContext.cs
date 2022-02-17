using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using Azure.DataGateway.Service.Exceptions;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace Azure.DataGateway.Service.Models
{
    /// <summary>
    /// UpsertRequestContext provides the major components of a REST query
    /// corresponding to the PutOne operations.
    /// </summary>
    public class UpsertRequestContext : RestRequestContext
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        public UpsertRequestContext(
            string entityName,
            JsonElement insertPayloadRoot,
            OperationAuthorizationRequirement httpVerb,
            Operation operationType)
            : base(httpVerb, entityName)
        {
            FieldsToBeReturned = new();
            PrimaryKeyValuePairs = new();
            OperationType = operationType;
            if (!string.IsNullOrEmpty(insertPayloadRoot.ToString()))
            {
                try
                {
                    FieldValuePairsInBody = JsonSerializer.Deserialize<Dictionary<string, object>>(insertPayloadRoot.ToString()!)!;
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

            // We don't support UpsertMany as yet.
            IsMany = false;
        }
    }
}

