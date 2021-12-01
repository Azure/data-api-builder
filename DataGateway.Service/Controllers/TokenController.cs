using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Azure.DataGateway.Service.Controllers
{
    public sealed class AadTokenRefreshRequest
    {
        [JsonProperty("aadToken")]
        public string AadToken { get; set; }
    }

    [Route("api/token")]
    [ApiController]
    public sealed class TokenController : ControllerBase
    {
        //private readonly CosmosDBCredentialsProvider cosmosDBCredentialsProvider;

        public TokenController(/*CosmosDBCredentialsProvider cosmosDBCredentialsProvider*/)
        {
            //this.cosmosDBCredentialsProvider = cosmosDBCredentialsProvider;
        }

        [HttpPost("refresh")]
        public async Task<ActionResult> TokenRefreshAsync([FromBody] AadTokenRefreshRequest aadTokenRefreshRequest)
        {
            //CosmosDbConfiguration cosmosDbConfiguration = CosmosDbConfiguration.Instance;
            //string aadToken = TokenUtils.GetRawAadToken(aadTokenRefreshRequest.AadToken);
            //if (cosmosDbConfiguration.AadToken.Equals(aadToken))
            //{
            //    return this.Ok();
            //}

            //await cosmosDbConfiguration.RefreshAadToken(cosmosDBCredentialsProvider, aadTokenRefreshRequest);
            return this.Ok();
        }
    }
}
