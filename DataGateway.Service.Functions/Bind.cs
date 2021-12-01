using Azure.DataGateway.Service.Controllers;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Azure.DataGateway.Service.Functions
{
    public class Bind
    {
        [Function("bind")]
        public static async Task<BindResponse> Run(
            [HttpTrigger(AuthorizationLevel.User, "post", Route = null)] HttpRequestData req)
        {
            string requestBody;
            using (var reader = new StreamReader(req.Body))
            {
                requestBody = await reader.ReadToEndAsync();
            }

            string aadToken = req.Headers.First(x => x.Key == "Authorization").Value.First();
            BindRequest bindRequest = JsonConvert.DeserializeObject<BindRequest>(requestBody);
            // TODO: redirect to the other project instead of duplicating code.
            return new BindResponse { SessionToken = bindRequest.SessionToken, AllocationTime = DateTime.Now };
        }
    }
}
