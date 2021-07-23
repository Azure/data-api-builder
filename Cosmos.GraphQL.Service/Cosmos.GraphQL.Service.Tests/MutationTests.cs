using Cosmos.GraphQL.Service.Controllers;
using Cosmos.GraphQL.Service.Models;
using Cosmos.GraphQL.Service.Resolvers;
using Cosmos.GraphQL.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Newtonsoft.Json;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Cosmos.GraphQL.Service.Tests
{
    [TestClass]
    public class MutationTests
    {


        [TestMethod]
        public async Task TestMutationRun()
        {

            GraphQLService graphQLService;
            QueryEngine queryEngine;
            GraphQLController controller;
            CosmosClientProvider clientProvider = new CosmosClientProvider();
            queryEngine = new QueryEngine(clientProvider);
            var mutationEngine = new MutationEngine(clientProvider);
            graphQLService = new GraphQLService(queryEngine, mutationEngine, clientProvider);
            graphQLService.parseAsync(TestHelper.GraphQLTestSchema);
            controller = new GraphQLController(null, queryEngine, mutationEngine, graphQLService);
            var request = new HttpRequestMessage();

            // Add query resolver
            controller.addMutationResolver(TestHelper.SampleMutationResolver());

            // Run query
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(TestHelper.SampleMutation));
            request.Method = HttpMethod.Post;
            request.Content = new StringContent(TestHelper.SampleMutation);
            var httpContext = new DefaultHttpContext()
            {
                Request = { Body = stream, ContentLength = stream.Length }
            };
            controller.ControllerContext.HttpContext = httpContext;
            JsonDocument response = await controller.Post();
            Assert.IsFalse(response.ToString().Contains("Error"));
        }

       /* [ClassInitialize]
        public void Init()
        {

        }
       */

    }
}
