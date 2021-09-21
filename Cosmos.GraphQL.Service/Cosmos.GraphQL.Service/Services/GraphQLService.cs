using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Cosmos.GraphQL.Service.Resolvers;
using Cosmos.GraphQL.Service.configurations;
using HotChocolate;
using HotChocolate.Execution;

namespace Cosmos.GraphQL.Services
{
    public partial class GraphQLService
    {
        private ISchema _schema;
        private readonly IQueryEngine _queryEngine;
        private readonly IMutationEngine _mutationEngine;
        private IMetadataStoreProvider _metadataStoreProvider;

        public GraphQLService(IQueryEngine queryEngine, IMutationEngine mutationEngine, IMetadataStoreProvider metadataStoreProvider)
        {
            this._queryEngine = queryEngine;
            this._mutationEngine = mutationEngine;
            this._metadataStoreProvider = metadataStoreProvider;

            if (ConfigurationProvider.getInstance().DbType != DatabaseType.Cosmos)
            {
                InitializeSchemaAndResolvers();
            }
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
