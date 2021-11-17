using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Resolvers;
using Azure.DataGateway.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Cosmos;
using System;
using System.IO;
using System.Net.Http;
using System.Text;

namespace Azure.DataGateway.Service.Tests
{
    public class TestBase
    {
        internal GraphQLService _graphQLService;
        internal CosmosClientProvider _clientProvider;
        internal IMetadataStoreProvider _metadataStoreProvider;
        internal CosmosQueryEngine _queryEngine;
        internal CosmosMutationEngine _mutationEngine;
        internal GraphQLController _controller;

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
            _controller = new GraphQLController(_graphQLService);
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
