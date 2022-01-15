using System.Collections.Generic;
using System.Text.Json;
using Azure.DataGateway.Service.Models;

namespace Azure.DataGateway.Service.Resolvers
{
    public class InsertRequestContext : RequestContext
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        public InsertRequestContext(string entityName, JsonElement insertPayloadRoot, Operation operationType)
        {
            EntityName = entityName;
            Fields = new();
            OperationType = operationType;
            FieldValuePairs = JsonSerializer.Deserialize<Dictionary<string, object>>(insertPayloadRoot.ToString());

            // We don't support InsertMany as yet.
            IsMany = false;
        }
    }
}
