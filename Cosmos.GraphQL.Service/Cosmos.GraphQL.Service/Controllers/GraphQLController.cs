using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cosmos.GraphQL.Service.Models;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Text.Json;
using Cosmos.GraphQL.Services;

namespace Cosmos.GraphQL.Service.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class GraphQLController : ControllerBase
    {

        string JsonData = @"{'serviceName':'cosmos', 'endpointType':'graphQL'}";

        private readonly QueryEngine _queryEngine;
        private readonly ILogger<GraphQLController> _logger;
        private readonly GraphQLService _schemaManager;

        public GraphQLController(ILogger<GraphQLController> logger, QueryEngine queryEngine, GraphQLService schemaManager)
        {
            _logger = logger;
            _queryEngine = queryEngine;
            _schemaManager = schemaManager;
        }

        [HttpGet]
        public IEnumerable<JObject> Get()
        {
            JObject data =JObject.Parse(JsonData);
            return Enumerable.Repeat(data, 1);
        }
        
        [Route("executeResolver/{graphQLQueryName?}")]
        [HttpPost]
        public async Task<JsonDocument> execute(string graphQLQueryName)
        {

            var result = await _queryEngine.execute(graphQLQueryName, new Dictionary<string, string>());
            return result;
        }
        
        [Route("addResolver")]
        [HttpPost]
        public void addResolver(GraphQLQueryResolver resolver)
        {
           _queryEngine.registerResolver(resolver);
           _schemaManager.attachQueryResolverToSchema(resolver.GraphQLQueryName);
        }

        [Route("schema")]
        [HttpPost]
        public async void Schema()
        {
            string data;
            using (StreamReader reader = new StreamReader(this.HttpContext.Request.Body))
            {
                data = await reader.ReadToEndAsync();
            }
            if (!String.IsNullOrEmpty(data))
            {
                this._schemaManager.parseAsync(data);
                return;
            }

            throw new InvalidDataException();
        }

        [HttpPost]
        public async Task<string> Post()
        {
            string requestBody;
            using (StreamReader reader = new StreamReader(this.HttpContext.Request.Body))
            {
                requestBody = await reader.ReadToEndAsync();
            }
            return await this._schemaManager.ExecuteAsync(requestBody);
        }
    }
}
