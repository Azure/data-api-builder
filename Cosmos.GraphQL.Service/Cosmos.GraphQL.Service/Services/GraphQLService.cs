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
        private ISchema _schema;
        private readonly QueryEngine _queryEngine;
        private readonly MutationEngine _mutationEngine;
        private IMetadataStoreProvider _metadataStoreProvider;

        public GraphQLService(QueryEngine queryEngine, MutationEngine mutationEngine, CosmosClientProvider clientProvider, IMetadataStoreProvider metadataStoreProvider)
        {
            this._queryEngine = queryEngine;
            this._mutationEngine = mutationEngine;
            this._metadataStoreProvider = metadataStoreProvider;
        }

        public void parseAsync(String data)
        {
            ISchema schema = SchemaBuilder.New()
                .AddDocumentFromString(data)
                .Use((services, next) => new ResolverMiddleware(next, _queryEngine, _mutationEngine))
                .Create();
            _schema = schema;
            this._metadataStoreProvider.StoreGraphQLSchema(data);
        }

        public ISchema Schema
        {
            get { return _schema; }
        }

        internal async Task<string> ExecuteAsync(String requestBody)
        {
            if (this.Schema == null)
            {
                return "{\"error\": \"Schema must be defined first\" }";
            }
            JsonDocument req = JsonDocument.Parse(requestBody);
            IQueryRequest queryRequest = QueryRequestBuilder.New()
                .SetQuery(req.RootElement.GetProperty("query").GetString())
                .Create();

            IRequestExecutor executor = Schema.MakeExecutable();
            IExecutionResult result =
                await executor.ExecuteAsync(queryRequest);
            
            return result.ToJson();
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
    }
}
