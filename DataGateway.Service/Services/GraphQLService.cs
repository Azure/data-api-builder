using Azure.DataGateway.Service.Resolvers;
using HotChocolate;
using HotChocolate.Execution;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace Azure.DataGateway.Services
{
    public class GraphQLService
    {
        private readonly IQueryEngine _queryEngine;
        private readonly IMutationEngine _mutationEngine;
        private IMetadataStoreProvider _metadataStoreProvider;

        public GraphQLService(IQueryEngine queryEngine,
            IMutationEngine mutationEngine,
            IMetadataStoreProvider metadataStoreProvider)
        {
            _queryEngine = queryEngine;
            _mutationEngine = mutationEngine;
            _metadataStoreProvider = metadataStoreProvider;

            InitializeSchemaAndResolvers();
        }

        public void ParseAsync(String data)
        {
            Executor = SchemaBuilder.New()
                .AddDocumentFromString(data)
                .Use((services, next) => new ResolverMiddleware(next, _queryEngine, _mutationEngine))
                .Create()
                .MakeExecutable();
        }

        public IRequestExecutor Executor { get; private set; }

        internal async Task<string> ExecuteAsync(String requestBody)
        {
            if (Executor == null)
            {
                return "{\"error\": \"Schema must be defined first\" }";
            }

            var req = JsonDocument.Parse(requestBody);
            IQueryRequest queryRequest = QueryRequestBuilder.New()
                .SetQuery(req.RootElement.GetProperty("query").GetString())
                .Create();

            IExecutionResult result =
                await Executor.ExecuteAsync(queryRequest);

            return result.ToJson(withIndentations: false);
        }

        /// <summary>
        /// If the metastore provider is able to get the graphql schema,
        /// this function parses it and attaches resolvers to the various query fields.
        /// </summary>
        private void InitializeSchemaAndResolvers()
        {
            // Attempt to get schema from the metadata store.
            //
            string graphqlSchema = _metadataStoreProvider.GetGraphQLSchema();

            // If the schema is available, parse it and attach resolvers.
            //
            if (!string.IsNullOrEmpty(graphqlSchema))
            {
                ParseAsync(graphqlSchema);
            }
        }

    }
}
