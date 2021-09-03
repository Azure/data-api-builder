﻿using Microsoft.AspNetCore.Mvc;
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
using Cosmos.GraphQL.Service.Resolvers;


namespace Cosmos.GraphQL.Service.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class GraphQLController : ControllerBase
    {

        string JsonData = @"{'serviceName':'datagateway', 'endpointType':'graphQL'}";

        private readonly IQueryEngine _queryEngine;
        private readonly MutationEngine _mutationEngine;

        private readonly ILogger<GraphQLController> _logger;
        private readonly GraphQLService _schemaManager;

        public GraphQLController(ILogger<GraphQLController> logger, IQueryEngine queryEngine, MutationEngine mutationEngine, GraphQLService schemaManager)

        {
            _logger = logger;
            _queryEngine = queryEngine;
            _mutationEngine = mutationEngine;
            _schemaManager = schemaManager;
        }

        [HttpGet]
        public IEnumerable<JObject> Get()
        {
            JObject data =JObject.Parse(JsonData);
            return Enumerable.Repeat(data, 1);
        }
        
        [Route("addResolver")]
        [HttpPost]
        public void addResolver(GraphQLQueryResolver resolver)
        {
           _queryEngine.RegisterResolver(resolver);
           _schemaManager.attachQueryResolverToSchema(resolver.id);
        }
        
        [Route("addMutationResolver")]
        [HttpPost]
        public void addMutationResolver(MutationResolver resolver)
        {
            _mutationEngine.registerResolver(resolver);
            _schemaManager.attachMutationResolverToSchema(resolver.id);
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
        public async Task<JsonDocument> Post()
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
