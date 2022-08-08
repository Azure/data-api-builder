using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Exceptions;

namespace Azure.DataApiBuilder.Service.Models
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
            DatabaseObject dbo,
            JsonElement insertPayloadRoot,
            Operation operationType)
            : base(entityName, dbo)
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
                    throw new DataApiBuilderException(
                        message: "The request body is not in a valid JSON format.",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
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

