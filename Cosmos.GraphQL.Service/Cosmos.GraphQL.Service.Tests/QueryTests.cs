using Cosmos.GraphQL.Service.Controllers;
using Cosmos.GraphQL.Service.Models;
using Cosmos.GraphQL.Service.Resolvers;
using Cosmos.GraphQL.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Cosmos;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Cosmos.GraphQL.Service.Tests
{
    [TestClass]
    public class QueryTests
    {


        [TestMethod]
        public async Task TestQueryRun()
        {

            GraphQLService graphQLService;
            QueryEngine queryEngine;
            GraphQLController controller;
            CosmosClientProvider clientProvider = new CosmosClientProvider();
            string uid = Guid.NewGuid().ToString();
            dynamic sourceItem = TestHelper.GetItem(uid);
            await clientProvider.getCosmosContainer().CreateItemAsync(sourceItem, new PartitionKey(uid));
            
            MetadataStoreProvider metadataStoreProvider = new CachedMetadataStoreProvider(new DocumentMetadataStoreProvider(clientProvider));
            queryEngine = new QueryEngine(clientProvider, metadataStoreProvider);
            var mutationEngine = new MutationEngine(clientProvider, metadataStoreProvider);
            graphQLService = new GraphQLService(queryEngine, mutationEngine, clientProvider, metadataStoreProvider);
            graphQLService.parseAsync(TestHelper.GraphQLTestSchema);
            controller = new GraphQLController(null, queryEngine, mutationEngine, graphQLService);
            var request = new HttpRequestMessage();

            // Add query resolver
            controller.addResolver(TestHelper.SampleQueryResolver());

            // Run query
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(TestHelper.SampleQuery));
            request.Method = HttpMethod.Post;
            request.Content = new StringContent(TestHelper.SampleQuery);
            var httpContext = new DefaultHttpContext()
            {
                Request = { Body = stream, ContentLength = stream.Length }
            };
            controller.ControllerContext.HttpContext = httpContext;
            string response = await controller.Post();
            Assert.IsFalse(response.Contains("Error"));
            JObject resultObj = JObject.Parse(response);
        }

       /* [ClassInitialize]
        public void Init()
        {

        }
       */

    }
}
