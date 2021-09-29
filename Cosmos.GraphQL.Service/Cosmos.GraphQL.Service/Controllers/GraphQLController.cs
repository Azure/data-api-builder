using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.IO;
using System.Text.Json;
using Cosmos.GraphQL.Services;
using Cosmos.GraphQL.Service.Resolvers;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using System.Net;

namespace Cosmos.GraphQL.Service.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class GraphQLController : ControllerBase
    {
        private readonly IQueryEngine _queryEngine;
        private readonly IMutationEngine _mutationEngine;

        private readonly ILogger<GraphQLController> _logger;
        private readonly GraphQLService _schemaManager;

        public GraphQLController(ILogger<GraphQLController> logger, IQueryEngine queryEngine, IMutationEngine mutationEngine, GraphQLService schemaManager)
        {
            _logger = logger;
            _queryEngine = queryEngine;
            _mutationEngine = mutationEngine;
            _schemaManager = schemaManager;
        }

        [Function("query")]
        public async System.Threading.Tasks.Task<HttpResponseData> QueryAsync([HttpTrigger(AuthorizationLevel.Anonymous, "post")]
            HttpRequestData req,
            FunctionContext executionContext)
        {
            string requestBody;
            using(StreamReader reader = new StreamReader(req.Body))
            {
                requestBody = await reader.ReadToEndAsync();
            }

            string resultJson = await _schemaManager.ExecuteAsync(requestBody);

            HttpResponseData response = req.CreateResponse(HttpStatusCode.OK);
            response.WriteString(resultJson);

            return response;
        }
    }
}
