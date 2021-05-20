using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Linq;
using System.Threading.Tasks;
using Cosmos.GraphQL.Service.Models;
using Newtonsoft.Json.Linq;

namespace Cosmos.GraphQL.Service.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class GraphQLController : ControllerBase
    {
        
        
        string JsonData = @"{'serviceName':'cosmos', 'endpointType':'graphQL'}";

        private readonly QueryEngine _queryEngine;
        private readonly ILogger<GraphQLController> _logger;

        public GraphQLController(ILogger<GraphQLController> logger, QueryEngine queryEngine)
        {
            _logger = logger;
            _queryEngine = queryEngine;
        }

        [HttpGet]
        public IEnumerable<JObject> Get()
        {
            JObject data =JObject.Parse(JsonData);
            return Enumerable.Repeat(data, 1);
        }
        
        [Route("executeResolver/{graphQLQueryName?}")]
        [HttpPost]
        public async Task<string> execute(string graphQLQueryName)
        {

            string result = await _queryEngine.execute(graphQLQueryName, new Dictionary<string, string>());
            return result;
        }
        
        [Route("addResolver")]
        [HttpPost]
        public void addResolver(GraphQLQueryResolver resolver)
        {
           _queryEngine.registerResolver(resolver);
        }
    }
}
