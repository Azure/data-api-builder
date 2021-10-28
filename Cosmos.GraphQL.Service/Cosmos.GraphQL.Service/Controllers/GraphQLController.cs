using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Cosmos.GraphQL.Services;
using Cosmos.GraphQL.Service.Resolvers;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Cosmos.GraphQL.Service.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class GraphQLController : ControllerBase
    {
        private readonly string _jsonData = @"{'serviceName':'datagateway', 'endpointType':'graphQL'}";

        private readonly IQueryEngine _queryEngine;
        private readonly IMutationEngine _mutationEngine;

        private readonly ILogger<GraphQLController> _logger;
        private readonly GraphQLService _schemaManager;

        public GraphQLController(ILogger<GraphQLController> logger, IQueryEngine queryEngine, IMutationEngine mutationEngine, GraphQLService schemaManager)
        {
            //_logger = logger;
            //_queryEngine = queryEngine;
            //_mutationEngine = mutationEngine;
            _schemaManager = schemaManager;
        }

        [HttpPost]
        public async Task<JsonDocument> PostAsync()
        {
            string requestBody;
            using (StreamReader reader = new StreamReader(this.HttpContext.Request.Body))
            {
                requestBody = await reader.ReadToEndAsync();
            }

            string resultJson = await this._schemaManager.ExecuteAsync(requestBody);
            return JsonDocument.Parse(resultJson);
        }
    }
}
