using System.Text.Json;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace Azure.DataGateway.Service.Models.RestRequestContexts
{
    public class UpdateRequestContext : UpsertRequestContext
    {
        public UpdateRequestContext(
            string entityName,
            JsonElement insertPayloadRoot,
            OperationAuthorizationRequirement httpVerb,
            Operation operationType)
            : base(entityName, insertPayloadRoot, httpVerb, operationType)
        {
        }
    }
}
