using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Azure.DataGateway.Service.Controllers
{
    public sealed class BindRequest
    {
        [JsonProperty("cosmosEndpoint")]
        public string CosmosEndpoint { get; set; }

        [JsonProperty("dbAccountName")]
        public string DbAccountName { get; set; }

        [JsonProperty("subscriptionId")]
        public string SubscriptionId { get; set; }

        [JsonProperty("resourceGroup")]
        public string ResourceGroup { get; set; }

        [JsonProperty("sessionToken")]
        public string SessionToken { get; set; }

        [JsonProperty("userId")]
        public string UserId { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("graphqlschema")]
        public string GraphQLSchema { get; set; }

        [JsonProperty("databaseConfig")]
        public string DatabaseConfig { get; set; }
    }

    public sealed class BindResponse
    {
        [JsonProperty("sessionToken")]
        public string SessionToken { get; set; }

        [JsonProperty("allocationTime")]
        public DateTime AllocationTime { get; set; }
    }
}
