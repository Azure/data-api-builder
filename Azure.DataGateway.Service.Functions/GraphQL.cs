using Azure.DataGateway.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

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
