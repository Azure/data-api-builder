using Azure.DataGateway.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Azure.DataGateway.Service.Functions
{
    /// <summary>
    /// This hosts the function that runs when we send a request to /graphql.
    /// The equivalent of the GraphQLController in the DotNetCore project.
    /// </summary>
    public class GraphQL
    {
        private readonly GraphQLService _schemaManager;
        public GraphQL(GraphQLService schemaManager)
        {
            _schemaManager = schemaManager;
        }

        /// <summary>
        /// This is where Post requests to /graphql are routed, it is the equivalent
        /// of "GraphQLController.PostAsync" in the DotNetCore project.
        /// It reads the body and calls into the GraphQLService to process the request.
        /// </summary>
        /// <param name="req">The request data.</param>
        /// <returns>The result of the graphql request.</returns>
        [Function("graphql")]
        public async Task<JsonDocument> Run(
            [HttpTrigger(AuthorizationLevel.User, "post", Route = null)] HttpRequestData req)
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
