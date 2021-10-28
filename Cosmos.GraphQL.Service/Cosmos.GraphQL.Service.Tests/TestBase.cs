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
        protected GraphQLService _graphQLService;
        protected CosmosClientProvider _clientProvider;
        protected IMetadataStoreProvider _metadataStoreProvider;
        protected CosmosQueryEngine _queryEngine;
        protected CosmosMutationEngine _mutationEngine;
        protected GraphQLController _controller;

        public TestBase()
        {
            Init();

        }

        private void Init()
        {
            _clientProvider = new CosmosClientProvider(TestHelper.DataGatewayConfig);
            string uid = Guid.NewGuid().ToString();
            dynamic sourceItem = TestHelper.GetItem(uid);

            _clientProvider.Client.GetContainer(TestHelper.DB_NAME, TestHelper.COL_NAME).CreateItemAsync(sourceItem, new PartitionKey(uid));
            _metadataStoreProvider = new MetadataStoreProviderForTest();
            _queryEngine = new CosmosQueryEngine(_clientProvider, _metadataStoreProvider);
            _mutationEngine = new CosmosMutationEngine(_clientProvider, _metadataStoreProvider);
            _graphQLService = new GraphQLService(_queryEngine, _mutationEngine, _metadataStoreProvider);
            _graphQLService.ParseAsync(TestHelper.GraphQLTestSchema);
            _controller = new GraphQLController(null, _queryEngine, _mutationEngine, _graphQLService);
        }

        internal static DefaultHttpContext GetHttpContextWithBody(string data)
        {
            HttpRequestMessage request = new HttpRequestMessage();
            MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(data));
            request.Method = HttpMethod.Post;
            request.Content = new StringContent(TestHelper.SampleQuery);
            DefaultHttpContext httpContext = new DefaultHttpContext()
            {
                Request = { Body = stream, ContentLength = stream.Length }
            };
            return httpContext;
        }
    }
}
