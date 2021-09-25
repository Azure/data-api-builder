using Cosmos.GraphQL.Service.configurations;
using Cosmos.GraphQL.Service.Controllers;
using Cosmos.GraphQL.Service.Resolvers;
using Cosmos.GraphQL.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Cosmos;
using System;
using System.IO;
using System.Net.Http;
using System.Text;

namespace Cosmos.GraphQL.Service.Tests
{
    public class TestBase
    {
        internal GraphQLService graphQLService;
        internal CosmosClientProvider clientProvider;
        internal IMetadataStoreProvider metadataStoreProvider;
        internal CosmosQueryEngine queryEngine;
        internal CosmosMutationEngine mutationEngine;
        internal GraphQLController controller;

        public TestBase()
        {
            Init();

        }

        private void Init()
        {
            TestHelper.LoadConfig();
            clientProvider = new CosmosClientProvider(TestHelper.DataGatewayConfig);
            string uid = Guid.NewGuid().ToString();
            dynamic sourceItem = TestHelper.GetItem(uid);
            clientProvider.GetClient().GetContainer(TestHelper.DB_NAME, TestHelper.COL_NAME).CreateItemAsync(sourceItem, new PartitionKey(uid));
            metadataStoreProvider = new CachedMetadataStoreProvider(new DocumentMetadataStoreProvider(clientProvider));
            queryEngine = new CosmosQueryEngine(clientProvider, metadataStoreProvider);
            mutationEngine = new CosmosMutationEngine(clientProvider, metadataStoreProvider);
            graphQLService = new GraphQLService(queryEngine, mutationEngine, metadataStoreProvider, TestHelper.DataGatewayConfig);
            graphQLService.parseAsync(TestHelper.GraphQLTestSchema);
            controller = new GraphQLController(null, queryEngine, mutationEngine, graphQLService);
        }

        internal static DefaultHttpContext GetHttpContextWithBody(string data)
        {
            var request = new HttpRequestMessage();
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(data));
            request.Method = HttpMethod.Post;
            request.Content = new StringContent(TestHelper.SampleQuery);
            var httpContext = new DefaultHttpContext()
            {
                Request = { Body = stream, ContentLength = stream.Length }
            };
            return httpContext;
        }


    }
}
