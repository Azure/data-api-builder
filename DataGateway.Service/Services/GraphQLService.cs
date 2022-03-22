using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.GraphQLBuilder;
using Azure.DataGateway.Service.GraphQLBuilder.Mutations;
using Azure.DataGateway.Service.GraphQLBuilder.Queries;
using Azure.DataGateway.Service.Resolvers;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Execution.Configuration;
using HotChocolate.Language;
using HotChocolate.Types;
using HotChocolate.Utilities;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.DataGateway.Service.Services
{
    public class GraphQLService
    {
        private readonly IQueryEngine _queryEngine;
        private readonly IMutationEngine _mutationEngine;
        private readonly IMetadataStoreProvider _metadataStoreProvider;
        public ISchema? Schema { private set; get; }
        public IRequestExecutor? Executor { private set; get; }

        public GraphQLService(
            IQueryEngine queryEngine,
            IMutationEngine mutationEngine,
            IMetadataStoreProvider metadataStoreProvider)
        {
            _queryEngine = queryEngine;
            _mutationEngine = mutationEngine;
            _metadataStoreProvider = metadataStoreProvider;

            InitializeSchemaAndResolvers();
        }

        public void ParseAsync(string data)
        {
            DocumentNode root = Utf8GraphQLParser.Parse(data);

            ISchemaBuilder sb = SchemaBuilder.New()
                .AddDocument(root)
                .AddDirectiveType(CustomDirectives.ModelTypeDirective())
                .AddDocument(QueryBuilder.Build(root))
                .AddDocument(MutationBuilder.Build(root));

            Schema = sb
                .AddAuthorizeDirectiveType()
                .Use((services, next) => new ResolverMiddleware(next, _queryEngine, _mutationEngine, _metadataStoreProvider))
                .Create();

            // Below is pretty much an inlined version of
            // ISchema.MakeExecutable. The reason that we inline it is that
            // same changes need to be made in the middle of it, such as
            // AddErrorFilter.
            IRequestExecutorBuilder builder = new ServiceCollection()
                .AddGraphQL()
                .AddAuthorization()
                .AddErrorFilter(error =>
            {
                if (error.Exception != null)
                {
                    Console.Error.WriteLine(error.Exception.Message);
                    Console.Error.WriteLine(error.Exception.StackTrace);
                }

                return error;
            })
                .AddErrorFilter(error =>
            {
                if (error.Exception is DataGatewayException)
                {
                    DataGatewayException thrownException = (DataGatewayException)error.Exception;
                    return error.RemoveException()
                            .RemoveLocations()
                            .RemovePath()
                            .WithMessage(thrownException.Message)
                            .WithCode($"{thrownException.SubStatusCode}");
                }

                return error;
            });

            // Sadly IRequestExecutorBuilder.Configure is internal, so we also
            // inline that one here too
            Executor = builder.Services
                .Configure(builder.Name, (RequestExecutorSetup o) => o.Schema = Schema)
                .BuildServiceProvider()
                .GetRequiredService<IRequestExecutorResolver>()
                .GetRequestExecutorAsync()
                .Result;
        }

        /// <summary>
        /// Executes GraphQL request within GraphQL Library components.
        /// </summary>
        /// <param name="requestBody">GraphQL request body</param>
        /// <param name="requestProperties">key/value pairs of properties to be used in GraphQL library pipeline</param>
        /// <returns></returns>
        public async Task<string> ExecuteAsync(string requestBody, Dictionary<string, object> requestProperties)
        {
            if (Executor == null)
            {
                /*lang=json,strict*/
                return "{\"error\": \"Schema must be defined first\" }";
            }

            IQueryRequest queryRequest = CompileRequest(requestBody, requestProperties);

            using IExecutionResult result = await Executor.ExecuteAsync(queryRequest);
            return result.ToJson(withIndentations: false);
        }

        /// <summary>
        /// If the metastore provider is able to get the graphql schema,
        /// this function parses it and attaches resolvers to the various query fields.
        /// </summary>
        private void InitializeSchemaAndResolvers()
        {
            // Attempt to get schema from the metadata store.
            string graphqlSchema = _metadataStoreProvider.GetGraphQLSchema();

            // If the schema is available, parse it and attach resolvers.
            if (!string.IsNullOrEmpty(graphqlSchema))
            {
                ParseAsync(graphqlSchema);
            }
        }

        /// <summary>
        /// Adds request properties(i.e. AuthN details) as GraphQL QueryRequest properties so key/values
        /// can be used in HotChocolate Middleware.
        /// </summary>
        /// <param name="requestBody">Http Request Body</param>
        /// <param name="requestProperties">Key/Value Pair of Https Headers intended to be used in GraphQL service</param>
        /// <returns></returns>
        private static IQueryRequest CompileRequest(string requestBody, Dictionary<string, object> requestProperties)
        {
            byte[] graphQLData = Encoding.UTF8.GetBytes(requestBody);
            ParserOptions _parserOptions = new();
            IDocumentHashProvider _documentHashProvider = new Sha256DocumentHashProvider();
            IDocumentCache _documentCache = new DocumentCache();

            Utf8GraphQLRequestParser requestParser = new(
                graphQLData,
                _parserOptions,
                _documentCache,
                _documentHashProvider);

            IReadOnlyList<GraphQLRequest> parsed = requestParser.Parse();

            // TODO: Overhaul this to support batch queries
            // right now we have only assumed a single query/mutation in the request
            // but HotChocolate supports batching and we're just ignoring it for now
            QueryRequestBuilder requestBuilder = QueryRequestBuilder.From(parsed[0]);

            // Individually adds each property to requestBuilder if they are provided.
            // Avoids using SetProperties() as it detrimentally overwrites
            // any properties other Middleware sets.
            if (requestProperties != null)
            {
                foreach (KeyValuePair<string, object> property in requestProperties)
                {
                    requestBuilder.AddProperty(property.Key, property.Value);
                }
            }

            return requestBuilder.Create();
        }
    }

    // This class shouldn't be inlined like this
    // need to review this service to better leverage
    // HotChocolate and how it handles things such as
    // caching, but one change at a time.
    class DocumentCache : IDocumentCache
    {
        private readonly Cache<DocumentNode> _cache;

        public DocumentCache(int capacity = 100)
        {
            _cache = new Cache<DocumentNode>(capacity);
        }

        public int Capacity => _cache.Size;

        public int Count => _cache.Usage;

        public void TryAddDocument(
            string documentId,
            DocumentNode document) =>
            _cache.GetOrCreate(documentId, () => document);

        public bool TryGetDocument(
            string documentId,
            [NotNullWhen(true)] out DocumentNode document) =>
            _cache.TryGet(documentId, out document!);

        public void Clear() => _cache.Clear();
    }
}
