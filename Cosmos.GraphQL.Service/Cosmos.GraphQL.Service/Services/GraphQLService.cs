using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Cosmos.GraphQL.Service.Resolvers;
using HotChocolate;
using HotChocolate.Execution;

namespace Cosmos.GraphQL.Services
{
    public class GraphQLService
    {
        private IRequestExecutor _executor;
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

        public void parseAsync(String data)
        {
            _executor = SchemaBuilder.New()
                .AddDocumentFromString(data)
                .Use((services, next) => new ResolverMiddleware(next, _queryEngine, _mutationEngine))
                .Create()
                .MakeExecutable();
        }

        public IRequestExecutor Executor
        {
            get { return _executor; }
        }

        internal async Task<string> ExecuteAsync(String requestBody)
        {
            if (_executor == null)
            {
                return "{\"error\": \"Schema must be defined first\" }";
            }
            JsonDocument req = JsonDocument.Parse(requestBody);
            IQueryRequest queryRequest = QueryRequestBuilder.New()
                .SetQuery(req.RootElement.GetProperty("query").GetString())
                .Create();

            IExecutionResult result =
                await Executor.ExecuteAsync(queryRequest);

            return result.ToJson(false);
        }

        private static bool IsIntrospectionPath(IEnumerable<object> path)
        {
            if (path.Any())
            {
                var firstPath = path.First() as string;
                if (firstPath.StartsWith("__", StringComparison.InvariantCulture))
                {
                    return true;
                }
            }

            return false;
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
                parseAsync(graphqlSchema);
            }
        }

    }
}
