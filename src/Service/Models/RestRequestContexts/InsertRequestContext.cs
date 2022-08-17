using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Exceptions;

namespace Azure.DataApiBuilder.Service.Models
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
            DatabaseObject dbo,
            JsonElement insertPayloadRoot,
            Operation operationType)
            : base(entityName, dbo)
        {
            FieldsToBeReturned = new();
            PrimaryKeyValuePairs = new();
            OperationType = operationType;

            string? payload = insertPayloadRoot.ToString();
            if (!string.IsNullOrEmpty(payload))
            {
                try
                {
                    Dictionary<string, object?>? fieldValuePairs = JsonSerializer.Deserialize<Dictionary<string, object?>>(payload);
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
                    try
                    {
                        Dictionary<string, object?>[]? fieldValuePairs = JsonSerializer.Deserialize<Dictionary<string, object?>[]>(payload);
                        throw new DataApiBuilderException(
                            statusCode: HttpStatusCode.BadRequest,
                            message: "Mutation operation on many instances of an entity in a single request are not yet supported.",
                            subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                    }
                    catch (JsonException)
                    {
                        throw new DataApiBuilderException(
                            message: "The request body is not in a valid JSON format.",
                            statusCode: HttpStatusCode.BadRequest,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                    }
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
