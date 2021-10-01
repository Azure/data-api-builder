using Cosmos.GraphQL.Service.Controllers;
using Cosmos.GraphQL.Service.Resolvers;
using Cosmos.GraphQL.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Moq;
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

        public FunctionContext FunctionContext { get; private set; }
        public Mock<HttpResponseData> ResponseDataMock { get; private set; }
        public Mock<HttpRequestData> RequestDataMock { get; private set; }

        /// <summary>
        /// Create FunctionContext, HttpResponseData mock and HttpRequestData mock.
        /// These are needed to call Function app.
        /// </summary>
        private void CreateHttpMockObject()
        {
            FunctionContext = TestFunctionContext.Create();
            ResponseDataMock = new Mock<HttpResponseData>(MockBehavior.Loose, FunctionContext);
            MemoryStream responseBodyStream = new MemoryStream();
            ResponseDataMock.Setup(x => x.Body).Returns(responseBodyStream);
            HttpHeadersCollection headers = new HttpHeadersCollection();
            ResponseDataMock.Setup(x => x.Headers).Returns(headers);

            RequestDataMock = new Mock<HttpRequestData>(MockBehavior.Loose, FunctionContext);
            RequestDataMock.Setup(x => x.CreateResponse()).Returns(ResponseDataMock.Object);
            MemoryStream requestBodyStream = new MemoryStream();
            RequestDataMock.Setup(x => x.Body).Returns(requestBodyStream);
        }

        public TestBase()
        {
            Init();

        }

        private void Init()
        {
            _clientProvider = new CosmosClientProvider(TestHelper.DataGatewayConfig);
            string uid = Guid.NewGuid().ToString();
            dynamic sourceItem = TestHelper.GetItem(uid);

            _clientProvider.GetClient().GetContainer(TestHelper.DB_NAME, TestHelper.COL_NAME).CreateItemAsync(sourceItem, new PartitionKey(uid));
            _metadataStoreProvider = new MetadataStoreProviderForTest();
            _queryEngine = new CosmosQueryEngine(_clientProvider, _metadataStoreProvider);
            _mutationEngine = new CosmosMutationEngine(_clientProvider, _metadataStoreProvider);
            _graphQLService = new GraphQLService(_queryEngine, _mutationEngine, _metadataStoreProvider);
            _graphQLService.parseAsync(TestHelper.GraphQLTestSchema);
            _controller = new GraphQLController(null, _queryEngine, _mutationEngine, _graphQLService);

            CreateHttpMockObject();
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
