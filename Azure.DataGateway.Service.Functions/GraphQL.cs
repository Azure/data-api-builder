using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Azure.DataGateway.Services;
using System.Text.Json;

namespace Azure.DataGateway.Service.Functions
{
    public class GraphQL
    {
        private readonly GraphQLService _schemaManager;
        public GraphQL(GraphQLService schemaManager)
        {
            _schemaManager = schemaManager;
        }

        [Function("GraphQL")]
        public async Task<JsonDocument> Run(
            [HttpTrigger(AuthorizationLevel.User, "post", Route = null)] HttpRequestData req,
            ILogger log)
        {
            string requestBody;
            using (var reader = new StreamReader(req.Body))
            {
                requestBody = await reader.ReadToEndAsync();
            }

            string resultJson = await _schemaManager.ExecuteAsync(requestBody);

            return JsonDocument.Parse(resultJson);
        }
    }
}
