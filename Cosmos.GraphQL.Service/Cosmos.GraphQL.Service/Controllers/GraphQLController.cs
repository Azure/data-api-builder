using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.IO;
using System.Text.Json;
using Cosmos.GraphQL.Services;
using Cosmos.GraphQL.Service.Resolvers;


namespace Cosmos.GraphQL.Service.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class GraphQLController : ControllerBase
    {

        string JsonData = @"{'serviceName':'datagateway', 'endpointType':'graphQL'}";

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

        [HttpPost]
        public async Task<JsonDocument> PostAsync()
        {
            string requestBody;
            using (StreamReader reader = new StreamReader(this.HttpContext.Request.Body))
            {
                requestBody = await reader.ReadToEndAsync();
            }
            var resultJson = await this._schemaManager.ExecuteAsync(requestBody);
            return JsonDocument.Parse(resultJson);
        }
    }
}
