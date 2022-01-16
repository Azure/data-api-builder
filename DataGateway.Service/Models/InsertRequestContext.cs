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
            Fields = new();
            HttpVerb = httpVerb;
            OperationType = operationType;
            if (!string.IsNullOrEmpty(insertPayloadRoot.ToString()))
            {
                FieldValuePairs = JsonSerializer.Deserialize<Dictionary<string, object>>(insertPayloadRoot.ToString());
            }
            else
            {
                FieldValuePairs = new();
            }

            // We don't support InsertMany as yet.
            IsMany = false;
        }
    }
}
