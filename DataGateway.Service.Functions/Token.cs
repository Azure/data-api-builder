using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Azure.DataGateway.Service.Functions
{
    public sealed class AadTokenRefreshRequest
    {
        [JsonProperty("aadToken")]
        public string AadToken { get; set; }
    }

    public class Token
    {
        [Function("token/refresh")]
        public static async Task<ActionResult> Run(
            [HttpTrigger(AuthorizationLevel.User, "post", Route = null)] HttpRequestData req)
        {
            string requestBody;
            using (var reader = new StreamReader(req.Body))
            {
                requestBody = await reader.ReadToEndAsync();
            }

            string aadToken = req.Headers.First(x => x.Key == "Authorization").Value.First();
            AadTokenRefreshRequest refreshTokenRequest = JsonConvert.DeserializeObject<AadTokenRefreshRequest>(requestBody);

            // TODO: Re-add this once we have cosmosdbconfig?
            //CosmosDbConfiguration cosmosDbConfiguration = CosmosDbConfiguration.Instance;
            //string aadToken = TokenUtils.GetRawAadToken(aadTokenRefreshRequest.AadToken);
            //if (cosmosDbConfiguration.AadToken.Equals(aadToken))
            //{
            //    return this.Ok();
            //}

            //await cosmosDbConfiguration.RefreshAadToken(cosmosDBCredentialsProvider, aadTokenRefreshRequest);
            return new OkResult();
        }
    }
}
