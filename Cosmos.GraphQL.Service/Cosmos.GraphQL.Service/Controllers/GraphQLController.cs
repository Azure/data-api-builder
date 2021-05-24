using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cosmos.GraphQL.Service.Models;
using Newtonsoft.Json.Linq;
using System.IO;

namespace Cosmos.GraphQL.Service.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class GraphQLController : ControllerBase
    {

        string JsonData = @"{'serviceName':'cosmos', 'endpointType':'graphQL'}";

        private readonly QueryEngine _queryEngine;
        private readonly ILogger<GraphQLController> _logger;
        private readonly SchemaManager _schemaManager;

        public GraphQLController(ILogger<GraphQLController> logger, QueryEngine queryEngine, SchemaManager schemaManager)
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

        [Route("execute_query")]
        [HttpPost]
        public async Task<object> ExecuteQuery()
        {
            string data;
            using (StreamReader reader = new StreamReader(this.HttpContext.Request.Body))
            {
                data = await reader.ReadToEndAsync();
            }
            return await this._schemaManager.ExecuteAsync(data);
        }
    }
}
