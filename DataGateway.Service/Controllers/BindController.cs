using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
    }

    public sealed class BindResponse
    {
        [JsonProperty("sessionToken")]
        public string SessionToken { get; set; }

        [JsonProperty("allocationTime")]
        public DateTime AllocationTime { get; set; }
    }

    [Route("api/[controller]")]
    [ApiController]
    public sealed class BindController : ControllerBase
    {
        //private readonly CosmosDBCredentialsProvider cosmosDBCredentialsProvider;

        public BindController(/*CosmosDBCredentialsProvider cosmosDBCredentialsProvider*/)
        {
            //this.cosmosDBCredentialsProvider = cosmosDBCredentialsProvider;
        }

        [HttpPost]
        public async Task<BindResponse> BindAsync([FromBody] BindRequest bindRequest, [FromHeader(Name = "Authorization")] string aadToken)
        {
            //CosmosDbConfiguration cosmosDbConfiguration = CosmosDbConfiguration.Instance;
            //if (cosmosDbConfiguration.CosmosEndpoint != null)
            //{
            //    throw new Exception("Container is already bound.");
            //}

            //await cosmosDbConfiguration.InitializeAsync(cosmosDBCredentialsProvider, bindRequest, aadToken);
            //ContainerMetadata.Instance.AllocatedTime = DateTime.UtcNow;
            //ContainerMetadata.Instance.SessionToken = bindRequest.SessionToken;
            //ContainerMetadata.Instance.LastHeartbeatTime = DateTime.UtcNow;
            //return new BindResponse { SessionToken = ContainerMetadata.Instance.SessionToken, AllocationTime = ContainerMetadata.Instance.AllocatedTime };
            return new BindResponse { SessionToken = bindRequest.SessionToken, AllocationTime = DateTime.Now };
        }
    }
}
